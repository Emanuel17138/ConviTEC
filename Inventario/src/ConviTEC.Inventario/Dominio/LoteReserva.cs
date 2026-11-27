using System;

namespace ConviTEC.Inventario.Dominio;

/// Lote de reserva de un pedido en esta proveedora. Es inmutable: una transición produce otra instancia
/// con <see cref="ConEstado"/>. Las líneas pueden estar vacías solo si el lote es LIBERADA (liberación anticipada)
/// o RECHAZADA (el contenido rechazado puede no ser representable como líneas válidas).
public sealed class LoteReserva
{
    private readonly LineaReserva[] _lineas;

    public string PedidoId { get; }
    public string CoordinadoraSedeId { get; }
    public EstadoReserva Estado { get; }

    public LoteReserva(string pedidoId, string coordinadoraSedeId, EstadoReserva estado, LineaReserva[] lineas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pedidoId, nameof(pedidoId));
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinadoraSedeId, nameof(coordinadoraSedeId));
        ArgumentNullException.ThrowIfNull(lineas, nameof(lineas));
        if (estado == EstadoReserva.Inexistente)
        {
            throw new ArgumentException("INEXISTENTE no es un estado guardable de un lote.", nameof(estado));
        }
        if ((estado == EstadoReserva.Reservada || estado == EstadoReserva.Confirmada) && lineas.Length == 0)
        {
            throw new ArgumentException("Un lote RESERVADA o CONFIRMADA necesita al menos una línea.", nameof(lineas));
        }

        LineaReserva[] copia = new LineaReserva[lineas.Length];
        for (int i = 0; i < lineas.Length; i++)
        {
            if (lineas[i] == null)
            {
                throw new ArgumentException($"La línea {i} es nula.", nameof(lineas));
            }
            copia[i] = lineas[i];
        }

        PedidoId = pedidoId;
        CoordinadoraSedeId = coordinadoraSedeId;
        Estado = estado;
        _lineas = copia;
    }

    /// Copia de las líneas: el lote no expone su arreglo interno.
    public LineaReserva[] Lineas()
    {
        LineaReserva[] copia = new LineaReserva[_lineas.Length];
        for (int i = 0; i < _lineas.Length; i++)
        {
            copia[i] = _lineas[i];
        }
        return copia;
    }

    public int CantidadLineas => _lineas.Length;

    public LoteReserva ConEstado(EstadoReserva nuevoEstado) => new LoteReserva(PedidoId, CoordinadoraSedeId, nuevoEstado, _lineas);
}
