using System;
using System.Text.Json;

namespace ConviTEC.Inventario.Mensajeria;

/// Sobre único v1: <c>{version,tipo,mensajeId,correlacionId,origenSedeId,destinoSedeId,cuerpo}</c>.
/// Inmutable. El cuerpo se conserva como JSON porque cada tipo de mensaje tiene el suyo.
public sealed class Sobre
{
    public string Tipo { get; }
    public string MensajeId { get; }
    public string CorrelacionId { get; }
    public string OrigenSedeId { get; }
    public string DestinoSedeId { get; }
    public JsonElement Cuerpo { get; }

    public Sobre(string tipo, string mensajeId, string correlacionId, string origenSedeId, string destinoSedeId, JsonElement cuerpo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo, nameof(tipo));
        ArgumentException.ThrowIfNullOrWhiteSpace(mensajeId, nameof(mensajeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(correlacionId, nameof(correlacionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(origenSedeId, nameof(origenSedeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(destinoSedeId, nameof(destinoSedeId));
        if (cuerpo.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("El sobre necesita un cuerpo (puede ser null o {}).", nameof(cuerpo));
        }

        Tipo = tipo;
        MensajeId = mensajeId;
        CorrelacionId = correlacionId;
        OrigenSedeId = origenSedeId;
        DestinoSedeId = destinoSedeId;
        Cuerpo = cuerpo.Clone();
    }

    /// Crea un sobre a partir del texto JSON de su cuerpo. Lanza <see cref="JsonException"/> si no es JSON válido.
    public static Sobre ConCuerpoTexto(
        string tipo, string mensajeId, string correlacionId, string origenSedeId, string destinoSedeId, string cuerpoJson)
    {
        ArgumentNullException.ThrowIfNull(cuerpoJson, nameof(cuerpoJson));
        using JsonDocument documento = JsonDocument.Parse(cuerpoJson);
        return new Sobre(tipo, mensajeId, correlacionId, origenSedeId, destinoSedeId, documento.RootElement);
    }
}
