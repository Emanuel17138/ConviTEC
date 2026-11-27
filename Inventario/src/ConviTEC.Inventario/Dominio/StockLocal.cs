using System;

namespace ConviTEC.Inventario.Dominio;

/// Existencias de un producto en la sede. Invariante: 0 ≤ reservada ≤ existencia
/// <see cref="Disponible"/> se calcula, nunca se almacena, para que no pueda desincronizarse.
/// Es inmutable; los movimientos de stock producen nuevas instancias.
public sealed class StockLocal
{
    public Producto Producto { get; }
    public int Existencia { get; }
    public int Reservada { get; }

    public int Disponible => Existencia - Reservada;

    public StockLocal(Producto producto, int existencia, int reservada)
    {
        ArgumentNullException.ThrowIfNull(producto, nameof(producto));
        if (reservada < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reservada), reservada,
                "La cantidad reservada no puede ser negativa.");
        }
        if (existencia < reservada)
        {
            throw new ArgumentOutOfRangeException(nameof(existencia), existencia,
                $"La existencia ({existencia}) no puede ser menor que la cantidad reservada ({reservada}).");
        }

        Producto = producto;
        Existencia = existencia;
        Reservada = reservada;
    }
}
