using System;
using System.Threading;
using System.Threading.Tasks;
using ConviTEC.Inventario.Mensajeria;
using ConviTEC.Inventario.Rabbit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ConviTEC.Inventario.Api;

/// Parámetros de la mensajería de una sede: a qué broker conectar, qué sedes declarar y quién despacha.
public sealed class OpcionesMensajeria
{
    public string SedeId { get; }
    public string[] SedesRemotas { get; }
    public string RabbitUri { get; }
    public DespachadorInventario Despachador { get; }

    /// Crea el emisor de la réplica del catálogo sobre el publicador de la conexión. Null si la sede no replica.
    public Func<IPublicadorMensajes, SincronizadorCatalogo>? FabricaSincronizador { get; }

    public OpcionesMensajeria(
        string sedeId, string[] sedesRemotas, string rabbitUri, DespachadorInventario despachador,
        Func<IPublicadorMensajes, SincronizadorCatalogo>? fabricaSincronizador = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(sedesRemotas, nameof(sedesRemotas));
        ArgumentException.ThrowIfNullOrWhiteSpace(rabbitUri, nameof(rabbitUri));
        ArgumentNullException.ThrowIfNull(despachador, nameof(despachador));
        SedeId = sedeId;
        SedesRemotas = sedesRemotas;
        RabbitUri = rabbitUri;
        Despachador = despachador;
        FabricaSincronizador = fabricaSincronizador;
    }
}

/// Mantiene el consumidor de <c>inventario.&lt;sedeId&gt;</c> mientras la sede está en marcha. Si el broker no está
/// disponible al arrancar, reintenta cada pocos segundos y el HTTP sigue sirviendo. Una vez conectado, la recuperación
/// automática del cliente cubre las caídas posteriores.
public sealed class ServicioMensajeria : BackgroundService
{
    private static readonly TimeSpan EsperaConexion = TimeSpan.FromSeconds(5);

    private readonly OpcionesMensajeria _opciones;
    private readonly ILogger<ServicioMensajeria> _log;

    public ServicioMensajeria(OpcionesMensajeria opciones, ILogger<ServicioMensajeria> log)
    {
        ArgumentNullException.ThrowIfNull(opciones, nameof(opciones));
        ArgumentNullException.ThrowIfNull(log, nameof(log));
        _opciones = opciones;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken detencion)
    {
        ConexionRabbit? conexion = null;
        while (conexion == null && !detencion.IsCancellationRequested)
        {
            try
            {
                conexion = await ConexionRabbit.ConectarAsync(_opciones.RabbitUri, detencion);
            }
            catch (EnvioFallidoException e)
            {
                _log.LogWarning("Broker no disponible, se reintenta en {Segundos}s: {Motivo}", EsperaConexion.TotalSeconds, e.Message);
                await EsperarAsync(detencion);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        if (conexion == null)
        {
            return;
        }

        await using (conexion)
        {
            await conexion.DeclararTopologiaAsync(Sedes(), detencion);
            await using PublicadorRabbit publicador = new PublicadorRabbit(conexion);
            await using ConsumidorInventario consumidor = new ConsumidorInventario(
                conexion, _opciones.SedeId, _opciones.Despachador, publicador, mensaje => _log.LogInformation("{Mensaje}", mensaje));
            await consumidor.IniciarAsync(detencion);
            _log.LogInformation("Sede {Sede} consumiendo {Cola}.", _opciones.SedeId, TopologiaConvitec.ColaInventario(_opciones.SedeId));

            try
            {
                if (_opciones.FabricaSincronizador == null)
                {
                    await Task.Delay(Timeout.Infinite, detencion);
                }
                else
                {
                    // Al arrancar pide las instantáneas y vacía lo pendiente; al reconectar vuelve a pedirlas.
                    SincronizadorCatalogo sincronizador = _opciones.FabricaSincronizador(publicador);
                    Func<Task> alReconectar = () =>
                    {
                        _log.LogInformation("Conexión con el broker recuperada: se piden de nuevo las instantáneas.");
                        sincronizador.AvisarReconexion();
                        return Task.CompletedTask;
                    };
                    conexion.Reconectada += alReconectar;
                    try
                    {
                        await sincronizador.EjecutarAsync(detencion);
                    }
                    finally
                    {
                        conexion.Reconectada -= alReconectar;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cierre ordenado de la sede.
            }
        }
    }

    private string[] Sedes()
    {
        string[] todas = new string[_opciones.SedesRemotas.Length + 1];
        todas[0] = _opciones.SedeId;
        Array.Copy(_opciones.SedesRemotas, 0, todas, 1, _opciones.SedesRemotas.Length);
        return todas;
    }

    private static async Task EsperarAsync(CancellationToken detencion)
    {
        try
        {
            await Task.Delay(EsperaConexion, detencion);
        }
        catch (OperationCanceledException)
        {
            // Se está cerrando: el bucle principal termina.
        }
    }
}
