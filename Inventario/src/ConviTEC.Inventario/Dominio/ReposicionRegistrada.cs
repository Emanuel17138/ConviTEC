using System;

namespace ConviTEC.Inventario.Dominio;

/// Reposición ya aplicada: la clave de repetición (operacionId), lo que pidió y el StockLocal que resultó.
/// El producto completo no se copia: se obtiene del stock por productoId.
public sealed class ReposicionRegistrada
{
    public string OperacionId { get; }
    public string ProductoId { get; }
    public int Cantidad { get; }
    public int Existencia { get; }
    public int Reservada { get; }

    public int Disponible => Existencia - Reservada;

    public ReposicionRegistrada(string operacionId, string productoId, int cantidad, int existencia, int reservada)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operacionId, nameof(operacionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(productoId, nameof(productoId));
        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), cantidad, "La cantidad repuesta debe ser positiva.");
        }
        if (reservada < 0 || existencia < reservada)
        {
            throw new ArgumentOutOfRangeException(nameof(reservada), reservada,
                "El resultado guardado debe cumplir 0 ≤ reservada ≤ existencia.");
        }

        OperacionId = operacionId;
        ProductoId = productoId;
        Cantidad = cantidad;
        Existencia = existencia;
        Reservada = reservada;
    }
}
