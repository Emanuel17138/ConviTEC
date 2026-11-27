using System;

namespace ConviTEC.Inventario.Dominio;

/// Stock de un producto junto con su revisión (entero creciente por producto de esta sede).
/// La revisión no forma parte del <see cref="StockLocal"/> que ve la API: solo la usa el catálogo replicado.
public sealed class EntradaStock
{
    public StockLocal Stock { get; }
    public long Revision { get; }

    public EntradaStock(StockLocal stock, long revision)
    {
        ArgumentNullException.ThrowIfNull(stock, nameof(stock));
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), revision, "La revisión no puede ser negativa.");
        }
        Stock = stock;
        Revision = revision;
    }
}
