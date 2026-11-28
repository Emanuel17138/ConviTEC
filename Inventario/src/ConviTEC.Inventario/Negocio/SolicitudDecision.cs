using System;

namespace ConviTEC.Inventario.Negocio;

/// Comando <c>stock.confirmar</c> o <c>stock.liberar</c> ya sacado del sobre: cuerpo <c>{pedidoId}</c>.
public sealed class SolicitudDecision
{
    public string MensajeId { get; }
    public string OrigenSedeId { get; }
    public string? PedidoId { get; }

    public SolicitudDecision(string mensajeId, string origenSedeId, string? pedidoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mensajeId, nameof(mensajeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(origenSedeId, nameof(origenSedeId));
        MensajeId = mensajeId;
        OrigenSedeId = origenSedeId;
        PedidoId = pedidoId;
    }
}
