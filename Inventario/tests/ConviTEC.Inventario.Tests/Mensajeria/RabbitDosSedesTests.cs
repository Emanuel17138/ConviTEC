using System;
using System.Text;
using System.Threading.Tasks;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Mensajeria;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Rabbit;
using ConviTEC.Inventario.Tests.Negocio;
using ConviTEC.Inventario.Tests.Persistencia;
using RabbitMQ.Client;

namespace ConviTEC.Inventario.Tests.Mensajeria;

/// Dos sedes contra un RabbitMQ real. Necesita un broker en <c>amqp://guest:guest@localhost:5672/</c> o en la URI
/// de la variable <c>CONVITEC_RABBITMQ_TEST</c>; sin broker las pruebas fallan con un mensaje claro. Cada prueba usa
/// sedes con prefijo único (<c>T&lt;hex&gt;-LIB</c>, <c>T&lt;hex&gt;-SJ</c>) y borra sus colas al terminar.
///
/// LIB es la proveedora (inventario con CAF 5). SJ hace de coordinadora: solo envía comandos con el
/// <see cref="ClienteInventario"/> y lee <c>pedidos.SJ</c>, como lo hará Pedidos.
public class RabbitDosSedesTests : IAsyncLifetime
{
    private const long Precio = 150000;
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(15);

    private readonly string _lib;
    private readonly string _sj;
    private readonly DirectorioTemporal _dir = new DirectorioTemporal();

    private ConexionRabbit _conexion = null!;
    private PublicadorRabbit _publicadorSj = null!;
    private ClienteInventario _clienteSj = null!;
    private IChannel _lector = null!;

    private InventarioLocal _inventarioLib = null!;
    private ConsumidorInventario? _consumidorLib;
    private PublicadorRabbit? _publicadorLib;

    public RabbitDosSedesTests()
    {
        string prefijo = "T" + Guid.NewGuid().ToString("N").Substring(0, 8);
        _lib = prefijo + "-LIB";
        _sj = prefijo + "-SJ";
    }

    public async Task InitializeAsync()
    {
        string uri = Environment.GetEnvironmentVariable("CONVITEC_RABBITMQ_TEST") ?? ConexionRabbit.UriPorDefecto;
        try
        {
            _conexion = await ConexionRabbit.ConectarAsync(uri);
        }
        catch (EnvioFallidoException e)
        {
            throw new InvalidOperationException(
                $"Estas pruebas necesitan RabbitMQ en {uri} (o la variable CONVITEC_RABBITMQ_TEST). {e.Message}", e);
        }

        await _conexion.DeclararTopologiaAsync(new[] { _lib, _sj });
        _publicadorSj = new PublicadorRabbit(_conexion);
        _clienteSj = new ClienteInventario(_sj, _publicadorSj);
        _lector = await _conexion.Conexion.CreateChannelAsync();

        _inventarioLib = FabricaInventario.CrearCon(
            _dir, _lib, new[] { _sj }, null, new[] { FabricaInventario.Stock("CAF", 5, 0, Precio) });
    }

    public async Task DisposeAsync()
    {
        await DetenerLibAsync();
        try
        {
            await _lector.QueueDeleteAsync(TopologiaConvitec.ColaInventario(_lib));
            await _lector.QueueDeleteAsync(TopologiaConvitec.ColaPedidos(_lib));
            await _lector.QueueDeleteAsync(TopologiaConvitec.ColaInventario(_sj));
            await _lector.QueueDeleteAsync(TopologiaConvitec.ColaPedidos(_sj));
        }
        finally
        {
            await _lector.CloseAsync();
            _lector.Dispose();
            await _publicadorSj.DisposeAsync();
            await _conexion.DisposeAsync();
            _dir.Dispose();
        }
    }

    // ---- Ayudas ----

    private DespachadorInventario CrearDespachador(InventarioLocal inventario)
    {
        DespachadorInventario despachador = new DespachadorInventario(_lib);
        despachador.Registrar(new ManejadorComandosStock(_lib, new ProveedorStock(inventario)));
        return despachador;
    }

