using System;
using System.IO;
using System.Text.Json;

namespace ConviTEC.Inventario.Mensajeria;

/// Convierte el sobre v1 a bytes (JSON UTF-8, camelCase) y de vuelta. La decodificación nunca lanza: un mensaje
/// ilegible se reporta con su motivo para que el consumidor lo descarte sin bloquear la cola.
public static class CodificadorSobre
{
    public static byte[] Codificar(Sobre sobre)
    {
        ArgumentNullException.ThrowIfNull(sobre, nameof(sobre));

        using MemoryStream salida = new MemoryStream();
        using (Utf8JsonWriter escritor = new Utf8JsonWriter(salida))
        {
            escritor.WriteStartObject();
            escritor.WriteNumber("version", TopologiaConvitec.VersionSobre);
            escritor.WriteString("tipo", sobre.Tipo);
            escritor.WriteString("mensajeId", sobre.MensajeId);
            escritor.WriteString("correlacionId", sobre.CorrelacionId);
            escritor.WriteString("origenSedeId", sobre.OrigenSedeId);
            escritor.WriteString("destinoSedeId", sobre.DestinoSedeId);
            escritor.WritePropertyName("cuerpo");
            sobre.Cuerpo.WriteTo(escritor);
            escritor.WriteEndObject();
        }
        return salida.ToArray();
    }

    public static bool TryDecodificar(byte[]? datos, out Sobre? sobre, out string motivo)
    {
        sobre = null;
        if (datos == null || datos.Length == 0)
        {
            motivo = "El mensaje está vacío.";
            return false;
        }

        try
        {
            using JsonDocument documento = JsonDocument.Parse(datos);
            return TryLeer(documento.RootElement, out sobre, out motivo);
        }
        catch (JsonException e)
        {
            motivo = "JSON inválido: " + e.Message;
            return false;
        }
    }

    private static bool TryLeer(JsonElement raiz, out Sobre? sobre, out string motivo)
    {
        sobre = null;
        if (raiz.ValueKind != JsonValueKind.Object)
        {
            motivo = "El sobre debe ser un objeto JSON.";
            return false;
        }

        if (!raiz.TryGetProperty("version", out JsonElement version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out int numeroVersion))
        {
            motivo = "Falta 'version' o no es un entero.";
            return false;
        }
        if (numeroVersion != TopologiaConvitec.VersionSobre)
        {
            motivo = $"Versión {numeroVersion} no soportada (se espera {TopologiaConvitec.VersionSobre}).";
            return false;
        }

        if (!TryTexto(raiz, "tipo", out string? tipo, out motivo)
            || !TryTexto(raiz, "mensajeId", out string? mensajeId, out motivo)
            || !TryTexto(raiz, "correlacionId", out string? correlacionId, out motivo)
            || !TryTexto(raiz, "origenSedeId", out string? origenSedeId, out motivo)
            || !TryTexto(raiz, "destinoSedeId", out string? destinoSedeId, out motivo))
        {
            return false;
        }
        if (!raiz.TryGetProperty("cuerpo", out JsonElement cuerpo))
        {
            motivo = "Falta 'cuerpo'.";
            return false;
        }

        sobre = new Sobre(tipo!, mensajeId!, correlacionId!, origenSedeId!, destinoSedeId!, cuerpo);
        motivo = string.Empty;
        return true;
    }

    private static bool TryTexto(JsonElement raiz, string campo, out string? valor, out string motivo)
    {
        valor = null;
        if (!raiz.TryGetProperty(campo, out JsonElement elemento) || elemento.ValueKind != JsonValueKind.String)
        {
            motivo = $"Falta '{campo}' o no es texto.";
            return false;
        }

        string? texto = elemento.GetString();
        if (string.IsNullOrWhiteSpace(texto))
        {
            motivo = $"'{campo}' está vacío.";
            return false;
        }

        valor = texto;
        motivo = string.Empty;
        return true;
    }
}
