using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Negocio;

public class InventarioLocalTests
{
    private static StockLocal Stock(string id, int existencia, int reservada) => FabricaInventario.Stock(id, existencia, reservada);

    [Fact]
    public void ConsultarInventario_DevuelveOrdenAscendenteIncluyendoAgotados()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.Crear(dir, "LIB", Stock("SAL", 0, 0), Stock("CAF", 5, 2), Stock("ARR", 3, 0));

        StockLocal[] resultado = inventario.ConsultarInventario();

        Assert.Equal(new[] { "ARR", "CAF", "SAL" }, new[]
        {
            resultado[0].Producto.ProductoId, resultado[1].Producto.ProductoId, resultado[2].Producto.ProductoId,
        });
        Assert.Equal(3, resultado[1].Disponible);
        Assert.Equal(0, resultado[2].Disponible);
    }

    [Fact]
    public void ConsultarInventario_Vacio_DevuelveArregloVacio()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.Crear(dir, "LIB");

        Assert.Empty(inventario.ConsultarInventario());
        Assert.Equal(0, inventario.Cantidad);
    }

    [Fact]
    public void BuscarStock_ProductoPresente_LoDevuelve()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.Crear(dir, "LIB", Stock("CAF", 5, 2));

        Assert.True(inventario.BuscarStock("CAF", out StockLocal? stock));
        Assert.Equal(3, stock.Disponible);
    }

    [Fact]
    public void BuscarStock_ProductoAusenteOConOtraCapitalizacion_DevuelveFalse()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.Crear(dir, "LIB", Stock("CAF", 5, 2));

        Assert.False(inventario.BuscarStock("XYZ", out _));
        Assert.False(inventario.BuscarStock("caf", out _));
    }

    [Fact]
    public void BuscarStock_IdNulo_Lanza()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.Crear(dir, "LIB");

        Assert.Throws<ArgumentNullException>(() => inventario.BuscarStock(null!, out _));
        Assert.Throws<ArgumentNullException>(() => inventario.Contiene(null!));
        Assert.Throws<ArgumentNullException>(() => inventario.BuscarRevision(null!, out _));
    }

    [Fact]
    public void BuscarRevision_ProductoSinCambios_EsCeroYAusenteDevuelveFalse()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.Crear(dir, "LIB", Stock("CAF", 5, 2));

        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(0, revision);
        Assert.False(inventario.BuscarRevision("XYZ", out _));
    }

    [Fact]
    public void Constructor_ArgumentosInvalidos_Lanza()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"));
        string[] sinRemotas = Array.Empty<string>();

        Assert.Throws<ArgumentNullException>(() => new InventarioLocal("LIB", null!, repositorio, sinRemotas));
        Assert.Throws<ArgumentNullException>(() => new InventarioLocal("LIB", new EstadoInventario(), null!, sinRemotas));
        Assert.Throws<ArgumentNullException>(() => new InventarioLocal("LIB", new EstadoInventario(), repositorio, null!));
        Assert.Throws<ArgumentException>(() => new InventarioLocal(" ", new EstadoInventario(), repositorio, sinRemotas));
    }

    [Fact]
    public void Constructor_SedesRemotasInvalidas_Lanza()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"));

        Assert.Throws<ArgumentException>(() => new InventarioLocal("LIB", new EstadoInventario(), repositorio, new[] { "LIB" }));
        Assert.Throws<ArgumentException>(() => new InventarioLocal("LIB", new EstadoInventario(), repositorio, new[] { "SJ", "SJ" }));
        Assert.Throws<ArgumentException>(() => new InventarioLocal("LIB", new EstadoInventario(), repositorio, new[] { "SJ", " " }));
    }
}
