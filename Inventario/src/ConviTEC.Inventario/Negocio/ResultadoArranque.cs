using System;

namespace ConviTEC.Inventario.Negocio;

public sealed class ResultadoArranque
{
    public InventarioLocal Inventario { get; }
    public ModoArranque Modo { get; }

    public ResultadoArranque(InventarioLocal inventario, ModoArranque modo)
    {
        ArgumentNullException.ThrowIfNull(inventario, nameof(inventario));
        Inventario = inventario;
        Modo = modo;
    }
}
