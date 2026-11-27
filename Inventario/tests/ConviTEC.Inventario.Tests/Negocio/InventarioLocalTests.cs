using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;
using ConviTEC.Inventario.Negocio;

namespace ConviTEC.Inventario.Tests.Negocio;

public class InventarioLocalTests
{
    private static StockLocal Stock(string id, int existencia, int reservada) =>
        new StockLocal(new Producto(id, "Nombre " + id, "Descripción", "Categoría", 1000), existencia, reservada);

    private static InventarioLocal Inventario(params StockLocal[] stocks)
    {
        ArbolAvl<string, StockLocal> indice = InventarioLocal.NuevoIndice();
        foreach (StockLocal stock in stocks)
        {
            indice.Insertar(stock.Producto.ProductoId, stock);
        }
        return new InventarioLocal("LIB", indice);
    }

    [Fact]
    public void ConsultarInventario_DevuelveOrdenAscendenteIncluyendoAgotados()
    {
        InventarioLocal inventario = Inventario(Stock("SAL", 0, 0), Stock("CAF", 5, 2), Stock("ARR", 3, 0));

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
        InventarioLocal inventario = Inventario();

        Assert.Empty(inventario.ConsultarInventario());
        Assert.Equal(0, inventario.Cantidad);
    }

    [Fact]
    public void BuscarStock_ProductoPresente_LoDevuelve()
    {
        InventarioLocal inventario = Inventario(Stock("CAF", 5, 2));

        Assert.True(inventario.BuscarStock("CAF", out StockLocal? stock));
        Assert.Equal(3, stock.Disponible);
    }

    [Fact]
    public void BuscarStock_ProductoAusenteOConOtraCapitalizacion_DevuelveFalse()
    {
        InventarioLocal inventario = Inventario(Stock("CAF", 5, 2));

        Assert.False(inventario.BuscarStock("XYZ", out _));
        Assert.False(inventario.BuscarStock("caf", out _));
    }

    [Fact]
    public void BuscarStock_IdNulo_Lanza()
    {
        InventarioLocal inventario = Inventario();

        Assert.Throws<ArgumentNullException>(() => inventario.BuscarStock(null!, out _));
        Assert.Throws<ArgumentNullException>(() => inventario.Contiene(null!));
    }

    [Fact]
    public void Constructor_IndiceNuloOSedeVacia_Lanza()
    {
        Assert.Throws<ArgumentNullException>(() => new InventarioLocal("LIB", null!));
        Assert.Throws<ArgumentException>(() => new InventarioLocal(" ", InventarioLocal.NuevoIndice()));
    }
}
