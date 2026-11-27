using System;
using System.Diagnostics.CodeAnalysis;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;

namespace ConviTEC.Inventario.Negocio;

/// Estado de inventario de una sede, indexado por productoId en un AVL propio.
/// Solo lo crea <see cref="CargadorInventario"/> con datos ya validados, así que nunca existe un estado parcial.
public sealed class InventarioLocal
{
    private readonly ArbolAvl<string, StockLocal> _indice;

    public string SedeId { get; }

    internal InventarioLocal(string sedeId, ArbolAvl<string, StockLocal> indice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(indice, nameof(indice));
        SedeId = sedeId;
        _indice = indice;
    }

    public int Cantidad => _indice.Cantidad;

    /// Índice vacío con comparación ordinal: sensible a mayúsculas.
    internal static ArbolAvl<string, StockLocal> NuevoIndice() => new ArbolAvl<string, StockLocal>(StringComparer.Ordinal);

    /// Todo el stock de la sede por productoId ascendente, agotados incluidos. Arreglo vacío si no hay productos.
    public StockLocal[] ConsultarInventario() => _indice.ValoresEnOrden();

    /// Búsqueda exacta (distingue mayúsculas). Devuelve false si el producto no es de esta sede.
    public bool BuscarStock(string productoId, [MaybeNullWhen(false)] out StockLocal stock)
    {
        ArgumentNullException.ThrowIfNull(productoId, nameof(productoId));
        return _indice.Buscar(productoId, out stock);
    }

    public bool Contiene(string productoId)
    {
        ArgumentNullException.ThrowIfNull(productoId, nameof(productoId));
        return _indice.Contiene(productoId);
    }
}