    private async Task ArrancarLibAsync()
    {
        _publicadorLib = new PublicadorRabbit(_conexion);
        _consumidorLib = new ConsumidorInventario(_conexion, _lib, CrearDespachador(_inventarioLib), _publicadorLib);
        await _consumidorLib.IniciarAsync();
    }

    private async Task DetenerLibAsync()
    {
        if (_consumidorLib != null)
        {
            await _consumidorLib.DisposeAsync();
            _consumidorLib = null;
        }
        if (_publicadorLib != null)
        {
            await _publicadorLib.DisposeAsync();
            _publicadorLib = null;
        }
    }

    /// Reinicio de la sede LIB: se descarta lo que había en memoria y se reconstruye desde sus archivos.
    private async Task ReiniciarLibAsync()
    {
        await DetenerLibAsync();
        _inventarioLib = FabricaInventario.Recargar(_dir, _lib, _sj);
        await ArrancarLibAsync();
    }

    private async Task EnviarAsync(Sobre comando) => await _clienteSj.EnviarAsync(comando);

    private async Task<Sobre> LeerRespuestaAsync()
    {
        DateTime limite = DateTime.UtcNow + Espera;
        while (DateTime.UtcNow < limite)
        {
            BasicGetResult? entrega = await _lector.BasicGetAsync(TopologiaConvitec.ColaPedidos(_sj), autoAck: false);
            if (entrega != null)
            {
                await _lector.BasicAckAsync(entrega.DeliveryTag, multiple: false);
                Assert.True(CodificadorSobre.TryDecodificar(entrega.Body.ToArray(), out Sobre? sobre, out string motivo), motivo);
                return sobre!;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("No llegó ninguna respuesta a pedidos." + _sj);
    }

    private async Task<ResultadoStock> LeerResultadoAsync()
    {
        Sobre sobre = await LeerRespuestaAsync();
        Assert.True(ClienteInventario.TryLeerResultado(sobre, out ResultadoStock? resultado, out string motivo), motivo);
        return resultado!;
    }

    private Sobre Reservar(string mensajeId, string pedidoId, int cantidad) =>
        _clienteSj.CrearReservar(mensajeId, _lib, pedidoId, new[] { new LineaReserva("CAF", cantidad, Precio) });

    private void AfirmarCantidades(int existencia, int reservada)
    {
        Assert.True(_inventarioLib.BuscarStock("CAF", out StockLocal? stock));
        Assert.Equal(existencia, stock.Existencia);
        Assert.Equal(reservada, stock.Reservada);
        Assert.Equal(existencia - reservada, stock.Disponible);
    }

    // ---- Pruebas ----

    [Fact]
    public async Task SjReservaYConfirmaEnLib_EjemploDelContrato()
    {
        await ArrancarLibAsync();
        string reservar = ClienteInventario.NuevoMensajeId();

        await EnviarAsync(Reservar(reservar, "p-1", 2));
        Sobre respuesta = await LeerRespuestaAsync();

        Assert.Equal("stock.resultado", respuesta.Tipo);
        Assert.Equal(reservar, respuesta.CorrelacionId);
        Assert.Equal(_lib, respuesta.OrigenSedeId);
        Assert.Equal(_sj, respuesta.DestinoSedeId);
        Assert.True(ClienteInventario.TryLeerResultado(respuesta, out ResultadoStock? reservado, out _));
        Assert.Equal(EstadoReserva.Reservada, reservado!.EstadoReserva);
        Assert.Equal(CodigosResultado.Ok, reservado.Codigo);
        AfirmarCantidades(5, 2);

        await EnviarAsync(_clienteSj.CrearConfirmar(ClienteInventario.NuevoMensajeId(), _lib, "p-1"));
        ResultadoStock confirmado = await LeerResultadoAsync();

        Assert.Equal(EstadoReserva.Confirmada, confirmado.EstadoReserva);
        AfirmarCantidades(3, 0);
    }

    [Fact]
    public async Task SjLiberaEnLib_DevuelveLasUnidades()
    {
        await ArrancarLibAsync();
        await EnviarAsync(Reservar(ClienteInventario.NuevoMensajeId(), "p-1", 2));
        await LeerResultadoAsync();

        await EnviarAsync(_clienteSj.CrearLiberar(ClienteInventario.NuevoMensajeId(), _lib, "p-1"));
        ResultadoStock liberado = await LeerResultadoAsync();

        Assert.Equal(EstadoReserva.Liberada, liberado.EstadoReserva);
        AfirmarCantidades(5, 0);
    }

    [Fact]
    public async Task DosReservasSimultaneasDe4Contra5_SoloUnaGana()
    {
        await ArrancarLibAsync();

        await Task.WhenAll(
            EnviarAsync(Reservar(ClienteInventario.NuevoMensajeId(), "p-a", 4)),
            EnviarAsync(Reservar(ClienteInventario.NuevoMensajeId(), "p-b", 4)));
        ResultadoStock primero = await LeerResultadoAsync();
        ResultadoStock segundo = await LeerResultadoAsync();

        int reservadas = (primero.EstadoReserva == EstadoReserva.Reservada ? 1 : 0)
            + (segundo.EstadoReserva == EstadoReserva.Reservada ? 1 : 0);
        Assert.Equal(1, reservadas);
        ResultadoStock perdedor = primero.EstadoReserva == EstadoReserva.Rechazada ? primero : segundo;
        Assert.Equal(CodigosResultado.StockInsuficiente, perdedor.Codigo);
        AfirmarCantidades(5, 4);
        Assert.True(_inventarioLib.BuscarStock("CAF", out StockLocal? stock));
        Assert.Equal(1, stock.Disponible);
    }

    [Fact]
    public async Task LiberacionLlegaAntesDeLaReserva_LaReservaTardiaNoAlteraNada()
    {
        await ArrancarLibAsync();

        await EnviarAsync(_clienteSj.CrearLiberar(ClienteInventario.NuevoMensajeId(), _lib, "p-1"));
        ResultadoStock liberada = await LeerResultadoAsync();
        await EnviarAsync(Reservar(ClienteInventario.NuevoMensajeId(), "p-1", 2));
        ResultadoStock tardia = await LeerResultadoAsync();

        Assert.Equal(EstadoReserva.Liberada, liberada.EstadoReserva);
        Assert.Equal(CodigosResultado.EstadoIncompatible, tardia.Codigo);
        AfirmarCantidades(5, 0);
    }

    [Fact]
    public async Task ReintentoDelMismoSobre_DevuelveElMismoResultadoYNoDuplicaElEfecto()
    {
        await ArrancarLibAsync();
        Sobre reservar = Reservar(ClienteInventario.NuevoMensajeId(), "p-1", 2);

        await EnviarAsync(reservar);
        Sobre primera = await LeerRespuestaAsync();
        await EnviarAsync(reservar);
        Sobre segunda = await LeerRespuestaAsync();

        Assert.Equal(primera.MensajeId, segunda.MensajeId);
        Assert.Equal(primera.CorrelacionId, segunda.CorrelacionId);
        AfirmarCantidades(5, 2);
    }

    [Fact]
    public async Task ReinicioTrasDescontarYAntesDelAcuse_RepetirConfirmarDevuelveExitoYDescuentaUnaVez()
    {
        await ArrancarLibAsync();
        await EnviarAsync(Reservar(ClienteInventario.NuevoMensajeId(), "p-1", 2));
        await LeerResultadoAsync();
        await DetenerLibAsync();

        // LIB recibe confirmar, descuenta y se cae antes de acusar: el mensaje queda sin acuse.
        Sobre confirmar = _clienteSj.CrearConfirmar(ClienteInventario.NuevoMensajeId(), _lib, "p-1");
        await EnviarAsync(confirmar);
        IChannel canalCaido = await _conexion.Conexion.CreateChannelAsync();
        BasicGetResult? entrega = await canalCaido.BasicGetAsync(TopologiaConvitec.ColaInventario(_lib), autoAck: false);
        Assert.NotNull(entrega);
        ResultadoDespacho despacho = CrearDespachador(_inventarioLib).Despachar(entrega.Body.ToArray());
        Assert.Equal(AccionConsumo.Acusar, despacho.Accion);
        AfirmarCantidades(3, 0);
        await canalCaido.CloseAsync();
        canalCaido.Dispose();

        // Reinicio: el estado vuelve de los archivos y el broker reentrega el comando.
        await ReiniciarLibAsync();
        Sobre respuesta = await LeerRespuestaAsync();

        Assert.True(ClienteInventario.TryLeerResultado(respuesta, out ResultadoStock? resultado, out _));
        Assert.Equal(EstadoReserva.Confirmada, resultado!.EstadoReserva);
        Assert.Equal(CodigosResultado.Ok, resultado.Codigo);
        Assert.Equal(IdentificadorDerivado.DeComando(confirmar.MensajeId), respuesta.MensajeId);
        AfirmarCantidades(3, 0);
    }

    [Fact]
    public async Task ComandoEnviadoConLibApagada_EsperaEnSuColaYSeProcesaAlArrancar()
    {
        await EnviarAsync(Reservar(ClienteInventario.NuevoMensajeId(), "p-1", 2));
        AfirmarCantidades(5, 0);

        await ArrancarLibAsync();
        ResultadoStock resultado = await LeerResultadoAsync();

        Assert.Equal(EstadoReserva.Reservada, resultado.EstadoReserva);
        AfirmarCantidades(5, 2);
    }

    [Fact]
    public async Task MensajeIlegibleEnLaCola_SeDescartaYNoBloqueaLosSiguientes()
    {
        await ArrancarLibAsync();
        await using (IChannel canal = await _conexion.Conexion.CreateChannelAsync())
        {
            BasicProperties propiedades = new BasicProperties { DeliveryMode = DeliveryModes.Persistent };
            await canal.BasicPublishAsync(
                TopologiaConvitec.Exchange, TopologiaConvitec.ColaInventario(_lib), mandatory: true, propiedades,
                Encoding.UTF8.GetBytes("esto no es un sobre"));
        }

        await EnviarAsync(Reservar(ClienteInventario.NuevoMensajeId(), "p-1", 2));
        ResultadoStock resultado = await LeerResultadoAsync();

        Assert.Equal(EstadoReserva.Reservada, resultado.EstadoReserva);
        AfirmarCantidades(5, 2);
    }

    [Fact]
    public async Task PublicarEnColaQueNoExiste_LanzaEnvioFallido()
    {
        Sobre sobre = Sobre.ConCuerpoTexto("stock.resultado", "r-1", "m-1", _sj, "T00000000-NOEXISTE", "{}");

        await Assert.ThrowsAsync<EnvioFallidoException>(
            () => _publicadorSj.PublicarAsync("pedidos.T00000000-NOEXISTE", sobre));
    }

    [Fact]
    public async Task ElPublicadorSeRecuperaTrasUnEnvioFallido()
    {
        Sobre perdido = Sobre.ConCuerpoTexto("stock.resultado", "r-1", "m-1", _sj, "T00000000-NOEXISTE", "{}");
        await Assert.ThrowsAsync<EnvioFallidoException>(
            () => _publicadorSj.PublicarAsync("pedidos.T00000000-NOEXISTE", perdido));

        await ArrancarLibAsync();
        await EnviarAsync(Reservar(ClienteInventario.NuevoMensajeId(), "p-1", 2));

        Assert.Equal(EstadoReserva.Reservada, (await LeerResultadoAsync()).EstadoReserva);
    }

    [Fact]
    public async Task ConexionARutaInalcanzable_LanzaEnvioFallido()
    {
        await Assert.ThrowsAsync<EnvioFallidoException>(
            () => ConexionRabbit.ConectarAsync("amqp://guest:guest@127.0.0.1:1/"));
    }

    [Fact]
    public async Task ConexionConUriInvalida_LanzaArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ConexionRabbit.ConectarAsync("esto no es una uri"));
    }
}
