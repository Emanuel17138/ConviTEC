using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Api;

/// Pruebas de GET /api/v1/inventario contra el backend real, con los datos que espera Web (04-web-ventas.md).
public class InventarioHttpTests
{
    private const string Ruta = "/api/v1/inventario";

    private static string EntradaGuardada(string id, string nombre, long precio, int existencia, int reservada) =>
        "{\"producto\":{\"productoId\":\"" + id + "\",\"nombre\":\"" + nombre + "\",\"descripcion\":\"d\",\"categoria\":\"c\"," +
        "\"precioUnitarioCentimos\":" + precio + "},\"existencia\":" + existencia + ",\"reservada\":" + reservada + "}";

    private static string EstadoGuardado(string sedeId, string stock) =>
        "{\"version\":1,\"sedeId\":\"" + sedeId + "\",\"stock\":" + stock + "}";

    private static async Task<JsonElement> ObtenerJsonAsync(HttpClient cliente, string ruta)
    {
        HttpResponseMessage respuesta = await cliente.GetAsync(ruta);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.StartsWith("application/json", respuesta.Content.Headers.ContentType?.MediaType ?? "");
        return JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Consultar_DevuelveContratoExactoConOrdenYAgotados()
    {
        DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, EstadoGuardado("LIB",
            "[" + EntradaGuardada("SAL", "Sal", 50000, 0, 0) + "," + EntradaGuardada("CAF", "Café", 150000, 5, 2) + "]"));
        await using SedeHttp sede = await SedeHttp.ArrancarAsync(dir, "LIB");

        JsonElement cuerpo = await ObtenerJsonAsync(sede.Cliente, Ruta);

        Assert.Equal(JsonValueKind.Array, cuerpo.ValueKind);
        Assert.Equal(2, cuerpo.GetArrayLength());

        JsonElement cafe = cuerpo[0];
        Assert.Equal("CAF", cafe.GetProperty("producto").GetProperty("productoId").GetString());
        Assert.Equal("Café", cafe.GetProperty("producto").GetProperty("nombre").GetString());
        Assert.Equal("d", cafe.GetProperty("producto").GetProperty("descripcion").GetString());
        Assert.Equal("c", cafe.GetProperty("producto").GetProperty("categoria").GetString());
        Assert.Equal(JsonValueKind.Number, cafe.GetProperty("producto").GetProperty("precioUnitarioCentimos").ValueKind);
        Assert.Equal(150000L, cafe.GetProperty("producto").GetProperty("precioUnitarioCentimos").GetInt64());
        Assert.Equal(5, cafe.GetProperty("existencia").GetInt32());
        Assert.Equal(2, cafe.GetProperty("reservada").GetInt32());
        Assert.Equal(3, cafe.GetProperty("disponible").GetInt32());

        JsonElement sal = cuerpo[1];
        Assert.Equal("SAL", sal.GetProperty("producto").GetProperty("productoId").GetString());
        Assert.Equal(0, sal.GetProperty("existencia").GetInt32());
        Assert.Equal(0, sal.GetProperty("disponible").GetInt32());
    }

    [Fact]
    public async Task Consultar_UsaSoloLosCamposDelContrato()
    {
        await using SedeHttp sede = await SedeHttp.ArrancarInicialAsync("LIB", """[{"productoId":"CAF","existencia":5}]""");

        JsonElement entrada = (await ObtenerJsonAsync(sede.Cliente, Ruta))[0];

        Assert.Equal(4, CantidadDePropiedades(entrada));
        Assert.Equal(5, CantidadDePropiedades(entrada.GetProperty("producto")));
    }

    [Fact]
    public async Task Consultar_ArranqueInicial_OrdenaPorProductoIdAscendente()
    {
        await using SedeHttp sede = await SedeHttp.ArrancarInicialAsync("LIB",
            """[{"productoId":"SAL","existencia":1},{"productoId":"CAF","existencia":2},{"productoId":"ARR","existencia":3}]""");

        JsonElement cuerpo = await ObtenerJsonAsync(sede.Cliente, Ruta);

        Assert.Equal(3, cuerpo.GetArrayLength());
        Assert.Equal("ARR", cuerpo[0].GetProperty("producto").GetProperty("productoId").GetString());
        Assert.Equal("CAF", cuerpo[1].GetProperty("producto").GetProperty("productoId").GetString());
        Assert.Equal("SAL", cuerpo[2].GetProperty("producto").GetProperty("productoId").GetString());
    }

