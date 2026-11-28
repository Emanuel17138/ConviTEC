using System;

namespace ConviTEC.Inventario.Negocio;

/// Comando <c>stock.reservar</c> ya sacado del sobre. <see cref="MensajeId"/> y <see cref="OrigenSedeId"/>
/// vienen del sobre y son obligatorios. El cuerpo puede estar incompleto: eso se responde como DATOS_INVALIDOS.
public sealed class SolicitudReserva
{
    public string MensajeId { get; }
    public string OrigenSedeId { get; }
    public string? PedidoId { get; }
    public LineaSolicitada?[]? Lineas { get; }

    public SolicitudReserva(string mensajeId, string origenSedeId, string? pedidoId, LineaSolicitada?[]? lineas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mensajeId, nameof(mensajeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(origenSedeId, nameof(origenSedeId));
        MensajeId = mensajeId;
        OrigenSedeId = origenSedeId;
        PedidoId = pedidoId;
        Lineas = lineas;
    }
}
