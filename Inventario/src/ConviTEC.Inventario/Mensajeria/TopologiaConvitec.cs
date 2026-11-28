using System;

namespace ConviTEC.Inventario.Mensajeria;

/// Nombres fijos del contrato v1: un exchange directo durable y, por sede, una cola de inventario
/// (la consume Integrante 1) y una de pedidos (la consume Integrante 2). Cada cola se vincula con una
/// clave de enrutamiento igual a su nombre.
public static class TopologiaConvitec
{
    public const string Exchange = "convitec.v1";
    public const int VersionSobre = 1;

    public static string ColaInventario(string sedeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        return "inventario." + sedeId;
    }

    public static string ColaPedidos(string sedeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        return "pedidos." + sedeId;
    }
}
