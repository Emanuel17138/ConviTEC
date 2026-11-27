using System;

namespace ConviTEC.Inventario.Dominio;

/// Intención guardada de enviar <c>catalogo.actualizado</c> a la cola de inventario de otra sede.
/// Lleva el disponible y la revisión absolutos del momento del cambio. Por pareja destino–producto solo
/// se conserva la última (los valores son absolutos). RF03 la envía y la retira tras la confirmación del broker.
public sealed class SalidaPendiente
{
    public const string TipoMensaje = "catalogo.actualizado";

    public string MensajeId { get; }
    public string DestinoSedeId { get; }
    public string ProductoId { get; }
    public int Disponible { get; }
    public long Revision { get; }

    /// Clave única de la pareja destino–producto.
    public string Clave => ClaveDe(DestinoSedeId, ProductoId);

    public SalidaPendiente(string mensajeId, string destinoSedeId, string productoId, int disponible, long revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mensajeId, nameof(mensajeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(destinoSedeId, nameof(destinoSedeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(productoId, nameof(productoId));
        if (disponible < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(disponible), disponible, "El disponible no puede ser negativo.");
        }
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), revision, "La revisión no puede ser negativa.");
        }

        MensajeId = mensajeId;
        DestinoSedeId = destinoSedeId;
        ProductoId = productoId;
        Disponible = disponible;
        Revision = revision;
    }

    /// El separador U+001F no aparece en identificadores de texto normales.
    public static string ClaveDe(string destinoSedeId, string productoId) => destinoSedeId + '\u001F' + productoId;
}
