using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using ConviTEC.Inventario.Api.Http;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Tests.Negocio;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Api;

/// GET /api/v1/catalogo contra el backend real (RF03 · rf03-consulta). Las instantáneas se aplican directo a la
/// réplica de la sede, como lo hará el consumidor de rf03-sincronizacion.
public class CatalogoHttpTests
{
    private const string Ruta = "/api/v1/catalogo";

    private static Task<SedeHttp> ArrancarSj(string existenciasJson, params string[] remotas) =>
        SedeHttp.ArrancarInicialAsync("SJ", existenciasJson, remotas);

    private static EntradaCatalogo Entrada(string productoId, int disponible, long revision) =>
        FabricaCatalogo.Entrada(productoId, disponible, revision);

    private static int Propiedades(JsonElement objeto)
    {
        int cantidad = 0;
        foreach (JsonProperty propiedad in objeto.EnumerateObject())
        {
            cantidad++;
        }
        return cantidad;
    }

    private static async Task<JsonElement> LeerAsync(HttpResponseMessage respuesta)
    {
        Assert.StartsWith("application/json", respuesta.Content.Headers.ContentType?.MediaType ?? "");
        return JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// Serializa el resultado del puerto con las mismas reglas del contrato, para compararlo con lo que sirve HTTP.
    private static string ComoJson(ProductoGlobal[] vista) =>
        JsonSerializer.Serialize(ProductoGlobalRespuesta.Desde(vista), new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    [Fact]
    public async Task MientrasFaltaUnaInstantaneaResponde503ConElCuerpoComun()
    {
        await using SedeHttp sede = await ArrancarSj("""[{"productoId":"CAF","existencia":5}]""", "LIB", "ALA");
        sede.Catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) });

