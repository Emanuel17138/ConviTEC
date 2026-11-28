using System;
using System.Threading;
using System.Threading.Tasks;
using ConviTEC.Inventario.Mensajeria;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace ConviTEC.Inventario.Rabbit;

/// Consume <c>inventario.&lt;sedeId&gt;</c>. Un mensaje a la vez (prefetch 1) y acuse manual con este orden:
/// despachar (el efecto queda durable) → publicar las salidas con confirmación → acusar. Si algo falla antes del
/// acuse, el mensaje vuelve a la cola; al reprocesarlo, la idempotencia por mensajeId devuelve el resultado original.
public sealed class ConsumidorInventario : IAsyncDisposable
{
    private static readonly TimeSpan EsperaReintento = TimeSpan.FromMilliseconds(500);

    private readonly ConexionRabbit _conexion;
    private readonly string _sedeId;
    private readonly DespachadorInventario _despachador;
    private readonly IPublicadorMensajes _publicador;
    private readonly Action<string> _registro;
    private IChannel? _canal;
    private CancellationToken _cancelacion;

    public ConsumidorInventario(
        ConexionRabbit conexion, string sedeId, DespachadorInventario despachador, IPublicadorMensajes publicador,
        Action<string>? registro = null)
    {
        ArgumentNullException.ThrowIfNull(conexion, nameof(conexion));
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(despachador, nameof(despachador));
        ArgumentNullException.ThrowIfNull(publicador, nameof(publicador));
        if (!string.Equals(despachador.SedeId, sedeId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"El despachador es de la sede '{despachador.SedeId}', no de '{sedeId}'.", nameof(despachador));
        }

        _conexion = conexion;
        _sedeId = sedeId;
        _despachador = despachador;
        _publicador = publicador;
        _registro = registro ?? (_ => { });
    }

    /// Empieza a consumir. La cola debe estar declarada (<see cref="ConexionRabbit.DeclararTopologiaAsync"/>).
    public async Task IniciarAsync(CancellationToken cancelacion = default)
    {
        if (_canal != null)
        {
            throw new InvalidOperationException("El consumidor ya está iniciado.");
        }

        _cancelacion = cancelacion;
        _canal = await _conexion.Conexion.CreateChannelAsync(cancellationToken: cancelacion);
        await _canal.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: cancelacion);

        AsyncEventingBasicConsumer consumidor = new AsyncEventingBasicConsumer(_canal);
        consumidor.ReceivedAsync += AlRecibirAsync;
        await _canal.BasicConsumeAsync(
            TopologiaConvitec.ColaInventario(_sedeId), autoAck: false, consumer: consumidor, cancellationToken: cancelacion);
    }

    private async Task AlRecibirAsync(object remitente, BasicDeliverEventArgs entrega)
    {
        IChannel canal = ((AsyncEventingBasicConsumer)remitente).Channel;
        try
        {
            ResultadoDespacho resultado = _despachador.Despachar(entrega.Body.ToArray());
            switch (resultado.Accion)
            {
                case AccionConsumo.Descartar:
                    _registro("Mensaje descartado: " + resultado.Motivo);
                    await AcusarAsync(canal, entrega);
                    return;
                case AccionConsumo.Reintentar:
                    _registro("Reintento: " + resultado.Motivo);
                    await DevolverAsync(canal, entrega);
                    return;
            }

            for (int i = 0; i < resultado.Salidas.Length; i++)
            {
                await _publicador.PublicarAsync(resultado.Salidas[i].ClaveRuta, resultado.Salidas[i].Sobre, _cancelacion);
            }
            await AcusarAsync(canal, entrega);
        }
        catch (EnvioFallidoException e)
        {
            _registro("Respuesta sin entregar, el comando se reprocesa: " + e.Message);
            await DevolverAsync(canal, entrega);
        }
        catch (OperationCanceledException)
        {
            // La sede se está deteniendo: el mensaje sigue sin acusar y el broker lo reentrega al volver.
        }
        catch (Exception e)
        {
            _registro("Fallo inesperado, el comando se reprocesa: " + e.Message);
            await DevolverAsync(canal, entrega);
        }
    }

    private async Task AcusarAsync(IChannel canal, BasicDeliverEventArgs entrega)
    {
        try
        {
            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, _cancelacion);
        }
        catch (Exception e) when (e is AlreadyClosedException || e is OperationInterruptedException)
        {
            _registro("Conexión perdida antes del acuse: el broker reentregará el comando.");
        }
    }

    private async Task DevolverAsync(IChannel canal, BasicDeliverEventArgs entrega)
    {
        try
        {
            await Task.Delay(EsperaReintento, _cancelacion);
            await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: true, _cancelacion);
        }
        catch (Exception e) when (e is AlreadyClosedException || e is OperationInterruptedException || e is OperationCanceledException)
        {
            // Sin canal o en cierre: el broker reentrega solo.
        }
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
    }
}
