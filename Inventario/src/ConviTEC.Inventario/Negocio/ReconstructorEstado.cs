using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Negocio;

/// Reconstruye, entrada por entrada (RNF16), las secciones del snapshot posteriores al stock: lotes, comandos,
/// reposiciones y salidas pendientes. Valida antes de instalar; cualquier problema se reporta con archivo y posición.
/// Una sección ausente (snapshot anterior a RF02) equivale a una sección vacía.
internal static class ReconstructorEstado
{
    /// El stock de <paramref name="estado"/> ya debe estar cargado: lotes, reposiciones y salidas se validan contra él.
    public static void CargarSecciones(EstadoInventarioDto dto, EstadoInventario estado, string sedeId, string archivo)
    {
        CargarLotes(dto.Lotes, estado, archivo);
        CargarComandos(dto.Comandos, estado, archivo);
        CargarReposiciones(dto.Reposiciones, estado, archivo);
        CargarSalidas(dto.SalidasPendientes, estado, sedeId, archivo);
    }

    private static void CargarLotes(LoteGuardadoDto?[]? lotes, EstadoInventario estado, string archivo)
    {
        if (lotes == null)
        {
            return;
        }

        for (int i = 0; i < lotes.Length; i++)
        {
            string posicion = $"lotes[{i}]";
            LoteGuardadoDto lote = lotes[i] ?? throw new DatosInventarioInvalidosException(archivo, $"{posicion} es nulo.");

            string pedidoId = CamposObligatorios.Texto(lote.PedidoId, "pedidoId", archivo, posicion);
            string coordinadora = CamposObligatorios.Texto(lote.CoordinadoraSedeId, "coordinadoraSedeId", archivo, posicion);
            EstadoReserva estadoLote = LeerEstado(lote.Estado, archivo, posicion);
            if (lote.Lineas == null)
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion}: falta 'lineas'.");
            }

            LineaReserva[] lineas = new LineaReserva[lote.Lineas.Length];
            for (int j = 0; j < lineas.Length; j++)
            {
                lineas[j] = ConvertirLinea(lote.Lineas[j], archivo, $"{posicion}.lineas[{j}]");
            }

            bool retiene = estadoLote == EstadoReserva.Reservada || estadoLote == EstadoReserva.Confirmada;
            if (retiene)
            {
                ValidarLineasDeLoteVigente(lineas, estado, archivo, posicion);
            }

