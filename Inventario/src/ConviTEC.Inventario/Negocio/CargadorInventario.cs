using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Negocio;

/// Si existe estado guardado, recupera sus cantidades y no toca las semillas. 
/// Si no, lee el maestro y las existencias iniciales y guarda el primer snapshot.
public sealed class CargadorInventario
{
    public ResultadoArranque Cargar(RutasArchivosInventario rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas, nameof(rutas));

        RepositorioInventario repositorio = new RepositorioInventario(rutas);
        return repositorio.ExisteEstadoGuardado()
            ? Recuperar(rutas, repositorio)
            : ArrancarPorPrimeraVez(rutas, repositorio);
    }

    private static ResultadoArranque ArrancarPorPrimeraVez(RutasArchivosInventario rutas, RepositorioInventario repositorio)
    {
        ArbolAvl<string, Producto> maestro = ConstruirMaestro(repositorio.LeerMaestro(), rutas.MaestroProductos);
        ArbolAvl<string, StockLocal> indice = ConstruirDesdeExistencias(
            repositorio.LeerExistenciasIniciales(), maestro, rutas);

        InventarioLocal inventario = new InventarioLocal(rutas.SedeId, indice);
        repositorio.GuardarEstado(rutas.SedeId, inventario.ConsultarInventario());
        return new ResultadoArranque(inventario, ModoArranque.Inicial);
    }

    private static ResultadoArranque Recuperar(RutasArchivosInventario rutas, RepositorioInventario repositorio)
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

        ArbolAvl<string, StockLocal> indice = InventarioLocal.NuevoIndice();
        for (int i = 0; i < estado.Stock.Length; i++)
        {
            string posicion = $"stock[{i}]";
            StockGuardadoDto? entrada = estado.Stock[i];
            if (entrada == null)
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion} es nulo.");
            }

            Producto producto = ConvertirProducto(entrada.Producto, archivo, posicion + ".producto");
            int existencia = Obligatorio(entrada.Existencia, "existencia", archivo, posicion);
            int reservada = Obligatorio(entrada.Reservada, "reservada", archivo, posicion);

            InsertarSinDuplicar(indice, producto.ProductoId,
                CrearStock(producto, existencia, reservada, archivo, posicion), archivo, posicion);
        }

        return new ResultadoArranque(new InventarioLocal(rutas.SedeId, indice), ModoArranque.Recuperacion);
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

    private static ArbolAvl<string, StockLocal> ConstruirDesdeExistencias(
        ExistenciasInicialesDto semilla, ArbolAvl<string, Producto> maestro, RutasArchivosInventario rutas)
    {
        string archivo = rutas.ExistenciasIniciales;
        ValidarSede(semilla.SedeId, rutas.SedeId, archivo);
        if (semilla.Existencias == null)
        {
            throw new DatosInventarioInvalidosException(archivo, "falta el campo 'existencias'.");
        }

        ArbolAvl<string, StockLocal> indice = InventarioLocal.NuevoIndice();
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
            int existencia = Obligatorio(entrada.Existencia, "existencia", archivo, posicion);

            if (!maestro.Buscar(entrada.ProductoId, out Producto? producto))
            {
                throw new DatosInventarioInvalidosException(archivo,
                    $"{posicion}: el producto '{entrada.ProductoId}' no está en el maestro.");
            }

            InsertarSinDuplicar(indice, entrada.ProductoId,
                CrearStock(producto, existencia, 0, archivo, posicion), archivo, posicion);
        }
        return indice;
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
        ArbolAvl<string, StockLocal> indice, string productoId, StockLocal stock, string archivo, string posicion)
    {
        if (!indice.Insertar(productoId, stock))
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: producto duplicado '{productoId}'.");
        }
    }

    private static int Obligatorio(int? valor, string campo, string archivo, string posicion)
    {
        if (valor == null)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: falta '{campo}'.");
        }
        return valor.Value;
    }

    private static Producto ConvertirProducto(ProductoDto? dto, string archivo, string posicion)
    {
        if (dto == null)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion} es nulo.");
        }
        if (dto.PrecioUnitarioCentimos == null)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: falta 'precioUnitarioCentimos'.");
        }

        try
        {
            return new Producto(
                dto.ProductoId!, dto.Nombre!, dto.Descripcion!, dto.Categoria!, dto.PrecioUnitarioCentimos.Value);
        }
        catch (ArgumentException e)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: {e.Message}", e);
        }
    }

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
