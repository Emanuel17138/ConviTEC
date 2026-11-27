using System;

namespace ConviTEC.Inventario.Dominio;

/// Comando de stock ya procesado: su mensajeId, el contenido con que llegó y el resultado original.
/// Sirve para devolver el mismo resultado a un duplicado, aunque el estado haya avanzado, y para detectar
/// el mismo mensajeId con otro contenido (CLAVE_REUTILIZADA).
public sealed class ComandoRegistrado
{
    public string MensajeId { get; }
    public string Tipo { get; }
    public string OrigenSedeId { get; }

    /// Huella canónica del cuerpo recibido; la calcula quien procesa el comando.
    public string Contenido { get; }

    public ResultadoStock Resultado { get; }

    public ComandoRegistrado(string mensajeId, string tipo, string origenSedeId, string contenido, ResultadoStock resultado)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mensajeId, nameof(mensajeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo, nameof(tipo));
        ArgumentException.ThrowIfNullOrWhiteSpace(origenSedeId, nameof(origenSedeId));
        ArgumentNullException.ThrowIfNull(contenido, nameof(contenido));
        ArgumentNullException.ThrowIfNull(resultado, nameof(resultado));

        MensajeId = mensajeId;
        Tipo = tipo;
        OrigenSedeId = origenSedeId;
        Contenido = contenido;
        Resultado = resultado;
    }
}