            LoteReserva convertido;
            try
            {
                convertido = new LoteReserva(pedidoId, coordinadora, estadoLote, lineas);
            }
            catch (ArgumentException e)
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion}: {e.Message}", e);
            }
            InsertarSinDuplicar(estado.Lotes, pedidoId, convertido, archivo, posicion, "pedido");
        }
    }

    /// Un lote que retiene unidades (RESERVADA o CONFIRMADA) debe referir productos de la sede y no repetirlos.
    /// Un lote RECHAZADA puede contener justamente líneas inválidas, por eso no se le exige.
    private static void ValidarLineasDeLoteVigente(LineaReserva[] lineas, EstadoInventario estado, string archivo, string posicion)
    {
        for (int j = 0; j < lineas.Length; j++)
        {
            if (!estado.Stock.Contiene(lineas[j].ProductoId))
            {
                throw new DatosInventarioInvalidosException(archivo,
                    $"{posicion}.lineas[{j}]: el producto '{lineas[j].ProductoId}' no existe en esta sede.");
            }
            for (int k = 0; k < j; k++)
            {
                if (string.Equals(lineas[k].ProductoId, lineas[j].ProductoId, StringComparison.Ordinal))
                {
                    throw new DatosInventarioInvalidosException(archivo,
                        $"{posicion}.lineas[{j}]: producto repetido '{lineas[j].ProductoId}'.");
                }
            }
        }
    }

    private static LineaReserva ConvertirLinea(LineaReservaDto? dto, string archivo, string posicion)
    {
        if (dto == null)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion} es nulo.");
        }

        string productoId = CamposObligatorios.Texto(dto.ProductoId, "productoId", archivo, posicion);
        int cantidad = CamposObligatorios.Valor(dto.Cantidad, "cantidad", archivo, posicion);
        long precio = CamposObligatorios.Valor(dto.PrecioUnitarioCentimos, "precioUnitarioCentimos", archivo, posicion);
        try
        {
            return new LineaReserva(productoId, cantidad, precio);
        }
        catch (ArgumentException e)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: {e.Message}", e);
        }
    }

    private static void CargarComandos(ComandoGuardadoDto?[]? comandos, EstadoInventario estado, string archivo)
    {
        if (comandos == null)
        {
            return;
        }

        for (int i = 0; i < comandos.Length; i++)
        {
            string posicion = $"comandos[{i}]";
            ComandoGuardadoDto comando = comandos[i] ?? throw new DatosInventarioInvalidosException(archivo, $"{posicion} es nulo.");

            string mensajeId = CamposObligatorios.Texto(comando.MensajeId, "mensajeId", archivo, posicion);
            string tipo = CamposObligatorios.Texto(comando.Tipo, "tipo", archivo, posicion);
            string origen = CamposObligatorios.Texto(comando.OrigenSedeId, "origenSedeId", archivo, posicion);
            string contenido = CamposObligatorios.TextoPresente(comando.Contenido, "contenido", archivo, posicion);
            ResultadoStock resultado = ConvertirResultado(comando.Resultado, archivo, posicion + ".resultado");

            ComandoRegistrado convertido;
            try
            {
                convertido = new ComandoRegistrado(mensajeId, tipo, origen, contenido, resultado);
            }
            catch (ArgumentException e)
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion}: {e.Message}", e);
            }
            InsertarSinDuplicar(estado.Comandos, mensajeId, convertido, archivo, posicion, "mensajeId");
        }
    }

    private static ResultadoStock ConvertirResultado(ResultadoStockDto? dto, string archivo, string posicion)
    {
        if (dto == null)
        {
            throw new DatosInventarioInvalidosException(archivo, $"falta '{posicion}'.");
        }

        string pedidoId = CamposObligatorios.TextoPresente(dto.PedidoId, "pedidoId", archivo, posicion);
        string operacion = CamposObligatorios.Texto(dto.Operacion, "operacion", archivo, posicion);
        EstadoReserva estadoReserva = LeerEstado(dto.EstadoReserva, archivo, posicion);
        string codigo = CamposObligatorios.Texto(dto.Codigo, "codigo", archivo, posicion);
        string mensaje = CamposObligatorios.TextoPresente(dto.Mensaje, "mensaje", archivo, posicion);
        try
        {
            return new ResultadoStock(pedidoId, operacion, estadoReserva, codigo, mensaje);
        }
        catch (ArgumentException e)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: {e.Message}", e);
        }
    }

    private static void CargarReposiciones(ReposicionGuardadaDto?[]? reposiciones, EstadoInventario estado, string archivo)
    {
        if (reposiciones == null)
        {
            return;
        }

        for (int i = 0; i < reposiciones.Length; i++)
        {
            string posicion = $"reposiciones[{i}]";
            ReposicionGuardadaDto reposicion = reposiciones[i] ?? throw new DatosInventarioInvalidosException(archivo, $"{posicion} es nulo.");

            string operacionId = CamposObligatorios.Texto(reposicion.OperacionId, "operacionId", archivo, posicion);
            string productoId = CamposObligatorios.Texto(reposicion.ProductoId, "productoId", archivo, posicion);
            int cantidad = CamposObligatorios.Valor(reposicion.Cantidad, "cantidad", archivo, posicion);
            int existencia = CamposObligatorios.Valor(reposicion.Existencia, "existencia", archivo, posicion);
            int reservada = CamposObligatorios.Valor(reposicion.Reservada, "reservada", archivo, posicion);
            ExigirProductoDeLaSede(estado, productoId, archivo, posicion);

            ReposicionRegistrada convertida;
            try
            {
                convertida = new ReposicionRegistrada(operacionId, productoId, cantidad, existencia, reservada);
            }
            catch (ArgumentException e)
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion}: {e.Message}", e);
            }
            InsertarSinDuplicar(estado.Reposiciones, operacionId, convertida, archivo, posicion, "operación");
        }
    }

    private static void CargarSalidas(SalidaPendienteGuardadaDto?[]? salidas, EstadoInventario estado, string sedeId, string archivo)
    {
        if (salidas == null)
        {
            return;
        }

        for (int i = 0; i < salidas.Length; i++)
        {
            string posicion = $"salidasPendientes[{i}]";
            SalidaPendienteGuardadaDto salida = salidas[i] ?? throw new DatosInventarioInvalidosException(archivo, $"{posicion} es nulo.");

            string mensajeId = CamposObligatorios.Texto(salida.MensajeId, "mensajeId", archivo, posicion);
            string destino = CamposObligatorios.Texto(salida.DestinoSedeId, "destinoSedeId", archivo, posicion);
            string productoId = CamposObligatorios.Texto(salida.ProductoId, "productoId", archivo, posicion);
            int disponible = CamposObligatorios.Valor(salida.Disponible, "disponible", archivo, posicion);
            long revision = CamposObligatorios.Valor(salida.Revision, "revision", archivo, posicion);
            if (string.Equals(destino, sedeId, StringComparison.Ordinal))
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion}: la sede no se publica a sí misma ('{destino}').");
            }
            ExigirProductoDeLaSede(estado, productoId, archivo, posicion);

            SalidaPendiente convertida;
            try
            {
                convertida = new SalidaPendiente(mensajeId, destino, productoId, disponible, revision);
            }
            catch (ArgumentException e)
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion}: {e.Message}", e);
            }
            InsertarSinDuplicar(estado.Salidas, convertida.Clave, convertida, archivo, posicion, "destino y producto");
        }
    }

    private static EstadoReserva LeerEstado(string? texto, string archivo, string posicion)
    {
        if (!TextoEstadoReserva.TryParse(texto, out EstadoReserva? estado))
        {
            throw new DatosInventarioInvalidosException(archivo,
                $"{posicion}: estado de reserva desconocido '{texto ?? "(falta)"}'.");
        }
        return estado.Value;
    }

    private static void ExigirProductoDeLaSede(EstadoInventario estado, string productoId, string archivo, string posicion)
    {
        if (!estado.Stock.Contiene(productoId))
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: el producto '{productoId}' no existe en esta sede.");
        }
    }

    private static void InsertarSinDuplicar<T>(
        ArbolAvl<string, T> indice, string clave, T valor, string archivo, string posicion, string descripcionClave)
    {
        if (!indice.Insertar(clave, valor))
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: {descripcionClave} duplicado.");
        }
    }
}
