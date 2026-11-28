using System.Threading;
using System.Threading.Tasks;

namespace ConviTEC.Inventario.Mensajeria;

/// Puerto de salida hacia el broker. Termina bien solo cuando el broker confirmó la publicación y el mensaje
/// llegó a una cola. En cualquier otro caso lanza <see cref="EnvioFallidoException"/> y quien envía debe reintentar
/// con el mismo mensajeId.
public interface IPublicadorMensajes
{
    Task PublicarAsync(string claveRuta, Sobre sobre, CancellationToken cancelacion = default);
}
