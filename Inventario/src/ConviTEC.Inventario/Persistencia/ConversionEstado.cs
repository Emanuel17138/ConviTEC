using System;
using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Persistencia;

/// Convierte el estado de dominio al DTO del snapshot. Solo usa arreglos nativos. La conversión inversa
/// (con validación) vive en la capa de negocio, que decide qué estado es aceptable.
public static class ConversionEstado
{
    /// Arma el snapshot completo. Las secciones que no se pasan quedan como arreglos vacíos.
    public static EstadoInventarioDto Construir(
        string sedeId,
        EntradaStock[] stock,
        LoteReserva[]? lotes = null,
        ComandoRegistrado[]? comandos = null,
        ReposicionRegistrada[]? reposiciones = null,
        SalidaPendiente[]? salidas = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(stock, nameof(stock));

        return new EstadoInventarioDto
        {
            Version = EstadoInventarioDto.VersionActual,
            SedeId = sedeId,
            Stock = Convertir(stock, ADto),
            Lotes = Convertir(lotes ?? Array.Empty<LoteReserva>(), ADto),
            Comandos = Convertir(comandos ?? Array.Empty<ComandoRegistrado>(), ADto),
            Reposiciones = Convertir(reposiciones ?? Array.Empty<ReposicionRegistrada>(), ADto),
            SalidasPendientes = Convertir(salidas ?? Array.Empty<SalidaPendiente>(), ADto),
        };
    }

    /// Arma el contenido de catalogo-replicado.json: ofertas (ya en orden) y sedes con primera instantánea.
    public static CatalogoReplicadoDto ConstruirCatalogo(string sedeId, string[] sedesSincronizadas, OfertaReplicada[] ofertas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(sedesSincronizadas, nameof(sedesSincronizadas));
        ArgumentNullException.ThrowIfNull(ofertas, nameof(ofertas));

        return new CatalogoReplicadoDto
        {
            Version = CatalogoReplicadoDto.VersionActual,
            SedeId = sedeId,
            SedesSincronizadas = (string[])sedesSincronizadas.Clone(),
            Ofertas = Convertir(ofertas, oferta => new OfertaReplicadaGuardadaDto
            {
                SedeId = oferta.SedeId,
                Producto = ADto(oferta.Producto),
                Disponible = oferta.Disponible,
                Revision = oferta.Revision,
            }),
        };
    }

    private static TDto[] Convertir<TOrigen, TDto>(TOrigen[] origen, Func<TOrigen, TDto> convertir)
    {
        TDto[] destino = new TDto[origen.Length];
        for (int i = 0; i < origen.Length; i++)
        {
            destino[i] = convertir(origen[i]);
        }
        return destino;
    }

    private static ProductoDto ADto(Producto producto) => new ProductoDto
    {
        ProductoId = producto.ProductoId,
        Nombre = producto.Nombre,
        Descripcion = producto.Descripcion,
        Categoria = producto.Categoria,
        PrecioUnitarioCentimos = producto.PrecioUnitarioCentimos,
    };

    private static StockGuardadoDto ADto(EntradaStock entrada)
    {
        return new StockGuardadoDto
        {
            Producto = ADto(entrada.Stock.Producto),
            Existencia = entrada.Stock.Existencia,
            Reservada = entrada.Stock.Reservada,
            Revision = entrada.Revision,
        };
    }

    private static LoteGuardadoDto ADto(LoteReserva lote) => new LoteGuardadoDto
    {
        PedidoId = lote.PedidoId,
        CoordinadoraSedeId = lote.CoordinadoraSedeId,
        Estado = TextoEstadoReserva.ATexto(lote.Estado),
        Lineas = Convertir(lote.Lineas(), linea => new LineaReservaDto
        {
            ProductoId = linea.ProductoId,
            Cantidad = linea.Cantidad,
            PrecioUnitarioCentimos = linea.PrecioUnitarioCentimos,
        }),
    };

    private static ComandoGuardadoDto ADto(ComandoRegistrado comando) => new ComandoGuardadoDto
    {
        MensajeId = comando.MensajeId,
        Tipo = comando.Tipo,
        OrigenSedeId = comando.OrigenSedeId,
        Contenido = comando.Contenido,
        Resultado = new ResultadoStockDto
        {
            PedidoId = comando.Resultado.PedidoId,
            Operacion = comando.Resultado.Operacion,
            EstadoReserva = TextoEstadoReserva.ATexto(comando.Resultado.EstadoReserva),
            Codigo = comando.Resultado.Codigo,
            Mensaje = comando.Resultado.Mensaje,
        },
    };

    private static ReposicionGuardadaDto ADto(ReposicionRegistrada reposicion) => new ReposicionGuardadaDto
    {
        OperacionId = reposicion.OperacionId,
        ProductoId = reposicion.ProductoId,
        Cantidad = reposicion.Cantidad,
        Existencia = reposicion.Existencia,
        Reservada = reposicion.Reservada,
    };

    private static SalidaPendienteGuardadaDto ADto(SalidaPendiente salida) => new SalidaPendienteGuardadaDto
    {
        MensajeId = salida.MensajeId,
        DestinoSedeId = salida.DestinoSedeId,
        ProductoId = salida.ProductoId,
        Disponible = salida.Disponible,
        Revision = salida.Revision,
    };
}
