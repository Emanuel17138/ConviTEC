using System;
using System.Text.Json;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Mensajeria;
using ConviTEC.Inventario.Tests.Negocio;

namespace ConviTEC.Inventario.Tests.Mensajeria;

/// Cuerpos de <c>catalogo.actualizado</c> y <c>catalogo.instantanea</c>: escritura, lectura y entradas inválidas.
public class CuerpoCatalogoTests
{
    private static JsonElement Json(string texto)
    {
        using JsonDocument documento = JsonDocument.Parse(texto);
        return documento.RootElement.Clone();
    }

    private const string ProductoCaf =
        "{\"productoId\":\"CAF\",\"nombre\":\"Café\",\"descripcion\":\"d\",\"categoria\":\"c\",\"precioUnitarioCentimos\":150000}";

    [Fact]
    public void Cambio_IdaYVuelta_ConservaProductoDisponibleYRevision()
    {
        EntradaCatalogo original = FabricaCatalogo.Entrada("CAF", 7, 12, precio: 99000);

        string texto = CuerpoCatalogo.EscribirCambio(original);
        bool ok = CuerpoCatalogo.TryLeerCambio(Json(texto), out EntradaCatalogo? leida, out string motivo);

        Assert.True(ok, motivo);
        Assert.Equal("CAF", leida!.Producto.ProductoId);
        Assert.Equal("Nombre CAF", leida.Producto.Nombre);
        Assert.Equal(99000L, leida.Producto.PrecioUnitarioCentimos);
        Assert.Equal(7, leida.Disponible);
        Assert.Equal(12L, leida.Revision);
    }

    [Fact]
    public void Cambio_EscribeLasClavesEnCamelCase()
    {
        JsonElement cuerpo = Json(CuerpoCatalogo.EscribirCambio(FabricaCatalogo.Entrada("CAF", 0, 3)));

        Assert.Equal("CAF", cuerpo.GetProperty("producto").GetProperty("productoId").GetString());
        Assert.Equal(0, cuerpo.GetProperty("disponible").GetInt32());
        Assert.Equal(3L, cuerpo.GetProperty("revision").GetInt64());
    }

    [Fact]
    public void Instantanea_IdaYVuelta_ConservaCerosYOrden()
    {
        EntradaCatalogo[] original =
        {
            FabricaCatalogo.Entrada("ARR", 0, 4),
            FabricaCatalogo.Entrada("CAF", 9, 10),
        };

        string texto = CuerpoCatalogo.EscribirInstantanea(original);
        bool ok = CuerpoCatalogo.TryLeerInstantanea(Json(texto), out EntradaCatalogo[]? leidas, out string motivo);

        Assert.True(ok, motivo);
        Assert.Equal(2, leidas!.Length);
        Assert.Equal("ARR", leidas[0].Producto.ProductoId);
        Assert.Equal(0, leidas[0].Disponible);
        Assert.Equal("CAF", leidas[1].Producto.ProductoId);
        Assert.Equal(10L, leidas[1].Revision);
    }

    [Fact]
    public void Instantanea_Vacia_EsValida()
    {
        string texto = CuerpoCatalogo.EscribirInstantanea(Array.Empty<EntradaCatalogo>());

        Assert.Equal("{\"entradas\":[]}", texto);
        Assert.True(CuerpoCatalogo.TryLeerInstantanea(Json(texto), out EntradaCatalogo[]? leidas, out _));
        Assert.Empty(leidas!);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"disponible\":1,\"revision\":1}")]                                       // sin producto
    [InlineData("{\"producto\":null,\"disponible\":1,\"revision\":1}")]
    [InlineData("{\"producto\":{\"productoId\":\"CAF\"},\"disponible\":1,\"revision\":1}")] // producto incompleto
    public void Cambio_MalFormado_DevuelveFalseConMotivo(string cuerpo)
    {
        Assert.False(CuerpoCatalogo.TryLeerCambio(Json(cuerpo), out EntradaCatalogo? entrada, out string motivo));
        Assert.Null(entrada);
        Assert.NotEmpty(motivo);
    }

    [Theory]
    [InlineData("-1", "1")]     // disponible negativo
    [InlineData("1", "-1")]     // revisión negativa
    [InlineData("1.5", "1")]    // disponible no entero
    [InlineData("\"2\"", "1")]  // disponible como texto
    [InlineData("1", "\"1\"")]  // revisión como texto
    [InlineData("1", "null")]
    [InlineData("3000000000", "1")] // no cabe en int
    public void Cambio_CantidadesInvalidas_DevuelveFalse(string disponible, string revision)
    {
        string cuerpo = $"{{\"producto\":{ProductoCaf},\"disponible\":{disponible},\"revision\":{revision}}}";

        Assert.False(CuerpoCatalogo.TryLeerCambio(Json(cuerpo), out _, out string motivo));
        Assert.NotEmpty(motivo);
    }

    [Fact]
    public void Cambio_ProductoConPrecioNegativoONombreVacio_DevuelveFalse()
    {
        string precioNegativo = ProductoCaf.Replace("150000", "-5");
        string nombreVacio = ProductoCaf.Replace("\"Café\"", "\"\"");

        Assert.False(CuerpoCatalogo.TryLeerCambio(
            Json($"{{\"producto\":{precioNegativo},\"disponible\":1,\"revision\":1}}"), out _, out _));
        Assert.False(CuerpoCatalogo.TryLeerCambio(
            Json($"{{\"producto\":{nombreVacio},\"disponible\":1,\"revision\":1}}"), out _, out _));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"entradas\":null}")]
    [InlineData("{\"entradas\":{}}")]
    [InlineData("{\"entradas\":[null]}")]
    [InlineData("{\"entradas\":[{\"disponible\":1}]}")]
    public void Instantanea_MalFormada_DevuelveFalse(string cuerpo)
    {
        Assert.False(CuerpoCatalogo.TryLeerInstantanea(Json(cuerpo), out EntradaCatalogo[]? entradas, out string motivo));
        Assert.Null(entradas);
        Assert.NotEmpty(motivo);
    }

    [Fact]
    public void Instantanea_UnaEntradaInvalida_InvalidaTodaLaInstantanea()
    {
        string buena = $"{{\"producto\":{ProductoCaf},\"disponible\":1,\"revision\":1}}";
        string mala = $"{{\"producto\":{ProductoCaf},\"disponible\":-1,\"revision\":1}}";

        Assert.False(CuerpoCatalogo.TryLeerInstantanea(Json($"{{\"entradas\":[{buena},{mala}]}}"), out _, out string motivo));
        Assert.Contains("entradas[1]", motivo);
    }

    [Fact]
    public void Escribir_ConEntradaNula_Lanza()
    {
        Assert.Throws<ArgumentNullException>(() => CuerpoCatalogo.EscribirCambio(null!));
        Assert.Throws<ArgumentNullException>(() => CuerpoCatalogo.EscribirInstantanea(null!));
    }

    [Fact]
    public void IdentificadorDerivadoDeSolicitud_EsEstableUuidYDistintoDelDeComando()
    {
        string a = IdentificadorDerivado.DeSolicitudCatalogo("solicitud-1");

        Assert.Equal(a, IdentificadorDerivado.DeSolicitudCatalogo("solicitud-1"));
        Assert.NotEqual(a, IdentificadorDerivado.DeSolicitudCatalogo("solicitud-2"));
        Assert.NotEqual(a, IdentificadorDerivado.DeComando("solicitud-1"));
        Assert.True(Guid.TryParse(a, out _));
        Assert.Throws<ArgumentException>(() => IdentificadorDerivado.DeSolicitudCatalogo(" "));
    }
}
