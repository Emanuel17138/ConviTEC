namespace ConviTEC.Inventario.Mensajeria;

/// Atiende uno o varios tipos de mensaje de la cola de inventario. RF03 agrega el suyo para <c>catalogo.*</c>
/// sin tocar el despachador ni el consumidor.
public interface IManejadorMensaje
{
    /// Valores de <c>tipo</c> que atiende este manejador.
    string[] Tipos { get; }

    /// Procesa el mensaje y deja durable su efecto antes de devolver las salidas. No debe lanzar por datos
    /// inválidos del mensaje: los reporta en su resultado.
    ResultadoDespacho Manejar(Sobre sobre);
}
