using System;
using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Tests.Dominio;

public class StockLocalTests
{
    private static Producto Cafe() => new Producto("CAF", "Café", "Café molido 500 g", "Bebidas", 150000);

    [Fact]
    public void Disponible_EsExistenciaMenosReservada()
    {
        StockLocal stock = new StockLocal(Cafe(), existencia: 5, reservada: 2);

        Assert.Equal(3, stock.Disponible);
        Assert.Equal(150000L, stock.Producto.PrecioUnitarioCentimos);
    }

    [Fact]
    public void ProductoAgotado_TieneDisponibleCero()
    {
        StockLocal stock = new StockLocal(Cafe(), existencia: 0, reservada: 0);

        Assert.Equal(0, stock.Disponible);
    }

    [Fact]
    public void TodoReservado_TieneDisponibleCero()
    {
        StockLocal stock = new StockLocal(Cafe(), existencia: 4, reservada: 4);

        Assert.Equal(0, stock.Disponible);
    }

    [Fact]
    public void ReservadaMayorQueExistencia_Lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StockLocal(Cafe(), existencia: 1, reservada: 2));
    }

    [Fact]
    public void ReservadaNegativa_Lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StockLocal(Cafe(), existencia: 5, reservada: -1));
    }

    [Fact]
    public void ExistenciaNegativa_Lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StockLocal(Cafe(), existencia: -1, reservada: 0));
    }

    [Fact]
    public void ProductoNulo_Lanza()
    {
        Assert.Throws<ArgumentNullException>(() => new StockLocal(null!, existencia: 1, reservada: 0));
    }
}
