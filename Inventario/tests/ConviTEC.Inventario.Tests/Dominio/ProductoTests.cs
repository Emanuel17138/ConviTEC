using System;
using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Tests.Dominio;

public class ProductoTests
{
    [Fact]
    public void Constructor_DatosValidos_ConservaTodosLosCampos()
    {
        Producto producto = new Producto("CAF", "Café", "Café molido 500 g", "Bebidas", 150000);

        Assert.Equal("CAF", producto.ProductoId);
        Assert.Equal("Café", producto.Nombre);
        Assert.Equal("Café molido 500 g", producto.Descripcion);
        Assert.Equal("Bebidas", producto.Categoria);
        Assert.Equal(150000L, producto.PrecioUnitarioCentimos);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_IdVacioONulo_Lanza(string? productoId)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Producto(productoId!, "Café", "d", "c", 100));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_NombreVacioONulo_Lanza(string? nombre)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Producto("CAF", nombre!, "d", "c", 100));
    }

    [Fact]
    public void Constructor_DescripcionOCategoriaNulas_Lanza()
    {
        Assert.Throws<ArgumentNullException>(() => new Producto("CAF", "Café", null!, "c", 100));
        Assert.Throws<ArgumentNullException>(() => new Producto("CAF", "Café", "d", null!, 100));
    }

    [Fact]
    public void Constructor_PrecioNegativo_Lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Producto("CAF", "Café", "d", "c", -1));
    }

    [Fact]
    public void Constructor_PrecioCero_EsValido()
    {
        Producto producto = new Producto("OBS", "Obsequio", "", "", 0);

        Assert.Equal(0L, producto.PrecioUnitarioCentimos);
    }
}
