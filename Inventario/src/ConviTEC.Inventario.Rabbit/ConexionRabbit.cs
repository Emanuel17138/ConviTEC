using System;
using System.Threading;
using System.Threading.Tasks;
using ConviTEC.Inventario.Mensajeria;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace ConviTEC.Inventario.Rabbit;

/// Conexión de la sede al broker y declaración de la topología v1: exchange directo durable <c>convitec.v1</c> y,
/// por sede, las colas durables <c>inventario.&lt;sedeId&gt;</c> y <c>pedidos.&lt;sedeId&gt;</c> vinculadas con claves
/// del mismo nombre. Declarar es idempotente, así que se declaran también las de las otras sedes: un mensaje para una
/// sede apagada espera en su cola en vez de devolverse.
public sealed class ConexionRabbit : IAsyncDisposable
{
    public const string UriPorDefecto = "amqp://guest:guest@localhost:5672/";

    private readonly IConnection _conexion;

    internal IConnection Conexion => _conexion;

    private ConexionRabbit(IConnection conexion)
    {
        _conexion = conexion;
    }

    /// Abre la conexión con recuperación automática. Lanza <see cref="EnvioFallidoException"/> si el broker no responde.
    public static async Task<ConexionRabbit> ConectarAsync(string uri, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri, nameof(uri));
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? direccion))
        {
            throw new ArgumentException($"La dirección del broker '{uri}' no es una URI válida.", nameof(uri));
        }

        ConnectionFactory fabrica = new ConnectionFactory
        {
            Uri = direccion,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            ClientProvidedName = "convitec-inventario",
        };

        try
        {
            return new ConexionRabbit(await fabrica.CreateConnectionAsync(cancelacion));
        }
        catch (BrokerUnreachableException e)
        {
            throw new EnvioFallidoException($"No se pudo conectar con el broker en {direccion.Host}:{direccion.Port}.", e);
        }
    }

    public async Task DeclararTopologiaAsync(string[] sedes, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(sedes, nameof(sedes));

        await using IChannel canal = await _conexion.CreateChannelAsync(cancellationToken: cancelacion);
        await canal.ExchangeDeclareAsync(
            TopologiaConvitec.Exchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancelacion);
        for (int i = 0; i < sedes.Length; i++)
        {
            await DeclararColaAsync(canal, TopologiaConvitec.ColaInventario(sedes[i]), cancelacion);
            await DeclararColaAsync(canal, TopologiaConvitec.ColaPedidos(sedes[i]), cancelacion);
        }
    }

    private static async Task DeclararColaAsync(IChannel canal, string cola, CancellationToken cancelacion)
    {
        await canal.QueueDeclareAsync(cola, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancelacion);
        await canal.QueueBindAsync(cola, TopologiaConvitec.Exchange, cola, cancellationToken: cancelacion);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _conexion.CloseAsync();
        }
        catch (Exception e) when (e is AlreadyClosedException || e is OperationInterruptedException)
        {
            // Ya estaba cerrada: nada que hacer.
        }
        _conexion.Dispose();
    }
}
