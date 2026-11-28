using System;

namespace ConviTEC.Inventario.Dominio;

/// Observación absoluta de una sede sobre un producto: cuerpo de <c>catalogo.actualizado</c> y cada elemento de
/// <c>catalogo.instantanea</c>. La sede propietaria no va aquí: la indica el origenSedeId del mensaje.
public sealed class EntradaCatalogo
{
    public Producto Producto { get; }
    public int Disponible { get; }
    public long Revision { get; }

    public EntradaCatalogo(Producto producto, int disponible, long revision)
    {
        ArgumentNullException.ThrowIfNull(producto, nameof(producto));
        if (disponible < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(disponible), disponible, "El disponible no puede ser negativo.");
        }
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), revision, "La revisión no puede ser negativa.");
        }

        Producto = producto;
        Disponible = disponible;
        Revision = revision;
    }
}
