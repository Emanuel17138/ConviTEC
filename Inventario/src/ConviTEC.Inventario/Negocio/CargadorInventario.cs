using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Negocio;

/// Si existe estado guardado, recupera sus cantidades y no toca las semillas. 
/// Si no, lee el maestro y las existencias iniciales y guarda el primer snapshot.
public sealed class CargadorInventario
{
    public ResultadoArranque Cargar(RutasArchivosInventario rutas) => Cargar(rutas, Array.Empty<string>());

    /// <paramref name="sedesRemotas"/>: las otras sedes, destino de las publicaciones de cambios de catálogo.
    public ResultadoArranque Cargar(RutasArchivosInventario rutas, string[] sedesRemotas)
    {
        ArgumentNullException.ThrowIfNull(rutas, nameof(rutas));
        ArgumentNullException.ThrowIfNull(sedesRemotas, nameof(sedesRemotas));

        return Cargar(rutas, sedesRemotas, new RepositorioInventario(rutas));
    }

    /// Permite a las pruebas inyectar un repositorio con puntos de corte.
    internal ResultadoArranque Cargar(RutasArchivosInventario rutas, string[] sedesRemotas, RepositorioInventario repositorio)
    {
        return repositorio.ExisteEstadoGuardado()
            ? Recuperar(rutas, sedesRemotas, repositorio)
            : ArrancarPorPrimeraVez(rutas, sedesRemotas, repositorio);
    }

    private static ResultadoArranque ArrancarPorPrimeraVez(
        RutasArchivosInventario rutas, string[] sedesRemotas, RepositorioInventario repositorio)
    {
        ArbolAvl<string, Producto> maestro = ConstruirMaestro(repositorio.LeerMaestro(), rutas.MaestroProductos);
        EstadoInventario estado = new EstadoInventario();
        CargarDesdeExistencias(repositorio.LeerExistenciasIniciales(), maestro, rutas, estado.Stock);

        InventarioLocal inventario = new InventarioLocal(rutas.SedeId, estado, repositorio, sedesRemotas);
        repositorio.GuardarEstado(estado.ADto(rutas.SedeId));
        return new ResultadoArranque(inventario, ModoArranque.Inicial);
    }

    private static ResultadoArranque Recuperar(
        RutasArchivosInventario rutas, string[] sedesRemotas, RepositorioInventario repositorio)
    {
        string archivo = rutas.EstadoInventario;
        EstadoInventarioDto estado = repositorio.LeerEstado();

        if (estado.Version != EstadoInventarioDto.VersionActual)
        {
            throw new DatosInventarioInvalidosException(archivo,
                $"versión de estado no soportada: {estado.Version?.ToString() ?? "(falta)"}.");
        }
        ValidarSede(estado.SedeId, rutas.SedeId, archivo);
        if (estado.Stock == null)
        {
            throw new DatosInventarioInvalidosException(archivo, "falta el campo 'stock'.");
        }

        EstadoInventario recuperado = new EstadoInventario();
        for (int i = 0; i < estado.Stock.Length; i++)
        {
            string posicion = $"stock[{i}]";
            StockGuardadoDto? entrada = estado.Stock[i];
            if (entrada == null)
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion} es nulo.");
            }

            Producto producto = ConvertirProducto(entrada.Producto, archivo, posicion + ".producto");
            int existencia = CamposObligatorios.Valor(entrada.Existencia, "existencia", archivo, posicion);
            int reservada = CamposObligatorios.Valor(entrada.Reservada, "reservada", archivo, posicion);
            long revision = entrada.Revision ?? 0; // Opcional: un snapshot anterior a RF02 no la trae.
            if (revision < 0)
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion}: la revisión no puede ser negativa.");
            }

            InsertarSinDuplicar(recuperado.Stock, producto.ProductoId,
                new EntradaStock(CrearStock(producto, existencia, reservada, archivo, posicion), revision), archivo, posicion);
        }
        ReconstructorEstado.CargarSecciones(estado, recuperado, rutas.SedeId, archivo);

        return new ResultadoArranque(
            new InventarioLocal(rutas.SedeId, recuperado, repositorio, sedesRemotas), ModoArranque.Recuperacion);
    }

    private static ArbolAvl<string, Producto> ConstruirMaestro(MaestroProductosDto maestro, string archivo)
    {
        if (maestro.Productos == null)
        {
            throw new DatosInventarioInvalidosException(archivo, "falta el campo 'productos'.");
        }

        ArbolAvl<string, Producto> indice = new ArbolAvl<string, Producto>(StringComparer.Ordinal);
        for (int i = 0; i < maestro.Productos.Length; i++)
        {
            string posicion = $"productos[{i}]";
            Producto producto = ConvertirProducto(maestro.Productos[i], archivo, posicion);
            if (!indice.Insertar(producto.ProductoId, producto))
            {
                throw new DatosInventarioInvalidosException(archivo,
                    $"{posicion}: código de producto duplicado '{producto.ProductoId}'.");
            }
        }
        return indice;
    }

    private static void CargarDesdeExistencias(
        ExistenciasInicialesDto semilla, ArbolAvl<string, Producto> maestro, RutasArchivosInventario rutas,
        ArbolAvl<string, EntradaStock> indice)
    {
        string archivo = rutas.ExistenciasIniciales;
        ValidarSede(semilla.SedeId, rutas.SedeId, archivo);
        if (semilla.Existencias == null)
        {
            throw new DatosInventarioInvalidosException(archivo, "falta el campo 'existencias'.");
        }

        for (int i = 0; i < semilla.Existencias.Length; i++)
        {
            string posicion = $"existencias[{i}]";
            ExistenciaInicialDto? entrada = semilla.Existencias[i];
            if (entrada == null)
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion} es nulo.");
            }
            if (string.IsNullOrWhiteSpace(entrada.ProductoId))
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion}: falta 'productoId'.");
            }
            int existencia = CamposObligatorios.Valor(entrada.Existencia, "existencia", archivo, posicion);

            if (!maestro.Buscar(entrada.ProductoId, out Producto? producto))
            {
                throw new DatosInventarioInvalidosException(archivo,
                    $"{posicion}: el producto '{entrada.ProductoId}' no está en el maestro.");
            }

            InsertarSinDuplicar(indice, entrada.ProductoId,
                new EntradaStock(CrearStock(producto, existencia, 0, archivo, posicion), 0), archivo, posicion);
        }
    }

    private static void ValidarSede(string? sedeEnArchivo, string sedeEsperada, string archivo)
    {
        if (!string.Equals(sedeEnArchivo, sedeEsperada, StringComparison.Ordinal))
        {
            throw new DatosInventarioInvalidosException(archivo,
                $"el archivo es de la sede '{sedeEnArchivo ?? "(falta)"}' y esta instancia es '{sedeEsperada}'.");
        }
    }

    private static void InsertarSinDuplicar(
        ArbolAvl<string, EntradaStock> indice, string productoId, EntradaStock entrada, string archivo, string posicion)
    {
        if (!indice.Insertar(productoId, entrada))
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: producto duplicado '{productoId}'.");
        }
    }

    private static Producto ConvertirProducto(ProductoDto? dto, string archivo, string posicion) =>
        ConversionProducto.Convertir(dto, archivo, posicion);

    /// El constructor de StockLocal impone 0 ≤ reservada ≤ existencia.
    private static StockLocal CrearStock(Producto producto, int existencia, int reservada, string archivo, string posicion)
    {
        try
        {
            return new StockLocal(producto, existencia, reservada);
        }
        catch (ArgumentException e)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: {e.Message}", e);
        }
    }
}