        HttpResponseMessage respuesta = await sede.Cliente.GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        JsonElement cuerpo = await LeerAsync(respuesta);
        Assert.Equal(2, Propiedades(cuerpo));
        Assert.Equal("CATALOGO_INICIALIZANDO", cuerpo.GetProperty("codigo").GetString());
        Assert.Contains("ALA", cuerpo.GetProperty("mensaje").GetString());
    }

    [Fact]
    public async Task PasaDe503A200SoloAlLlegarLaInstantaneaDeLaUltimaSedeAunqueYaHayaRecibidoUnCambioDeElla()
    {
        await using SedeHttp sede = await ArrancarSj("[]", "LIB", "ALA");
        sede.Catalogo.AplicarInstantanea("LIB", Array.Empty<EntradaCatalogo>());
        sede.Catalogo.AplicarCambio("ALA", Entrada("CAF", 1, 1));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await sede.Cliente.GetAsync(Ruta)).StatusCode);

        sede.Catalogo.AplicarInstantanea("ALA", Array.Empty<EntradaCatalogo>());

        HttpResponseMessage respuesta = await sede.Cliente.GetAsync(Ruta);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        JsonElement cuerpo = await LeerAsync(respuesta);
        Assert.Equal(JsonValueKind.Array, cuerpo.ValueKind);
        // El cambio de ALA llegó antes de su instantánea y se conservó: la vista ya lo muestra.
        Assert.Equal(1, cuerpo.GetArrayLength());
        Assert.Equal("ALA", cuerpo[0].GetProperty("ofertas")[0].GetProperty("sedeId").GetString());
    }

    [Fact]
    public async Task SinProductosNiOfertasResponde200YArregloVacio()
    {
        await using SedeHttp sede = await ArrancarSj("[]");

        HttpResponseMessage respuesta = await sede.Cliente.GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("[]", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ElContratoTieneLosCamposExactosEnCamelCaseYElOrdenEsProductoYSede()
    {
        await using SedeHttp sede = await ArrancarSj("""[{"productoId":"CAF","existencia":5}]""", "LIB", "ALA");
        sede.Catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 0, 3), Entrada("ARR", 7, 2) });
        sede.Catalogo.AplicarInstantanea("ALA", Array.Empty<EntradaCatalogo>());

        JsonElement cuerpo = await LeerAsync(await sede.Cliente.GetAsync(Ruta));

        Assert.Equal(2, cuerpo.GetArrayLength());
        JsonElement arr = cuerpo[0];
        Assert.Equal(2, Propiedades(arr));
        Assert.Equal("ARR", arr.GetProperty("producto").GetProperty("productoId").GetString());
        Assert.Equal(1, arr.GetProperty("ofertas").GetArrayLength());

        JsonElement caf = cuerpo[1];
        JsonElement producto = caf.GetProperty("producto");
        Assert.Equal(5, Propiedades(producto));
        Assert.Equal("CAF", producto.GetProperty("productoId").GetString());
        Assert.Equal("Café", producto.GetProperty("nombre").GetString());
        Assert.Equal(150000, producto.GetProperty("precioUnitarioCentimos").GetInt64());

        JsonElement ofertas = caf.GetProperty("ofertas");
        Assert.Equal(2, ofertas.GetArrayLength());
        Assert.Equal(3, Propiedades(ofertas[0]));
        Assert.Equal("LIB", ofertas[0].GetProperty("sedeId").GetString());
        Assert.Equal(0, ofertas[0].GetProperty("disponible").GetInt32());
        Assert.Equal(3, ofertas[0].GetProperty("revision").GetInt64());
        Assert.Equal("SJ", ofertas[1].GetProperty("sedeId").GetString());
        Assert.Equal(5, ofertas[1].GetProperty("disponible").GetInt32());
        Assert.DoesNotContain("ALA", await sede.Cliente.GetStringAsync(Ruta));
    }

    [Fact]
    public async Task HttpYElPuertoLocalDevuelvenElMismoContenido()
    {
        await using SedeHttp sede = await ArrancarSj(
            """[{"productoId":"CAF","existencia":5},{"productoId":"SAL","existencia":0}]""", "LIB", "ALA");
        sede.Catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1), Entrada("ARR", 1, 1) });
        sede.Catalogo.AplicarInstantanea("ALA", new[] { Entrada("SAL", 6, 2) });

        string http = await sede.Cliente.GetStringAsync(Ruta);
        string puerto = ComoJson(sede.Consulta.ConsultarCatalogo());

        Assert.Equal(puerto, http);
    }

    [Fact]
    public async Task UnaReposicionPorHttpSeVeEnLaOfertaPropiaConRevisionSuperior()
    {
        await using SedeHttp sede = await ArrancarSj("""[{"productoId":"CAF","existencia":5}]""");
        string operacion = "40000000-0000-4000-8000-000000000001";
        HttpResponseMessage reposicion = await sede.Cliente.PostAsync(
            "/api/v1/inventario/CAF/reposiciones",
            new StringContent("{\"operacionId\":\"" + operacion + "\",\"cantidad\":3}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, reposicion.StatusCode);

        JsonElement oferta = (await LeerAsync(await sede.Cliente.GetAsync(Ruta)))[0].GetProperty("ofertas")[0];

        Assert.Equal("SJ", oferta.GetProperty("sedeId").GetString());
        Assert.Equal(8, oferta.GetProperty("disponible").GetInt32());
        Assert.Equal(1, oferta.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task UnaCopiaInicializadaSigueConsultablePorHttpTrasReiniciar()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir("maestro-productos.json", SedeHttp.Maestro);
        dir.Escribir("existencias-iniciales.json", """{"sedeId":"SJ","existencias":[{"productoId":"CAF","existencia":5}]}""");
        await using (SedeHttp antes = await SedeHttp.ArrancarAsync(dir, "SJ", false, "LIB"))
        {
            antes.Catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) });
            Assert.Equal(HttpStatusCode.OK, (await antes.Cliente.GetAsync(Ruta)).StatusCode);
        }

        await using SedeHttp despues = await SedeHttp.ArrancarAsync(dir, "SJ", false, "LIB");

        JsonElement cuerpo = await LeerAsync(await despues.Cliente.GetAsync(Ruta));
        Assert.Equal(2, cuerpo[0].GetProperty("ofertas").GetArrayLength());
        Assert.Equal(4, cuerpo[0].GetProperty("ofertas")[0].GetProperty("disponible").GetInt32());
    }

    [Fact]
    public async Task OtrosMetodosSobreElCatalogoDan405()
    {
        await using SedeHttp sede = await ArrancarSj("[]");

        HttpResponseMessage respuesta = await sede.Cliente.PostAsync(Ruta, new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, respuesta.StatusCode);
    }
}
