using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Api;

/// Pruebas de POST /api/v1/inventario/{productoId}/reposiciones contra el backend real.
public class ReposicionesHttpTests
{
    private const string Op1 = "20000000-0000-4000-8000-000000000001";
    private const string Op2 = "20000000-0000-4000-8000-000000000002";

    private static string RutaDe(string productoId) => "/api/v1/inventario/" + productoId + "/reposiciones";

    private static Task<HttpResponseMessage> Reponer(HttpClient cliente, string productoId, string cuerpoJson) =>
        cliente.PostAsync(RutaDe(productoId), new StringContent(cuerpoJson, Encoding.UTF8, "application/json"));

    private static string Cuerpo(string operacionId, int cantidad) =>
        "{\"operacionId\":\"" + operacionId + "\",\"cantidad\":" + cantidad + "}";

    private static async Task<JsonElement> LeerJsonAsync(HttpResponseMessage respuesta)
    {
        Assert.StartsWith("application/json", respuesta.Content.Headers.ContentType?.MediaType ?? "");
        return JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task AfirmarErrorAsync(HttpResponseMessage respuesta, HttpStatusCode estado, string codigo)
    {
        Assert.Equal(estado, respuesta.StatusCode);
        JsonElement cuerpo = await LeerJsonAsync(respuesta);
        Assert.Equal(codigo, cuerpo.GetProperty("codigo").GetString());
        Assert.False(string.IsNullOrWhiteSpace(cuerpo.GetProperty("mensaje").GetString()));
        Assert.Equal(2, CantidadDePropiedades(cuerpo));
    }

    /// El inventario completo, tal como lo ve la pantalla, para comprobar que un rechazo no tocó nada.
    private static async Task<string> InventarioActualAsync(HttpClient cliente) =>
        await cliente.GetStringAsync("/api/v1/inventario");

    private static int CantidadDePropiedades(JsonElement objeto)
    {
        int cantidad = 0;
        foreach (JsonProperty propiedad in objeto.EnumerateObject())
        {
            cantidad++;
        }
        return cantidad;
    }

    private static void AfirmarStock(JsonElement stock, string productoId, int existencia, int reservada, int disponible)
    {
        Assert.Equal(4, CantidadDePropiedades(stock));
        Assert.Equal(productoId, stock.GetProperty("producto").GetProperty("productoId").GetString());
        Assert.Equal(existencia, stock.GetProperty("existencia").GetInt32());
        Assert.Equal(reservada, stock.GetProperty("reservada").GetInt32());
        Assert.Equal(disponible, stock.GetProperty("disponible").GetInt32());
    }

    private static Task<SedeHttp> ArrancarCafe(int existencia) =>
        SedeHttp.ArrancarInicialAsync("LIB", "[{\"productoId\":\"CAF\",\"existencia\":" + existencia + "}]");

    // ---- Caso de aceptación ----

    [Fact]
    public async Task Reponer_DevuelveElStockLocalResultanteYLaPantallaLoVe()
    {
        await using SedeHttp sede = await ArrancarCafe(5);

        HttpResponseMessage respuesta = await Reponer(sede.Cliente, "CAF", Cuerpo(Op1, 3));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        JsonElement stock = await LeerJsonAsync(respuesta);
        AfirmarStock(stock, "CAF", 8, 0, 8);
        Assert.Equal(150000L, stock.GetProperty("producto").GetProperty("precioUnitarioCentimos").GetInt64());

        JsonElement inventario = JsonDocument.Parse(await InventarioActualAsync(sede.Cliente)).RootElement;
        AfirmarStock(inventario[0], "CAF", 8, 0, 8);
    }

    [Fact]
    public async Task Reponer_RepetirLaPrimeraPeticion_DevuelveElMismoResultadoAunqueLaExistenciaAvanzo()
    {
        await using SedeHttp sede = await ArrancarCafe(5);

        string primera = await (await Reponer(sede.Cliente, "CAF", Cuerpo(Op1, 3))).Content.ReadAsStringAsync();
        HttpResponseMessage segunda = await Reponer(sede.Cliente, "CAF", Cuerpo(Op2, 1));
        HttpResponseMessage repetida = await Reponer(sede.Cliente, "CAF", Cuerpo(Op1, 3));

        AfirmarStock(await LeerJsonAsync(segunda), "CAF", 9, 0, 9);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal(primera, await repetida.Content.ReadAsStringAsync());
        JsonElement inventario = JsonDocument.Parse(await InventarioActualAsync(sede.Cliente)).RootElement;
        AfirmarStock(inventario[0], "CAF", 9, 0, 9);
    }

    [Fact]
    public async Task Reponer_MismaClaveConOtraCantidadOtroProducto_Devuelve409YNoCambiaNada()
    {
        await using SedeHttp sede = await SedeHttp.ArrancarInicialAsync("LIB",
            """[{"productoId":"CAF","existencia":5},{"productoId":"ARR","existencia":4}]""");
        await Reponer(sede.Cliente, "CAF", Cuerpo(Op1, 3));
        string antes = await InventarioActualAsync(sede.Cliente);

        await AfirmarErrorAsync(await Reponer(sede.Cliente, "CAF", Cuerpo(Op1, 4)), HttpStatusCode.Conflict, "CLAVE_REUTILIZADA");
        await AfirmarErrorAsync(await Reponer(sede.Cliente, "ARR", Cuerpo(Op1, 3)), HttpStatusCode.Conflict, "CLAVE_REUTILIZADA");

        Assert.Equal(antes, await InventarioActualAsync(sede.Cliente));
    }

    // ---- Errores ----

    [Theory]
    [InlineData("{\"operacionId\":\"" + Op1 + "\",\"cantidad\":0}")]
    [InlineData("{\"operacionId\":\"" + Op1 + "\",\"cantidad\":-4}")]
    [InlineData("{\"operacionId\":\"" + Op1 + "\",\"cantidad\":2.5}")]
    [InlineData("{\"operacionId\":\"" + Op1 + "\",\"cantidad\":\"3\"}")]
    [InlineData("{\"operacionId\":\"" + Op1 + "\",\"cantidad\":99999999999}")]
    [InlineData("{\"operacionId\":\"" + Op1 + "\",\"cantidad\":null}")]
    [InlineData("{\"operacionId\":\"" + Op1 + "\"}")]
    public async Task Reponer_CantidadInvalida_Devuelve400YNoCambiaNada(string cuerpo)
    {
        await using SedeHttp sede = await ArrancarCafe(5);
        string antes = await InventarioActualAsync(sede.Cliente);

        await AfirmarErrorAsync(await Reponer(sede.Cliente, "CAF", cuerpo), HttpStatusCode.BadRequest, "CANTIDAD_INVALIDA");

        Assert.Equal(antes, await InventarioActualAsync(sede.Cliente));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("{\"operacionId\":")]
    [InlineData("[1,2,3]")]
    [InlineData("\"texto\"")]
    [InlineData("{\"cantidad\":3}")]
    [InlineData("{\"operacionId\":7,\"cantidad\":3}")]
    [InlineData("{\"operacionId\":\"op-1\",\"cantidad\":3}")]
    public async Task Reponer_CuerpoOClaveInvalidos_Devuelve400DatosInvalidosYNoCambiaNada(string cuerpo)
    {
        await using SedeHttp sede = await ArrancarCafe(5);
        string antes = await InventarioActualAsync(sede.Cliente);

        await AfirmarErrorAsync(await Reponer(sede.Cliente, "CAF", cuerpo), HttpStatusCode.BadRequest, "DATOS_INVALIDOS");

        Assert.Equal(antes, await InventarioActualAsync(sede.Cliente));
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("caf")]
    public async Task Reponer_ProductoDesconocido_Devuelve404YNoCambiaNada(string productoId)
    {
        await using SedeHttp sede = await ArrancarCafe(5);
        string antes = await InventarioActualAsync(sede.Cliente);

        await AfirmarErrorAsync(await Reponer(sede.Cliente, productoId, Cuerpo(Op1, 3)), HttpStatusCode.NotFound, "PRODUCTO_DESCONOCIDO");

        Assert.Equal(antes, await InventarioActualAsync(sede.Cliente));
    }

    [Fact]
    public async Task Reponer_ProductoDeOtraSedeNoSeAfectaEntreInstancias()
    {
        await using SedeHttp liberia = await ArrancarCafe(5);
        await using SedeHttp alajuela = await SedeHttp.ArrancarInicialAsync("ALA", """[{"productoId":"CAF","existencia":9}]""");

        await Reponer(liberia.Cliente, "CAF", Cuerpo(Op1, 3));

        JsonElement deAlajuela = JsonDocument.Parse(await InventarioActualAsync(alajuela.Cliente)).RootElement;
        AfirmarStock(deAlajuela[0], "CAF", 9, 0, 9);
        // La misma clave en otra sede es otra operación: cada sede tiene sus propias claves.
        HttpResponseMessage enAlajuela = await Reponer(alajuela.Cliente, "CAF", Cuerpo(Op1, 3));
        AfirmarStock(await LeerJsonAsync(enAlajuela), "CAF", 12, 0, 12);
    }

    [Fact]
    public async Task Reponer_MetodoNoPermitido_NoModificaElInventario()
    {
        await using SedeHttp sede = await ArrancarCafe(5);

        HttpResponseMessage respuesta = await sede.Cliente.GetAsync(RutaDe("CAF"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, respuesta.StatusCode);
        Assert.Equal(5, sede.Arranque.Inventario.ConsultarInventario()[0].Existencia);
    }

    // ---- Reinicio ----

    [Fact]
    public async Task Reponer_TrasReiniciarLaSede_LaRepeticionDevuelveElMismoCuerpoYElEstadoSigue()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, SedeHttp.Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales,
            """{"sedeId":"LIB","existencias":[{"productoId":"CAF","existencia":5}]}""");
        string primera;
        await using (SedeHttp antes = await SedeHttp.ArrancarAsync(dir, "LIB", poseeDirectorio: false))
        {
            primera = await (await Reponer(antes.Cliente, "CAF", Cuerpo(Op1, 3))).Content.ReadAsStringAsync();
            await Reponer(antes.Cliente, "CAF", Cuerpo(Op2, 1));
        }

        await using SedeHttp despues = await SedeHttp.ArrancarAsync(dir, "LIB", poseeDirectorio: false);
        HttpResponseMessage repetida = await Reponer(despues.Cliente, "CAF", Cuerpo(Op1, 3));

        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal(primera, await repetida.Content.ReadAsStringAsync());
        JsonElement inventario = JsonDocument.Parse(await InventarioActualAsync(despues.Cliente)).RootElement;
        AfirmarStock(inventario[0], "CAF", 9, 0, 9);
        await AfirmarErrorAsync(await Reponer(despues.Cliente, "CAF", Cuerpo(Op1, 7)), HttpStatusCode.Conflict, "CLAVE_REUTILIZADA");
    }
}
