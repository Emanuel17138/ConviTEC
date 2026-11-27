using System.Diagnostics.CodeAnalysis;

namespace ConviTEC.Inventario.Dominio;

/// Estado de un lote de reserva o de la respuesta a un comando de stock. Inexistente solo describe
/// la respuesta a una confirmación sin reserva: nunca es el estado guardado de un lote.
public enum EstadoReserva
{
    Reservada,
    Confirmada,
    Liberada,
    Rechazada,
    Inexistente,
}

/// Texto del contrato v1 (MAYÚSCULAS) de cada estado.
public static class TextoEstadoReserva
{
    public static string ATexto(EstadoReserva estado) => estado switch
    {
        EstadoReserva.Reservada => "RESERVADA",
        EstadoReserva.Confirmada => "CONFIRMADA",
        EstadoReserva.Liberada => "LIBERADA",
        EstadoReserva.Rechazada => "RECHAZADA",
        EstadoReserva.Inexistente => "INEXISTENTE",
        _ => throw new System.ArgumentOutOfRangeException(nameof(estado), estado, "Estado de reserva desconocido."),
    };

    /// Comparación ordinal exacta: "reservada" no es válido.
    public static bool TryParse(string? texto, [NotNullWhen(true)] out EstadoReserva? estado)
    {
        switch (texto)
        {
            case "RESERVADA": estado = EstadoReserva.Reservada; return true;
            case "CONFIRMADA": estado = EstadoReserva.Confirmada; return true;
            case "LIBERADA": estado = EstadoReserva.Liberada; return true;
            case "RECHAZADA": estado = EstadoReserva.Rechazada; return true;
            case "INEXISTENTE": estado = EstadoReserva.Inexistente; return true;
            default: estado = null; return false;
        }
    }
}
