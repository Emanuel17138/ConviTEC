using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Mensajeria;

/// Lado emisor de la réplica del catálogo (RF03). Independiente del transporte: usa el puerto
/// <see cref="IPublicadorMensajes"/>.
/// <list type="number">
/// <item><b>Cambios.</b> Publica cada <see cref="SalidaPendiente"/> como <c>catalogo.actualizado</c> en la cola de
/// inventario de su destino, con el <c>mensajeId</c> guardado junto al cambio, y solo cuando el broker confirmó y la
/// cola recibió el mensaje la retira (<see cref="InventarioLocal.RetirarSalidas"/>). Si algo falla, queda pendiente y se
/// reintenta con el mismo ID; el transporte puede duplicar y la réplica ignora lo que no supera su revisión.</item>
/// <item><b>Solicitudes.</b> Al iniciar y al reconectar pide <c>catalogo.solicitar</c> a cada otra sede para recibir su
/// instantánea. La solicitud no se guarda en disco: cada arranque la repite y, mientras el broker no la confirme, se
/// reintenta desde memoria.</item>
/// </list>
public sealed class SincronizadorCatalogo
{
    private static readonly TimeSpan EsperaPorDefecto = TimeSpan.FromSeconds(5);

    private readonly string _sedeId;
    private readonly string[] _sedesRemotas;
    private readonly InventarioLocal _inventario;
    private readonly IPublicadorMensajes _publicador;
    private readonly Action<string> _registro;
    private readonly TimeSpan _espera;
    private readonly object _cerrojo = new object();
    private readonly SemaphoreSlim _senal = new SemaphoreSlim(0, 1);

    /// sedeId → mensajeId de su solicitud aún sin confirmar. El reintento reutiliza el mismo ID.
    private readonly ArbolAvl<string, string> _solicitudesPendientes = new ArbolAvl<string, string>(StringComparer.Ordinal);

    public SincronizadorCatalogo(
        string sedeId, string[] sedesRemotas, InventarioLocal inventario, IPublicadorMensajes publicador,
        Action<string>? registro = null, TimeSpan? espera = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(inventario, nameof(inventario));
        ArgumentNullException.ThrowIfNull(publicador, nameof(publicador));
        if (!string.Equals(inventario.SedeId, sedeId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"El inventario es de la sede '{inventario.SedeId}', no de '{sedeId}'.", nameof(inventario));
        }
        if (espera != null && espera.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(espera), espera, "La espera de reintento debe ser positiva.");
        }

