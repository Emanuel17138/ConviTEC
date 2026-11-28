using System;

namespace ConviTEC.Inventario.Dominio;

/// Última observación recibida de otra sede sobre un producto (valores absolutos). Es la <c>Oferta</c> del contrato
/// junto con el producto al que se refiere.
public sealed class OfertaReplicada
{
    public string SedeId { get; }
    public Producto Producto { get; }
    public int Disponible { get; }
    public long Revision { get; }

    /// Clave única de la pareja producto–sede. En orden ordinal, recorrer un índice por esta clave da productoId
    /// ascendente y, dentro de cada producto, sedeId ascendente.
    public string Clave => ClaveDe(Producto.ProductoId, SedeId);

    public OfertaReplicada(string sedeId, Producto producto, int disponible, long revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(producto, nameof(producto));
        if (disponible < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(disponible), disponible, "El disponible no puede ser negativo.");
        }
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), revision, "La revisión no puede ser negativa.");
        }

        SedeId = sedeId;
        Producto = producto;
        Disponible = disponible;
        Revision = revision;
    }

    /// El separador U+001F es menor que cualquier carácter imprimible: "CA" precede a "CAF" y las ofertas de un
    /// mismo producto quedan contiguas.
    public static string ClaveDe(string productoId, string sedeId) => productoId + '\u001F' + sedeId;
}
