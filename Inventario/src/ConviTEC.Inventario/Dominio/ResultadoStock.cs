using System;

namespace ConviTEC.Inventario.Dominio;

/// Cuerpo de <c>stock.resultado</c>: <c>{pedidoId,operacion,estadoReserva,codigo,mensaje}</c>.
/// <see cref="PedidoId"/> puede ser vacío si el comando recibido no lo traía.
public sealed class ResultadoStock
{
    public string PedidoId { get; }
    public string Operacion { get; }
    public EstadoReserva EstadoReserva { get; }
    public string Codigo { get; }
    public string Mensaje { get; }

    public ResultadoStock(string pedidoId, string operacion, EstadoReserva estadoReserva, string codigo, string mensaje)
    {
        ArgumentNullException.ThrowIfNull(pedidoId, nameof(pedidoId));
        ArgumentException.ThrowIfNullOrWhiteSpace(operacion, nameof(operacion));
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo, nameof(codigo));
        ArgumentNullException.ThrowIfNull(mensaje, nameof(mensaje));

        PedidoId = pedidoId;
        Operacion = operacion;
        EstadoReserva = estadoReserva;
        Codigo = codigo;
        Mensaje = mensaje;
    }
}