    [Fact]
    public async Task Consultar_SedeSinProductos_Devuelve200ConArregloVacio()
    {
        await using SedeHttp sede = await SedeHttp.ArrancarInicialAsync("LIB", "[]");

        HttpResponseMessage respuesta = await sede.Cliente.GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("[]", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Consultar_TrasReiniciar_MuestraEstadoGuardadoYNoLaSemilla()
    {
        DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, SedeHttp.Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales,
            """{"sedeId":"LIB","existencias":[{"productoId":"CAF","existencia":8}]}""");
        dir.Escribir(RutasArchivosInventario.NombreEstado,
            EstadoGuardado("LIB", "[" + EntradaGuardada("CAF", "Café", 150000, 4, 1) + "]"));
        await using SedeHttp sede = await SedeHttp.ArrancarAsync(dir, "LIB");

        JsonElement cuerpo = await ObtenerJsonAsync(sede.Cliente, Ruta);

        Assert.Equal(ModoArranque.Recuperacion, sede.Arranque.Modo);
        Assert.Equal(1, cuerpo.GetArrayLength());
        Assert.Equal(4, cuerpo[0].GetProperty("existencia").GetInt32());
        Assert.Equal(1, cuerpo[0].GetProperty("reservada").GetInt32());
        Assert.Equal(3, cuerpo[0].GetProperty("disponible").GetInt32());
    }

    [Fact]
    public async Task Consultar_DosInstanciasConCantidadesDistintas_CadaUnaDevuelveLasSuyas()
    {
        await using SedeHttp liberia = await SedeHttp.ArrancarInicialAsync("LIB", """[{"productoId":"CAF","existencia":5}]""");
        await using SedeHttp alajuela = await SedeHttp.ArrancarInicialAsync("ALA", """[{"productoId":"CAF","existencia":9}]""");

        JsonElement deLiberia = await ObtenerJsonAsync(liberia.Cliente, Ruta);
        JsonElement deAlajuela = await ObtenerJsonAsync(alajuela.Cliente, Ruta);

        Assert.Equal(5, deLiberia[0].GetProperty("existencia").GetInt32());
        Assert.Equal(9, deAlajuela[0].GetProperty("existencia").GetInt32());
    }

    [Fact]
    public async Task Consultar_ReflejaCambiosPosterioresDelInventario()
    {
        DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado,
            EstadoGuardado("LIB", "[" + EntradaGuardada("CAF", "Café", 150000, 5, 0) + "]"));
        await using SedeHttp sede = await SedeHttp.ArrancarAsync(dir, "LIB");

        // Dos consultas seguidas leen el mismo estado vivo y no una copia congelada al arrancar.
        JsonElement primera = await ObtenerJsonAsync(sede.Cliente, Ruta);
        JsonElement segunda = await ObtenerJsonAsync(sede.Cliente, Ruta);

        Assert.Equal(primera.GetRawText(), segunda.GetRawText());
    }

    [Fact]
    public async Task RutaInexistente_Devuelve404()
    {
        await using SedeHttp sede = await SedeHttp.ArrancarInicialAsync("LIB", "[]");

        HttpResponseMessage respuesta = await sede.Cliente.GetAsync("/api/v1/otra-cosa");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task MetodoNoPermitido_NoModificaElInventario()
    {
        await using SedeHttp sede = await SedeHttp.ArrancarInicialAsync("LIB", """[{"productoId":"CAF","existencia":5}]""");

        HttpResponseMessage respuesta = await sede.Cliente.DeleteAsync(Ruta);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, respuesta.StatusCode);
        Assert.Equal(1, sede.Arranque.Inventario.Cantidad);
    }

    private static int CantidadDePropiedades(JsonElement objeto)
    {
        int cantidad = 0;
        foreach (JsonProperty propiedad in objeto.EnumerateObject())
        {
            cantidad++;
        }
        return cantidad;
    }
}
