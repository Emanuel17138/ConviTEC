using System;
using System.Threading;
using System.Threading.Tasks;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Mensajeria;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Rabbit;
using ConviTEC.Inventario.Tests.Negocio;
using ConviTEC.Inventario.Tests.Persistencia;
using RabbitMQ.Client;

namespace ConviTEC.Inventario.Tests.Mensajeria;

/// Réplica del catálogo entre sedes con un RabbitMQ real. Necesita un broker en
/// <c>amqp://guest:guest@localhost:5672/</c> o en la URI de <c>CONVITEC_RABBITMQ_TEST</c>. Cada prueba usa sedes con
/// prefijo único (<c>T&lt;hex&gt;-LIB</c>, ...), cada una con su propio directorio de datos, y borra sus colas al terminar.
/// Cada sede viva ejecuta lo mismo que <c>Program.cs</c>: consumidor, manejadores de stock y catálogo, y sincronizador.
public class RabbitCatalogoTests : IAsyncLifetime
{
    private const long Precio = 150000;
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Reintento = TimeSpan.FromMilliseconds(200);

    private readonly string _prefijo = "T" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private ConexionRabbit _conexion = null!;
    private PublicadorRabbit _publicadorPrueba = null!;
    private IChannel _lector = null!;
    private SedeViva[] _sedes = Array.Empty<SedeViva>();
    private string[] _todasLasColas = Array.Empty<string>();

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
        _publicadorPrueba = new PublicadorRabbit(_conexion);
        _lector = await _conexion.Conexion.CreateChannelAsync();
    }

    public async Task DisposeAsync()
    {
        for (int i = 0; i < _sedes.Length; i++)
        {
            await _sedes[i].DisposeAsync();
        }
        try
        {
            for (int i = 0; i < _todasLasColas.Length; i++)
            {
                await _lector.QueueDeleteAsync(_todasLasColas[i]);
            }
        }
        finally
        {
            await _lector.CloseAsync();
            _lector.Dispose();
            await _publicadorPrueba.DisposeAsync();
            await _conexion.DisposeAsync();
        }
    }

    // ---- Armado de sedes ----

    private string Id(string corto) => _prefijo + "-" + corto;

    private string[] Ids(params string[] cortos)
    {
        string[] ids = new string[cortos.Length];
        for (int i = 0; i < cortos.Length; i++)
        {
            ids[i] = Id(cortos[i]);
        }
        return ids;
    }

    /// Declara las colas de las sedes indicadas y las anota para borrarlas al terminar.
    private async Task DeclararAsync(params string[] sedes)
    {
        await _conexion.DeclararTopologiaAsync(sedes);
        string[] nuevas = new string[_todasLasColas.Length + sedes.Length * 2];
        Array.Copy(_todasLasColas, nuevas, _todasLasColas.Length);
        for (int i = 0; i < sedes.Length; i++)
        {
            nuevas[_todasLasColas.Length + i * 2] = TopologiaConvitec.ColaInventario(sedes[i]);
            nuevas[_todasLasColas.Length + i * 2 + 1] = TopologiaConvitec.ColaPedidos(sedes[i]);
        }
        _todasLasColas = nuevas;
    }

    private SedeViva Crear(string id, string[] remotas, params StockLocal[] stocks)
    {
        SedeViva sede = new SedeViva(_conexion, id, remotas, stocks);
        SedeViva[] nuevas = new SedeViva[_sedes.Length + 1];
        Array.Copy(_sedes, nuevas, _sedes.Length);
        nuevas[_sedes.Length] = sede;
        _sedes = nuevas;
        return sede;
    }

    private static StockLocal Stock(string id, int existencia, int reservada) =>
        FabricaInventario.Stock(id, existencia, reservada, Precio);

    private static async Task EsperarAsync(Func<bool> condicion, string descripcion)
    {
        DateTime limite = DateTime.UtcNow + Espera;
        while (!condicion())
        {
            Assert.True(DateTime.UtcNow < limite, "No se cumplió a tiempo: " + descripcion);
            await Task.Delay(25);
        }
    }

    private static bool OfertaEs(SedeViva sede, string de, string productoId, int disponible, long revision) =>
        sede.Catalogo.BuscarOferta(de, productoId, out OfertaReplicada? oferta)
        && oferta.Disponible == disponible && oferta.Revision == revision;

    private static Oferta? OfertaDeLaVista(ProductoGlobal[] vista, string productoId, string sedeId)
    {
        for (int i = 0; i < vista.Length; i++)
        {
            if (vista[i].Producto.ProductoId != productoId)
            {
                continue;
            }
            for (int j = 0; j < vista[i].Ofertas.Length; j++)
            {
                if (vista[i].Ofertas[j].SedeId == sedeId)
                {
                    return vista[i].Ofertas[j];
                }
            }
        }
        return null;
    }

    private Sobre MensajeDe(string tipo, string origen, string destino, string cuerpo) =>
        Sobre.ConCuerpoTexto(tipo, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), origen, destino, cuerpo);

    private static string EntradaJson(string productoId, int disponible, long revision) =>
        $"{{\"producto\":{{\"productoId\":\"{productoId}\",\"nombre\":\"Nombre {productoId}\",\"descripcion\":\"d\",\"categoria\":\"c\",\"precioUnitarioCentimos\":{Precio}}},\"disponible\":{disponible},\"revision\":{revision}}}";

    /// Una sede en marcha: sus datos propios y las piezas que arma <c>Program.cs</c>.
    private sealed class SedeViva : IAsyncDisposable
    {
        private readonly ConexionRabbit _conexion;
        private readonly string[] _remotas;
        private PublicadorRabbit? _publicador;
        private ConsumidorInventario? _consumidor;
        private SincronizadorCatalogo? _sincronizador;
        private CancellationTokenSource? _corte;
        private Task? _bucle;

        public string Id { get; }
        public DirectorioTemporal Dir { get; } = new DirectorioTemporal();
        public InventarioLocal Inventario { get; private set; }
        public CatalogoReplicado Catalogo { get; private set; }
        public ConsultaCatalogo Consulta { get; private set; }
        public SincronizadorCatalogo? Sincronizador => _sincronizador;

        public SedeViva(ConexionRabbit conexion, string id, string[] remotas, StockLocal[] stocks)
        {
            _conexion = conexion;
            Id = id;
            _remotas = remotas;
            Inventario = FabricaInventario.CrearCon(Dir, id, remotas, null, stocks);
            Catalogo = FabricaCatalogo.Crear(Dir, id, remotas);
            Consulta = new ConsultaCatalogo(Inventario, Catalogo);
        }

        public async Task ArrancarAsync()
        {
            DespachadorInventario despachador = new DespachadorInventario(Id);
            despachador.Registrar(new ManejadorComandosStock(Id, new ProveedorStock(Inventario)));
            despachador.Registrar(new ManejadorMensajesCatalogo(Id, Inventario, Catalogo));

            _publicador = new PublicadorRabbit(_conexion);
            _consumidor = new ConsumidorInventario(_conexion, Id, despachador, _publicador);
            await _consumidor.IniciarAsync();

            _sincronizador = new SincronizadorCatalogo(Id, _remotas, Inventario, _publicador, null, Reintento);
            _corte = new CancellationTokenSource();
            CancellationToken token = _corte.Token;
            SincronizadorCatalogo sincronizador = _sincronizador;
            _bucle = Task.Run(() => sincronizador.EjecutarAsync(token));
        }

        public async Task DetenerAsync()
        {
            if (_corte != null)
            {
                _corte.Cancel();
                await _bucle!;
                _corte.Dispose();
                _corte = null;
                _bucle = null;
            }
            if (_consumidor != null)
            {
                await _consumidor.DisposeAsync();
                _consumidor = null;
            }
            if (_publicador != null)
            {
                await _publicador.DisposeAsync();
                _publicador = null;
            }
            _sincronizador = null;
        }

        /// Reinicio del proceso: se descarta la memoria y se reconstruye desde los archivos de la sede.
        public void RecargarDeDisco()
        {
            Inventario = FabricaInventario.Recargar(Dir, Id, _remotas);
            Catalogo = FabricaCatalogo.Recargar(Dir, Id, _remotas);
            Consulta = new ConsultaCatalogo(Inventario, Catalogo);
        }

        public async ValueTask DisposeAsync()
        {
            await DetenerAsync();
            Dir.Dispose();
        }
    }

    // ---- Pruebas ----

    [Fact]
    public async Task SjConTresRemotas_PasaDeInicializandoAConsultableSoloConLaTerceraInstantanea()
    {
        string[] remotas = Ids("A", "B", "C");
        string sj = Id("SJ");
        await DeclararAsync(sj, remotas[0], remotas[1], remotas[2]);
        SedeViva sede = Crear(sj, remotas, Stock("CAF", 5, 0));
        await sede.ArrancarAsync();
        string cola = TopologiaConvitec.ColaInventario(sj);

        await _publicadorPrueba.PublicarAsync(cola, MensajeDe("catalogo.instantanea", remotas[0], sj,
            $"{{\"entradas\":[{EntradaJson("CAF", 3, 2)}]}}"));
        await _publicadorPrueba.PublicarAsync(cola, MensajeDe("catalogo.instantanea", remotas[1], sj, "{\"entradas\":[]}"));
        await _publicadorPrueba.PublicarAsync(cola, MensajeDe("catalogo.actualizado", remotas[2], sj, EntradaJson("CAF", 1, 1)));
        await EsperarAsync(() => OfertaEs(sede, remotas[2], "CAF", 1, 1) && sede.Catalogo.EstaSincronizada(remotas[1]),
            "llegaron dos instantáneas y el cambio de la tercera");

        Assert.False(sede.Consulta.EstaInicializado);
        CatalogoInicializandoException pendiente = Assert.Throws<CatalogoInicializandoException>(() => sede.Consulta.ConsultarCatalogo());
        Assert.Equal(new[] { remotas[2] }, pendiente.SedesPendientes);

        await _publicadorPrueba.PublicarAsync(cola, MensajeDe("catalogo.instantanea", remotas[2], sj, "{\"entradas\":[]}"));
        await EsperarAsync(() => sede.Consulta.EstaInicializado, "llegó la instantánea vacía de la tercera");

        ProductoGlobal[] vista = sede.Consulta.ConsultarCatalogo();
        Assert.Equal(3, OfertaDeLaVista(vista, "CAF", remotas[0])!.Disponible);
        Assert.Equal(1, OfertaDeLaVista(vista, "CAF", remotas[2])!.Disponible);
        Assert.Null(OfertaDeLaVista(vista, "CAF", remotas[1])); // sin datos: se omite
        Assert.Equal(5, OfertaDeLaVista(vista, "CAF", sj)!.Disponible);
    }

    [Fact]
    public async Task CuatroSedes_UnaReposicionEnLibLlegaATodasYLaReservaYLaConfirmacionTambien()
    {
        string lib = Id("LIB"), sj = Id("SJ"), ala = Id("ALA"), lim = Id("LIM");
        await DeclararAsync(lib, sj, ala, lim);
        SedeViva sedeLib = Crear(lib, new[] { sj, ala, lim }, Stock("CAF", 5, 0));
        SedeViva sedeSj = Crear(sj, new[] { lib, ala, lim }, Stock("CAF", 2, 0));
        SedeViva sedeAla = Crear(ala, new[] { lib, sj, lim }, Stock("CAF", 1, 0));
        SedeViva sedeLim = Crear(lim, new[] { lib, sj, ala }, Stock("CAF", 0, 0));
        SedeViva[] copias = { sedeSj, sedeAla, sedeLim };

        await sedeLib.ArrancarAsync();
        await sedeSj.ArrancarAsync();
        await sedeAla.ArrancarAsync();
        await sedeLim.ArrancarAsync();

        // Arranque: ninguna venta ni cambio, solo las instantáneas.
        await EsperarAsync(
            () => sedeLib.Consulta.EstaInicializado && sedeSj.Consulta.EstaInicializado
                  && sedeAla.Consulta.EstaInicializado && sedeLim.Consulta.EstaInicializado,
            "las cuatro sedes recibieron la instantánea de las otras tres");
        for (int i = 0; i < copias.Length; i++)
        {
            Assert.True(OfertaEs(copias[i], lib, "CAF", 5, 0));
        }
        Assert.True(OfertaEs(sedeLib, lim, "CAF", 0, 0)); // el cero se informa

        // Reposición en LIB.
        ResultadoReposicion reposicion = new ServicioReposiciones(sedeLib.Inventario).Reponer("CAF", Guid.NewGuid().ToString(), 10);
        Assert.True(reposicion.EsExito);
        await EsperarAsync(() => OfertaEs(sedeSj, lib, "CAF", 15, 1) && OfertaEs(sedeAla, lib, "CAF", 15, 1)
                                 && OfertaEs(sedeLim, lib, "CAF", 15, 1), "las tres copias muestran la reposición de LIB");

        // SJ reserva 4 en LIB.
        ClienteInventario cliente = new ClienteInventario(sj, _publicadorPrueba);
        string reservar = ClienteInventario.NuevoMensajeId();
        await cliente.EnviarAsync(cliente.CrearReservar(reservar, lib, "p-1", new[] { new LineaReserva("CAF", 4, Precio) }));
        await EsperarAsync(() => OfertaEs(sedeSj, lib, "CAF", 11, 2) && OfertaEs(sedeAla, lib, "CAF", 11, 2)
                                 && OfertaEs(sedeLim, lib, "CAF", 11, 2), "la reserva reduce el disponible de LIB en las copias");

        // SJ confirma: el disponible se conserva con una revisión superior.
        await cliente.EnviarAsync(cliente.CrearConfirmar(ClienteInventario.NuevoMensajeId(), lib, "p-1"));
        await EsperarAsync(() => OfertaEs(sedeSj, lib, "CAF", 11, 3) && OfertaEs(sedeAla, lib, "CAF", 11, 3)
                                 && OfertaEs(sedeLim, lib, "CAF", 11, 3), "la confirmación conserva el disponible con revisión 3");

        // Solo LIB modifica las cantidades propietarias.
        Assert.True(sedeLib.Inventario.BuscarStock("CAF", out StockLocal? stockLib));
        Assert.Equal(11, stockLib.Existencia);
        Assert.Equal(0, stockLib.Reservada);
        Assert.True(sedeSj.Inventario.BuscarStock("CAF", out StockLocal? stockSj));
        Assert.Equal(2, stockSj.Existencia);
        await EsperarAsync(() => sedeLib.Inventario.ConsultarSalidasPendientes().Length == 0,
            "LIB retiró todo lo que el broker confirmó");
    }

    [Fact]
    public async Task SedeApagadaMientrasSeRepone_AlVolverConvergeYLaCopiaSobreviveAlReinicio()
    {
        string lib = Id("LIB"), sj = Id("SJ"), ala = Id("ALA");
        await DeclararAsync(lib, sj, ala);
        SedeViva sedeLib = Crear(lib, new[] { sj, ala }, Stock("CAF", 5, 0));
        SedeViva sedeSj = Crear(sj, new[] { lib, ala }, Stock("CAF", 2, 0));
        SedeViva sedeAla = Crear(ala, new[] { lib, sj }, Stock("CAF", 1, 0));
        await sedeLib.ArrancarAsync();
        await sedeAla.ArrancarAsync();

        // SJ está desconectada: dos reposiciones esperan en su cola.
        ServicioReposiciones servicio = new ServicioReposiciones(sedeLib.Inventario);
        servicio.Reponer("CAF", Guid.NewGuid().ToString(), 3);
        servicio.Reponer("CAF", Guid.NewGuid().ToString(), 4);
        await EsperarAsync(() => sedeLib.Inventario.ConsultarSalidasPendientes().Length == 0, "LIB entregó sus cambios al broker");
        await EsperarAsync(() => OfertaEs(sedeAla, lib, "CAF", 12, 2), "ALA ve la última oferta de LIB");

        // La publicadora se reinicia y la sede desconectada vuelve.
        await sedeLib.DetenerAsync();
        sedeLib.RecargarDeDisco();
        await sedeLib.ArrancarAsync();
        await sedeSj.ArrancarAsync();

        await EsperarAsync(() => sedeSj.Consulta.EstaInicializado && OfertaEs(sedeSj, lib, "CAF", 12, 2),
            "SJ converge con la oferta de LIB (revisión 2)");
        Assert.True(OfertaEs(sedeSj, ala, "CAF", 1, 0));

        // Sin movimientos nuevos, un reinicio de SJ conserva la copia ya inicializada, incluso con las demás apagadas.
        await sedeSj.DetenerAsync();
        await sedeLib.DetenerAsync();
        await sedeAla.DetenerAsync();
        sedeSj.RecargarDeDisco();

        Assert.True(sedeSj.Consulta.EstaInicializado);
        ProductoGlobal[] vista = sedeSj.Consulta.ConsultarCatalogo();
        Assert.Equal(12, OfertaDeLaVista(vista, "CAF", lib)!.Disponible);
        Assert.Equal(2L, OfertaDeLaVista(vista, "CAF", lib)!.Revision);
    }

    [Fact]
    public async Task SedeNuevaSinVentas_RecibeLaInstantaneaConLosCerosYQuedaInicializada()
    {
        string lib = Id("LIB"), sj = Id("SJ");
        await DeclararAsync(lib, sj);
        SedeViva sedeLib = Crear(lib, new[] { sj }, Stock("ARR", 3, 3), Stock("CAF", 5, 0));
        SedeViva sedeSj = Crear(sj, new[] { lib }, Stock("CAF", 1, 0));
        await sedeLib.ArrancarAsync();
        Assert.Empty(sedeLib.Inventario.ConsultarSalidasPendientes()); // LIB no tiene nada que publicar

        await sedeSj.ArrancarAsync();
        await EsperarAsync(() => sedeSj.Consulta.EstaInicializado, "SJ recibió la instantánea de LIB");

        Assert.True(OfertaEs(sedeSj, lib, "CAF", 5, 0));
        Assert.True(OfertaEs(sedeSj, lib, "ARR", 0, 0)); // agotado en LIB, aun así se informa
    }

    [Fact]
    public async Task DestinoSinCola_LaSalidaSeConservaSobreviveAlReinicioYSeEntregaCuandoApareceLaCola()
    {
        string lib = Id("LIB"), sj = Id("SJ"), fantasma = Id("FANTASMA");
        await DeclararAsync(lib, sj); // la cola del fantasma aún no existe: el broker devuelve el mensaje
        SedeViva sedeLib = Crear(lib, new[] { sj, fantasma }, Stock("CAF", 5, 0));
        SedeViva sedeSj = Crear(sj, new[] { lib }, Stock("CAF", 1, 0));
        await sedeLib.ArrancarAsync();
        await sedeSj.ArrancarAsync();

        new ServicioReposiciones(sedeLib.Inventario).Reponer("CAF", Guid.NewGuid().ToString(), 5);
        await EsperarAsync(() => OfertaEs(sedeSj, lib, "CAF", 10, 1), "SJ recibe el cambio aunque el fantasma no tenga cola");

        // La entrega a SJ y el retiro de su salida son dos pasos: se espera a que quede solo la del fantasma.
        await EsperarAsync(() => sedeLib.Inventario.ConsultarSalidasPendientes().Length == 1, "LIB retiró la salida de SJ");
        SalidaPendiente pendiente = Assert.Single(sedeLib.Inventario.ConsultarSalidasPendientes());
        Assert.Equal(fantasma, pendiente.DestinoSedeId);

        // Reinicio de LIB: la intención sigue guardada.
        await sedeLib.DetenerAsync();
        sedeLib.RecargarDeDisco();
        Assert.Equal(pendiente.MensajeId, Assert.Single(sedeLib.Inventario.ConsultarSalidasPendientes()).MensajeId);
        await sedeLib.ArrancarAsync();
        await Task.Delay(500);
        Assert.Single(sedeLib.Inventario.ConsultarSalidasPendientes());

        // Aparece la cola del fantasma: el siguiente reintento la entrega con el mismo mensajeId y la retira.
        await DeclararAsync(fantasma);
        await EsperarAsync(() => sedeLib.Inventario.ConsultarSalidasPendientes().Length == 0, "se entregó la salida pendiente");

        bool vistoElCambio = false;
        for (int i = 0; i < 2 && !vistoElCambio; i++)
        {
            BasicGetResult? entrega = await _lector.BasicGetAsync(TopologiaConvitec.ColaInventario(fantasma), autoAck: true);
            Assert.NotNull(entrega);
            Assert.True(CodificadorSobre.TryDecodificar(entrega.Body.ToArray(), out Sobre? sobre, out string motivo), motivo);
            if (sobre!.Tipo == "catalogo.actualizado")
            {
                vistoElCambio = true;
                Assert.Equal(pendiente.MensajeId, sobre.MensajeId);
                Assert.Equal(lib, sobre.OrigenSedeId);
            }
        }
        Assert.True(vistoElCambio, "la cola del fantasma debía contener el cambio pendiente");
    }

    [Fact]
    public async Task MensajeDeCatalogoDeUnaSedeAjena_SeDescartaYNoBloqueaLaCola()
    {
        string lib = Id("LIB"), sj = Id("SJ"), intrusa = Id("INTRUSA");
        await DeclararAsync(lib, sj);
        SedeViva sedeSj = Crear(sj, new[] { lib }, Stock("CAF", 1, 0));
        await sedeSj.ArrancarAsync();
        string cola = TopologiaConvitec.ColaInventario(sj);

        await _publicadorPrueba.PublicarAsync(cola, MensajeDe("catalogo.actualizado", intrusa, sj, EntradaJson("CAF", 9, 9)));
        await _publicadorPrueba.PublicarAsync(cola, MensajeDe("catalogo.actualizado", lib, sj, "{\"cuerpo\":\"roto\"}"));
        await _publicadorPrueba.PublicarAsync(cola, MensajeDe("catalogo.actualizado", lib, sj, EntradaJson("CAF", 4, 1)));

        await EsperarAsync(() => OfertaEs(sedeSj, lib, "CAF", 4, 1), "el mensaje válido llegó detrás de los descartados");
        Assert.False(sedeSj.Catalogo.BuscarOferta(intrusa, "CAF", out _));
    }
}
