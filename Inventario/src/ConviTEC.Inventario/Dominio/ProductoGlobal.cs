using System;

namespace ConviTEC.Inventario.Dominio;

/// ProductoGlobal del contrato v1: <c>{producto,ofertas}</c>. Las ofertas van por sedeId ascendente (orden ordinal),
/// pueden valer cero y no incluyen a las sedes sin datos de este producto.
public sealed class ProductoGlobal
{
    private readonly Oferta[] _ofertas;

    public Producto Producto { get; }

    /// Copia de las ofertas: quien consulta no puede alterar la vista.
    public Oferta[] Ofertas
    {
        get
        {
            Oferta[] copia = new Oferta[_ofertas.Length];
            Array.Copy(_ofertas, copia, _ofertas.Length);
            return copia;
        }
    }

    public ProductoGlobal(Producto producto, Oferta[] ofertas)
    {
        ArgumentNullException.ThrowIfNull(producto, nameof(producto));
        ArgumentNullException.ThrowIfNull(ofertas, nameof(ofertas));

        Oferta[] copia = new Oferta[ofertas.Length];
        for (int i = 0; i < ofertas.Length; i++)
        {
            if (ofertas[i] == null)
            {
                throw new ArgumentException($"La oferta {i} es nula.", nameof(ofertas));
            }
            copia[i] = ofertas[i];
        }

        Producto = producto;
        _ofertas = copia;
    }
}