        _sedeId = sedeId;
        _sedesRemotas = SedesRemotas.Validar(sedeId, sedesRemotas);
        _inventario = inventario;
        _publicador = publicador;
        _registro = registro ?? (_ => { });
        _espera = espera ?? EsperaPorDefecto;
        MarcarTodasPorSolicitar();
    }

    /// Sedes a las que aún no se les confirmó la solicitud de instantánea, en orden ordinal.
    public string[] SolicitudesPendientes()
    {
        lock (_cerrojo)
        {
            return _solicitudesPendientes.ClavesEnOrden();
        }
    }

    /// Despierta el bucle: hay salidas nuevas o solicitudes por hacer.
    public void Avisar()
    {
        lock (_cerrojo)
        {
            if (_senal.CurrentCount == 0)
            {
                _senal.Release();
            }
        }
    }

    /// La conexión con el broker se recuperó: vuelve a pedir la instantánea de todas las sedes (pudo perderse
    /// información mientras estuvo caída) y reenvía lo pendiente.
    public void AvisarReconexion()
    {
        MarcarTodasPorSolicitar();
        Avisar();
    }

    /// Pide <c>catalogo.solicitar</c> a cada sede pendiente. Devuelve las sedes cuya solicitud sigue sin confirmar
    /// (el broker no la confirmó o no hay cola), que se reintentan después con el mismo mensajeId.
    public async Task<string[]> SolicitarInstantaneasAsync(CancellationToken cancelacion = default)
    {
        string[] sedes;
        string[] ids;
        lock (_cerrojo)
        {
            sedes = _solicitudesPendientes.ClavesEnOrden();
            ids = _solicitudesPendientes.ValoresEnOrden();
        }

        for (int i = 0; i < sedes.Length; i++)
        {
            cancelacion.ThrowIfCancellationRequested();
            Sobre solicitud = Sobre.ConCuerpoTexto(
                TiposMensajeCatalogo.Solicitar, ids[i], ids[i], _sedeId, sedes[i], "{}");
            try
            {
                await _publicador.PublicarAsync(TopologiaConvitec.ColaInventario(sedes[i]), solicitud, cancelacion);
            }
            catch (EnvioFallidoException e)
            {
                _registro($"Solicitud de catálogo a {sedes[i]} sin entregar, se reintenta: {e.Message}");
                continue;
            }

            lock (_cerrojo)
            {
                // Si una reconexión creó otra solicitud mientras publicábamos, esa sigue pendiente.
                if (_solicitudesPendientes.Buscar(sedes[i], out string? vigente)
                    && string.Equals(vigente, ids[i], StringComparison.Ordinal))
                {
                    _solicitudesPendientes.Eliminar(sedes[i]);
                }
            }
        }
        return SolicitudesPendientes();
    }

    /// Publica las salidas pendientes y retira las que el broker confirmó, todas con un solo guardado. Un destino que
    /// falla no frena a los demás. Devuelve cuántas salidas quedaron entregadas y retiradas.
    public async Task<int> EnviarPendientesAsync(CancellationToken cancelacion = default)
    {
        SalidaPendiente[] pendientes = _inventario.ConsultarSalidasPendientes();
        SalidaPendiente[] entregadas = new SalidaPendiente[pendientes.Length];
        int cantidad = 0;

        for (int i = 0; i < pendientes.Length; i++)
        {
            cancelacion.ThrowIfCancellationRequested();
            if (!_inventario.BuscarStock(pendientes[i].ProductoId, out StockLocal? stock))
            {
                _registro($"La salida {pendientes[i].MensajeId} es de '{pendientes[i].ProductoId}', que ya no existe: se omite.");
                continue;
            }

            EntradaCatalogo entrada = new EntradaCatalogo(stock.Producto, pendientes[i].Disponible, pendientes[i].Revision);
            Sobre cambio = Sobre.ConCuerpoTexto(
                TiposMensajeCatalogo.Actualizado, pendientes[i].MensajeId, pendientes[i].MensajeId,
                _sedeId, pendientes[i].DestinoSedeId, CuerpoCatalogo.EscribirCambio(entrada));
            try
            {
                await _publicador.PublicarAsync(
                    TopologiaConvitec.ColaInventario(pendientes[i].DestinoSedeId), cambio, cancelacion);
            }
            catch (EnvioFallidoException e)
            {
                _registro($"Cambio de {pendientes[i].ProductoId} para {pendientes[i].DestinoSedeId} sin entregar, se reintenta: {e.Message}");
                continue;
            }
            entregadas[cantidad++] = pendientes[i];
        }

        if (cantidad == 0)
        {
            return 0;
        }

        SalidaPendiente[] aRetirar = new SalidaPendiente[cantidad];
        Array.Copy(entregadas, aRetirar, cantidad);
        _inventario.RetirarSalidas(aRetirar);
        return cantidad;
    }

    /// Bucle de la sede: solicita instantáneas y vacía las salidas. Espera la señal de <see cref="Avisar"/> y, mientras
    /// quede algo sin confirmar, reintenta cada pocos segundos. Termina al cancelar.
    public async Task EjecutarAsync(CancellationToken cancelacion)
    {
        _inventario.SalidasRegistradas += Avisar;
        try
        {
            while (!cancelacion.IsCancellationRequested)
            {
                bool quedaTrabajo = await IntentarAsync(cancelacion);
                try
                {
                    await _senal.WaitAsync(quedaTrabajo ? _espera : Timeout.InfiniteTimeSpan, cancelacion);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        finally
        {
            _inventario.SalidasRegistradas -= Avisar;
        }
    }

    /// Una pasada de trabajo. Devuelve true si algo quedó sin confirmar y hay que reintentar.
    private async Task<bool> IntentarAsync(CancellationToken cancelacion)
    {
        try
        {
            string[] sinConfirmar = await SolicitarInstantaneasAsync(cancelacion);
            await EnviarPendientesAsync(cancelacion);
            return sinConfirmar.Length > 0 || _inventario.ConsultarSalidasPendientes().Length > 0;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception e) when (e is DatosInventarioInvalidosException || e is IOException || e is UnauthorizedAccessException)
        {
            // El retiro no se pudo guardar: las salidas siguen pendientes y se reenvían, lo que es inofensivo.
            _registro("No se pudo guardar el retiro de publicaciones, se reintenta: " + e.Message);
            return true;
        }
        catch (Exception e)
        {
            // Un fallo inesperado no debe detener la difusión de la sede: se registra y se reintenta.
            _registro("Fallo inesperado al sincronizar el catálogo, se reintenta: " + e.Message);
            return true;
        }
    }

    private void MarcarTodasPorSolicitar()
    {
        lock (_cerrojo)
        {
            for (int i = 0; i < _sedesRemotas.Length; i++)
            {
                if (!_solicitudesPendientes.Contiene(_sedesRemotas[i]))
                {
                    _solicitudesPendientes.Insertar(_sedesRemotas[i], Guid.NewGuid().ToString());
                }
            }
        }
    }
}
