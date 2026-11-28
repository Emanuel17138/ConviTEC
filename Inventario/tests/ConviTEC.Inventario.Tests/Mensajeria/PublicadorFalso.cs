using System;
using System.Threading;
using System.Threading.Tasks;
using ConviTEC.Inventario.Mensajeria;

namespace ConviTEC.Inventario.Tests.Mensajeria;

/// Publicador en memoria: guarda lo publicado en un arreglo propio y puede simular que el broker no entrega.
internal sealed class PublicadorFalso : IPublicadorMensajes
{
    private SobreSaliente[] _enviados = Array.Empty<SobreSaliente>();

    public bool Falla { get; set; }

    /// Falla solo para las claves de ruta que cumplan el predicado (por ejemplo, una sede sin cola).
    public Func<string, bool>? FallaPara { get; set; }

    public SobreSaliente[] Enviados => _enviados;

    public Task PublicarAsync(string claveRuta, Sobre sobre, CancellationToken cancelacion = default)
    {
        if (Falla || (FallaPara != null && FallaPara(claveRuta)))
        {
            throw new EnvioFallidoException("Sin destinatario (simulado).");
        }

        SobreSaliente[] nuevos = new SobreSaliente[_enviados.Length + 1];
        Array.Copy(_enviados, nuevos, _enviados.Length);
        nuevos[_enviados.Length] = new SobreSaliente(claveRuta, sobre);
        _enviados = nuevos;
        return Task.CompletedTask;
    }
}
