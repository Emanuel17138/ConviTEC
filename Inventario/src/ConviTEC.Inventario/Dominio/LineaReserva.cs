using System;

namespace ConviTEC.Inventario.Dominio;

/// Una línea de un lote de reserva: producto, cantidad positiva y precio acordado en céntimos.
public sealed class LineaReserva
{
    public string ProductoId { get; }
    public int Cantidad { get; }
    public long PrecioUnitarioCentimos { get; }

    public LineaReserva(string productoId, int cantidad, long precioUnitarioCentimos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productoId, nameof(productoId));
        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), cantidad, "La cantidad de una línea debe ser positiva.");
        }
        if (precioUnitarioCentimos < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(precioUnitarioCentimos), precioUnitarioCentimos,
                "El precio unitario en céntimos no puede ser negativo.");
        }

        ProductoId = productoId;
        Cantidad = cantidad;
        PrecioUnitarioCentimos = precioUnitarioCentimos;
    }
}
