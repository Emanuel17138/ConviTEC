using System;
using System.Threading;
using System.Threading.Tasks;
using ConviTEC.Inventario.Mensajeria;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace ConviTEC.Inventario.Rabbit;

/// Publica en el exchange <c>convitec.v1</c> con mensajes persistentes, confirmación del publicador y
/// <c>mandatory</c>: si el broker no confirma o ninguna cola recibe el mensaje, lanza
/// <see cref="EnvioFallidoException"/> y quien envía reintenta con el mismo mensajeId.
public sealed class PublicadorRabbit : IPublicadorMensajes, IAsyncDisposable
{
    private readonly ConexionRabbit _conexion;
    private readonly SemaphoreSlim _turno = new SemaphoreSlim(1, 1);
    private IChannel? _canal;

    public PublicadorRabbit(ConexionRabbit conexion)
    {
        ArgumentNullException.ThrowIfNull(conexion, nameof(conexion));
        _conexion = conexion;
    }

    public async Task PublicarAsync(string claveRuta, Sobre sobre, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claveRuta, nameof(claveRuta));
        ArgumentNullException.ThrowIfNull(sobre, nameof(sobre));

        byte[] cuerpo = CodificadorSobre.Codificar(sobre);
        BasicProperties propiedades = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            MessageId = sobre.MensajeId,
            CorrelationId = sobre.CorrelacionId,
            Type = sobre.Tipo,
        };

        await _turno.WaitAsync(cancelacion);
        try
        {
            IChannel canal = await CanalAsync(cancelacion);
            await canal.BasicPublishAsync(
                TopologiaConvitec.Exchange, claveRuta, mandatory: true, basicProperties: propiedades, body: cuerpo,
                cancellationToken: cancelacion);
        }
        catch (PublishException e)
        {
            string causa = e.IsReturn ? "ninguna cola recibió el mensaje" : "el broker no confirmó la publicación";
            throw new EnvioFallidoException($"No se entregó {sobre.Tipo} {sobre.MensajeId} a '{claveRuta}': {causa}.", e);
        }
        catch (Exception e) when (e is AlreadyClosedException || e is OperationInterruptedException
                                  || e is BrokerUnreachableException || e is ObjectDisposedException)
        {
            // El canal murió: el próximo envío abre uno nuevo.
            _canal = null;
            throw new EnvioFallidoException($"No se pudo publicar {sobre.Tipo} {sobre.MensajeId} en '{claveRuta}'.", e);
        }
        finally
        {
            _turno.Release();
        }
    }

    private async Task<IChannel> CanalAsync(CancellationToken cancelacion)
    {
        if (_canal != null && _canal.IsOpen)
        {
            return _canal;
        }

        if (_canal != null)
        {
            _canal.Dispose();
        }
        _canal = await _conexion.Conexion.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancelacion);
        return _canal;
    }

    public async ValueTask DisposeAsync()
    {
        if (_canal != null)
        {
            try
            {
                await _canal.CloseAsync();
            }
            catch (Exception e) when (e is AlreadyClosedException || e is OperationInterruptedException)
            {
                // Ya estaba cerrado.
            }
            _canal.Dispose();
            _canal = null;
        }
        _turno.Dispose();
    }
}
