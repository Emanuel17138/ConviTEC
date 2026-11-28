using System.Text;
using System.Text.Json;
using ConviTEC.Inventario.Mensajeria;

namespace ConviTEC.Inventario.Tests.Mensajeria;

/// Sobre v1: JSON UTF-8 en camelCase, ida y vuelta, y mensajes ilegibles reportados sin lanzar.
public class CodificadorSobreTests
{
    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    private const string Valido =
        "{\"version\":1,\"tipo\":\"stock.confirmar\",\"mensajeId\":\"m-1\",\"correlacionId\":\"m-1\","
        + "\"origenSedeId\":\"SJ\",\"destinoSedeId\":\"LIB\",\"cuerpo\":{\"pedidoId\":\"p-1\"}}";

    [Fact]
    public void IdaYVuelta_ConservaTodosLosCampos()
    {
        Sobre original = Sobre.ConCuerpoTexto("stock.liberar", "m-9", "m-9", "SJ", "LIB", "{\"pedidoId\":\"ñandú-ü\"}");

        byte[] datos = CodificadorSobre.Codificar(original);

        Assert.True(CodificadorSobre.TryDecodificar(datos, out Sobre? leido, out string motivo), motivo);
        Assert.Equal("stock.liberar", leido!.Tipo);
        Assert.Equal("m-9", leido.MensajeId);
        Assert.Equal("m-9", leido.CorrelacionId);
        Assert.Equal("SJ", leido.OrigenSedeId);
        Assert.Equal("LIB", leido.DestinoSedeId);
        Assert.Equal("ñandú-ü", leido.Cuerpo.GetProperty("pedidoId").GetString());
    }

    [Fact]
    public void Codificar_EscribeClavesEnCamelCaseYVersion1()
    {
        Sobre sobre = Sobre.ConCuerpoTexto("stock.confirmar", "m-1", "m-1", "SJ", "LIB", "{}");

        using JsonDocument documento = JsonDocument.Parse(CodificadorSobre.Codificar(sobre));
        JsonElement raiz = documento.RootElement;

        Assert.Equal(1, raiz.GetProperty("version").GetInt32());
        Assert.Equal("stock.confirmar", raiz.GetProperty("tipo").GetString());
        Assert.Equal("m-1", raiz.GetProperty("mensajeId").GetString());
        Assert.Equal("m-1", raiz.GetProperty("correlacionId").GetString());
        Assert.Equal("SJ", raiz.GetProperty("origenSedeId").GetString());
        Assert.Equal("LIB", raiz.GetProperty("destinoSedeId").GetString());
        Assert.Equal(JsonValueKind.Object, raiz.GetProperty("cuerpo").ValueKind);
    }

    [Fact]
    public void Decodificar_MensajeValido()
    {
        Assert.True(CodificadorSobre.TryDecodificar(Bytes(Valido), out Sobre? sobre, out _));
        Assert.Equal("p-1", sobre!.Cuerpo.GetProperty("pedidoId").GetString());
    }

    [Fact]
    public void Decodificar_CuerpoNullEsValido()
    {
        string json = Valido.Replace("{\"pedidoId\":\"p-1\"}", "null");

        Assert.True(CodificadorSobre.TryDecodificar(Bytes(json), out Sobre? sobre, out _));
        Assert.Equal(JsonValueKind.Null, sobre!.Cuerpo.ValueKind);
    }

    [Fact]
    public void Decodificar_NuloOVacio_NoLanza()
    {
        Assert.False(CodificadorSobre.TryDecodificar(null, out Sobre? sobre, out string motivo));
        Assert.Null(sobre);
        Assert.False(string.IsNullOrEmpty(motivo));
        Assert.False(CodificadorSobre.TryDecodificar(new byte[0], out _, out _));
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"texto\"")]
    [InlineData("{}")]
    public void Decodificar_FormaInvalida_NoLanza(string json)
    {
        Assert.False(CodificadorSobre.TryDecodificar(Bytes(json), out Sobre? sobre, out string motivo));
        Assert.Null(sobre);
        Assert.False(string.IsNullOrEmpty(motivo));
    }

    [Fact]
    public void Decodificar_VersionDistinta_SeRechaza()
    {
        string json = Valido.Replace("\"version\":1", "\"version\":2");

        Assert.False(CodificadorSobre.TryDecodificar(Bytes(json), out _, out string motivo));
        Assert.Contains("Versión 2", motivo);
    }

    [Theory]
    [InlineData("tipo")]
    [InlineData("mensajeId")]
    [InlineData("correlacionId")]
    [InlineData("origenSedeId")]
    [InlineData("destinoSedeId")]
    [InlineData("cuerpo")]
    public void Decodificar_CampoFaltante_SeRechazaNombrandoElCampo(string campo)
    {
        using JsonDocument original = JsonDocument.Parse(Valido);
        using System.IO.MemoryStream salida = new System.IO.MemoryStream();
        using (Utf8JsonWriter escritor = new Utf8JsonWriter(salida))
        {
            escritor.WriteStartObject();
            foreach (JsonProperty propiedad in original.RootElement.EnumerateObject())
            {
                if (propiedad.Name != campo)
                {
                    propiedad.WriteTo(escritor);
                }
            }
            escritor.WriteEndObject();
        }

        Assert.False(CodificadorSobre.TryDecodificar(salida.ToArray(), out _, out string motivo));
        Assert.Contains(campo, motivo);
    }

    [Fact]
    public void Decodificar_CampoVacioOConTipoErroneo_SeRechaza()
    {
        Assert.False(CodificadorSobre.TryDecodificar(Bytes(Valido.Replace("\"SJ\"", "\"\"")), out _, out _));
        Assert.False(CodificadorSobre.TryDecodificar(Bytes(Valido.Replace("\"m-1\"", "7")), out _, out _));
    }

    [Fact]
    public void Decodificar_VersionComoTexto_SeRechaza()
    {
        Assert.False(CodificadorSobre.TryDecodificar(Bytes(Valido.Replace("\"version\":1", "\"version\":\"1\"")), out _, out _));
    }

    [Fact]
    public void Sobre_SinCuerpoDefinido_Lanza()
    {
        Assert.Throws<System.ArgumentException>(() => new Sobre("t", "m", "m", "SJ", "LIB", default));
    }
}
