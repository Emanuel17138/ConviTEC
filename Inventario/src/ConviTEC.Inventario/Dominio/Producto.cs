using System;

namespace ConviTEC.Inventario.Dominio;

/// Producto maestro. El mismo código identifica el mismo producto y precio en las sedes.
/// Es inmutable: una vez construido cumple todas las reglas de validación.
public sealed class Producto
{
    public string ProductoId { get; }
    public string Nombre { get; }
    public string Descripcion { get; }
    public string Categoria { get; }
    public long PrecioUnitarioCentimos { get; }

    public Producto(string productoId, string nombre, string descripcion, string categoria, long precioUnitarioCentimos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productoId, nameof(productoId));
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre, nameof(nombre));
        ArgumentNullException.ThrowIfNull(descripcion, nameof(descripcion));
        ArgumentNullException.ThrowIfNull(categoria, nameof(categoria));
        if (precioUnitarioCentimos < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(precioUnitarioCentimos), precioUnitarioCentimos,
                "El precio unitario en céntimos no puede ser negativo.");
        }

        ProductoId = productoId;
        Nombre = nombre;
        Descripcion = descripcion;
        Categoria = categoria;
        PrecioUnitarioCentimos = precioUnitarioCentimos;
    }
}
