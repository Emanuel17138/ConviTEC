using System;
using ConviTEC.Inventario.Estructuras;
using ConviTEC.Inventario.Negocio;

namespace ConviTEC.Inventario.Mensajeria;

/// Recibe los bytes de la cola <c>inventario.&lt;sedeId&gt;</c>, valida el sobre y lo entrega al manejador de su
/// <c>tipo</c>. Un tipo se registra una sola vez. Es independiente del transporte: lo usa el consumidor RabbitMQ
/// y también las pruebas, y RF03 le suma sus manejadores.
public sealed class DespachadorInventario
{
    private readonly ArbolAvl<string, IManejadorMensaje> _manejadores = EstadoInventario.NuevoIndice<IManejadorMensaje>();

    public string SedeId { get; }

    public DespachadorInventario(string sedeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        SedeId = sedeId;
    }

    /// Registra el manejador para cada uno de sus tipos. Lanza si algún tipo ya tenía manejador.
    public void Registrar(IManejadorMensaje manejador)
    {
        ArgumentNullException.ThrowIfNull(manejador, nameof(manejador));
        string[] tipos = manejador.Tipos;
        if (tipos == null || tipos.Length == 0)
        {
            throw new ArgumentException("El manejador no declara ningún tipo.", nameof(manejador));
        }

        for (int i = 0; i < tipos.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tipos[i], nameof(manejador));
            if (_manejadores.Contiene(tipos[i]))
            {
                throw new InvalidOperationException($"El tipo '{tipos[i]}' ya tiene un manejador registrado.");
            }
        }
        for (int i = 0; i < tipos.Length; i++)
        {
            _manejadores.Insertar(tipos[i], manejador);
        }
    }

    public bool AtiendeTipo(string tipo)
    {
        ArgumentNullException.ThrowIfNull(tipo, nameof(tipo));
        return _manejadores.Contiene(tipo);
    }

    public ResultadoDespacho Despachar(byte[]? datos)
    {
        if (!CodificadorSobre.TryDecodificar(datos, out Sobre? sobre, out string motivo))
        {
            return ResultadoDespacho.Descartar("Sobre ilegible: " + motivo);
        }
        return Despachar(sobre!);
    }

    public ResultadoDespacho Despachar(Sobre sobre)
    {
        ArgumentNullException.ThrowIfNull(sobre, nameof(sobre));

        if (!string.Equals(sobre.DestinoSedeId, SedeId, StringComparison.Ordinal))
        {
            return ResultadoDespacho.Descartar(
                $"El mensaje {sobre.MensajeId} es para '{sobre.DestinoSedeId}', no para '{SedeId}'.");
        }
        if (!_manejadores.Buscar(sobre.Tipo, out IManejadorMensaje? manejador))
        {
            return ResultadoDespacho.Descartar($"Tipo '{sobre.Tipo}' sin manejador (mensaje {sobre.MensajeId}).");
        }
        return manejador.Manejar(sobre);
    }
}
