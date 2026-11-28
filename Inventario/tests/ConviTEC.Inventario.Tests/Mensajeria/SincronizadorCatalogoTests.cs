using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Mensajeria;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Negocio;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Mensajeria;

/// Emisor de la réplica del catálogo con un publicador en memoria: envío de cambios, retiro tras la confirmación,
/// solicitud de instantáneas y reintentos. La sede bajo prueba es LIB, con ALA y SJ como remotas.
public class SincronizadorCatalogoTests
{
    private static readonly string[] Remotas = { "ALA", "SJ" };
    private static readonly TimeSpan EsperaCorta = TimeSpan.FromMilliseconds(50);

    private static InventarioLocal CrearInventario(DirectorioTemporal dir, RepositorioInventario? repositorio = null) =>
        FabricaInventario.CrearCon(
            dir, "LIB", Remotas, repositorio,
            new[] { FabricaInventario.Stock("ARR", 4, 4), FabricaInventario.Stock("CAF", 5, 0) });

    private static SincronizadorCatalogo CrearSincronizador(
        InventarioLocal inventario, IPublicadorMensajes publicador, string[]? remotas = null) =>
        new SincronizadorCatalogo("LIB", remotas ?? Remotas, inventario, publicador, null, EsperaCorta);

    private static EntradaCatalogo LeerCambio(Sobre sobre)
    {
        Assert.Equal("catalogo.actualizado", sobre.Tipo);
        Assert.True(CuerpoCatalogo.TryLeerCambio(sobre.Cuerpo, out EntradaCatalogo? entrada, out string motivo), motivo);
        return entrada!;
    }

    // ---- Cambios ----

    [Fact]
    public async Task EnviarPendientes_PublicaCadaCambioEnLaColaDeSuDestinoYLosRetira()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = CrearInventario(dir);
        PublicadorFalso publicador = new PublicadorFalso();
        SincronizadorCatalogo sincronizador = CrearSincronizador(inventario, publicador);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 8, 3)); // CAF revisión 1, disponible 5
        SalidaPendiente[] antes = inventario.ConsultarSalidasPendientes();

        int enviadas = await sincronizador.EnviarPendientesAsync();

        Assert.Equal(2, enviadas);
        Assert.Equal(2, publicador.Enviados.Length);
        Assert.Equal("inventario.ALA", publicador.Enviados[0].ClaveRuta);
        Assert.Equal("inventario.SJ", publicador.Enviados[1].ClaveRuta);
        for (int i = 0; i < publicador.Enviados.Length; i++)
        {
            Sobre sobre = publicador.Enviados[i].Sobre;
            Assert.Equal("LIB", sobre.OrigenSedeId);
            Assert.Equal(antes[i].MensajeId, sobre.MensajeId); // el ID guardado junto al cambio
            Assert.Equal(antes[i].DestinoSedeId, sobre.DestinoSedeId);
            EntradaCatalogo entrada = LeerCambio(sobre);
            Assert.Equal("CAF", entrada.Producto.ProductoId);
            Assert.Equal(5, entrada.Disponible);
            Assert.Equal(1L, entrada.Revision);
        }
        Assert.Empty(inventario.ConsultarSalidasPendientes());
        Assert.Empty(FabricaInventario.Recargar(dir, "LIB", Remotas).ConsultarSalidasPendientes());
    }

    [Fact]
    public async Task EnviarPendientes_SinCambios_NoPublicaNiEscribe()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = CrearInventario(dir);
        PublicadorFalso publicador = new PublicadorFalso();
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);

        int enviadas = await CrearSincronizador(inventario, publicador).EnviarPendientesAsync();

        Assert.Equal(0, enviadas);
        Assert.Empty(publicador.Enviados);
        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
    }

    [Fact]
    public async Task EnviarPendientes_ElBrokerNoConfirma_NoRetiraNadaYReintentaConElMismoId()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = CrearInventario(dir);
        PublicadorFalso publicador = new PublicadorFalso { Falla = true };
        SincronizadorCatalogo sincronizador = CrearSincronizador(inventario, publicador);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        string[] idsAntes = { inventario.ConsultarSalidasPendientes()[0].MensajeId, inventario.ConsultarSalidasPendientes()[1].MensajeId };

        Assert.Equal(0, await sincronizador.EnviarPendientesAsync());
        Assert.Equal(2, inventario.ConsultarSalidasPendientes().Length);

        publicador.Falla = false;
        Assert.Equal(2, await sincronizador.EnviarPendientesAsync());
        Assert.Equal(idsAntes[0], publicador.Enviados[0].Sobre.MensajeId);
        Assert.Equal(idsAntes[1], publicador.Enviados[1].Sobre.MensajeId);
        Assert.Empty(inventario.ConsultarSalidasPendientes());
    }

    [Fact]
    public async Task EnviarPendientes_UnDestinoSinCola_SoloConservaLaSalidaDeEseDestino()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = CrearInventario(dir);
        PublicadorFalso publicador = new PublicadorFalso { FallaPara = ruta => ruta == "inventario.ALA" };
        SincronizadorCatalogo sincronizador = CrearSincronizador(inventario, publicador);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));

        int enviadas = await sincronizador.EnviarPendientesAsync();

        Assert.Equal(1, enviadas);
        SalidaPendiente restante = Assert.Single(inventario.ConsultarSalidasPendientes());
        Assert.Equal("ALA", restante.DestinoSedeId);
        Assert.Equal("inventario.SJ", Assert.Single(publicador.Enviados).ClaveRuta);

        // El reinicio conserva la que falta y no repite la que ya salió.
        SalidaPendiente recargada = Assert.Single(FabricaInventario.Recargar(dir, "LIB", Remotas).ConsultarSalidasPendientes());
        Assert.Equal("ALA", recargada.DestinoSedeId);
    }

    [Fact]
    public async Task EnviarPendientes_TrasReiniciarLaPublicadora_ReenviaLoQueQuedabaPendiente()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = CrearInventario(dir);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 9, 1)); // dos reposiciones seguidas sin broker

        InventarioLocal reiniciado = FabricaInventario.Recargar(dir, "LIB", Remotas);
        PublicadorFalso publicador = new PublicadorFalso();
        int enviadas = await CrearSincronizador(reiniciado, publicador).EnviarPendientesAsync();

        Assert.Equal(2, enviadas);
        EntradaCatalogo ultima = LeerCambio(publicador.Enviados[0].Sobre);
        Assert.Equal(8, ultima.Disponible); // solo viaja el valor absoluto más reciente
        Assert.Equal(2L, ultima.Revision);
        Assert.Empty(reiniciado.ConsultarSalidasPendientes());
    }

    [Fact]
    public async Task EnviarPendientes_UnCambioNuevoMientrasSePublica_NoSeRetiraLaSalidaNueva()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = CrearInventario(dir);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        PublicadorSustituyente publicador = new PublicadorSustituyente(
            () => inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 12, 1)));
        SincronizadorCatalogo sincronizador = new SincronizadorCatalogo("LIB", Remotas, inventario, publicador, null, EsperaCorta);

        await sincronizador.EnviarPendientesAsync();

        SalidaPendiente[] quedan = inventario.ConsultarSalidasPendientes();
        Assert.Equal(2, quedan.Length);
        for (int i = 0; i < quedan.Length; i++)
        {
            Assert.Equal(2L, quedan[i].Revision);
            Assert.Equal(11, quedan[i].Disponible);
        }
    }

    [Fact]
    public async Task EnviarPendientes_SinSedesRemotas_NoHayNadaQueEnviar()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.Crear(dir, "LIB", FabricaInventario.Stock("CAF", 5, 0));
        PublicadorFalso publicador = new PublicadorFalso();
        SincronizadorCatalogo sincronizador = CrearSincronizador(inventario, publicador, Array.Empty<string>());
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));

        Assert.Equal(0, await sincronizador.EnviarPendientesAsync());
        Assert.Empty((await sincronizador.SolicitarInstantaneasAsync()));
        Assert.Empty(publicador.Enviados);
    }

    // ---- Solicitudes ----

    [Fact]
    public async Task Solicitar_PideLaInstantaneaACadaOtraSedeConCuerpoVacio()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        PublicadorFalso publicador = new PublicadorFalso();
        SincronizadorCatalogo sincronizador = CrearSincronizador(CrearInventario(dir), publicador);

        string[] sinConfirmar = await sincronizador.SolicitarInstantaneasAsync();

        Assert.Empty(sinConfirmar);
        Assert.Equal(2, publicador.Enviados.Length);
        Assert.Equal("inventario.ALA", publicador.Enviados[0].ClaveRuta);
        Assert.Equal("inventario.SJ", publicador.Enviados[1].ClaveRuta);
        for (int i = 0; i < 2; i++)
        {
            Sobre sobre = publicador.Enviados[i].Sobre;
            Assert.Equal("catalogo.solicitar", sobre.Tipo);
            Assert.Equal("LIB", sobre.OrigenSedeId);
            Assert.Equal(sobre.MensajeId, sobre.CorrelacionId); // petición nueva
            Assert.True(Guid.TryParse(sobre.MensajeId, out _));
            Assert.Equal(System.Text.Json.JsonValueKind.Object, sobre.Cuerpo.ValueKind);
            Assert.Equal(0, CantidadDePropiedades(sobre)); // cuerpo {}
        }
        Assert.NotEqual(publicador.Enviados[0].Sobre.MensajeId, publicador.Enviados[1].Sobre.MensajeId);
        Assert.Empty(sincronizador.SolicitudesPendientes());
    }

    [Fact]
    public async Task Solicitar_UnaSedeSinCola_SeReintentaConElMismoMensajeId()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        PublicadorFalso publicador = new PublicadorFalso { FallaPara = ruta => ruta == "inventario.SJ" };
        SincronizadorCatalogo sincronizador = CrearSincronizador(CrearInventario(dir), publicador);

        string[] sinConfirmar = await sincronizador.SolicitarInstantaneasAsync();

        Assert.Equal(new[] { "SJ" }, sinConfirmar);
        Assert.Equal("inventario.ALA", Assert.Single(publicador.Enviados).ClaveRuta);

        publicador.FallaPara = null;
        string[] despues = await sincronizador.SolicitarInstantaneasAsync();

        Assert.Empty(despues);
        Assert.Equal(2, publicador.Enviados.Length);
        Assert.Equal("inventario.SJ", publicador.Enviados[1].ClaveRuta);
        Assert.Empty(sincronizador.SolicitudesPendientes());
    }

    [Fact]
    public async Task Solicitar_ElReintentoReutilizaElMismoMensajeId()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        PublicadorIntentos publicador = new PublicadorIntentos { Falla = true };
        SincronizadorCatalogo sincronizador = CrearSincronizador(CrearInventario(dir), publicador);

        string[] primera = await sincronizador.SolicitarInstantaneasAsync(); // el broker no confirma
        publicador.Falla = false;
        string[] segunda = await sincronizador.SolicitarInstantaneasAsync();

        Assert.Equal(new[] { "ALA", "SJ" }, primera);
        Assert.Empty(segunda);
        Assert.Equal(4, publicador.Intentos.Length); // dos fallidos y dos confirmados
        Assert.Equal(publicador.Intentos[0].Sobre.MensajeId, publicador.Intentos[2].Sobre.MensajeId); // ALA
        Assert.Equal(publicador.Intentos[1].Sobre.MensajeId, publicador.Intentos[3].Sobre.MensajeId); // SJ
    }

    [Fact]
    public async Task Reconexion_VuelveAPedirLasInstantaneasDeTodasLasSedes()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        PublicadorFalso publicador = new PublicadorFalso();
        SincronizadorCatalogo sincronizador = CrearSincronizador(CrearInventario(dir), publicador);
        await sincronizador.SolicitarInstantaneasAsync();
        Assert.Empty(sincronizador.SolicitudesPendientes());

        sincronizador.AvisarReconexion();

        Assert.Equal(new[] { "ALA", "SJ" }, sincronizador.SolicitudesPendientes());
        await sincronizador.SolicitarInstantaneasAsync();
        Assert.Equal(4, publicador.Enviados.Length);
        Assert.NotEqual(publicador.Enviados[0].Sobre.MensajeId, publicador.Enviados[2].Sobre.MensajeId);
    }

    [Fact]
    public async Task Reconexion_ConUnaSolicitudAunPendiente_ConservaSuMensajeId()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        PublicadorIntentos publicador = new PublicadorIntentos { FallaPara = ruta => ruta == "inventario.SJ" };
        SincronizadorCatalogo sincronizador = CrearSincronizador(CrearInventario(dir), publicador);
        await sincronizador.SolicitarInstantaneasAsync(); // ALA confirmada, SJ sin cola

        sincronizador.AvisarReconexion();
        publicador.FallaPara = null;
        await sincronizador.SolicitarInstantaneasAsync();

        // Intentos: ALA, SJ(falla), ALA(reconexión), SJ(mismo ID que el intento fallido).
        Assert.Equal(4, publicador.Intentos.Length);
        Assert.Equal("inventario.SJ", publicador.Intentos[1].ClaveRuta);
        Assert.Equal("inventario.SJ", publicador.Intentos[3].ClaveRuta);
        Assert.Equal(publicador.Intentos[1].Sobre.MensajeId, publicador.Intentos[3].Sobre.MensajeId);
        Assert.NotEqual(publicador.Intentos[0].Sobre.MensajeId, publicador.Intentos[2].Sobre.MensajeId);
    }

    // ---- Bucle ----

    [Fact]
    public async Task Ejecutar_AlIniciarSolicitaYVaciaLoPendiente_YDespuesReaccionaAUnCambioNuevo()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = CrearInventario(dir);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1)); // pendiente desde antes de arrancar
        PublicadorFalso publicador = new PublicadorFalso();
        SincronizadorCatalogo sincronizador = CrearSincronizador(inventario, publicador);
        using CancellationTokenSource corte = new CancellationTokenSource();

        Task bucle = sincronizador.EjecutarAsync(corte.Token);
        await EsperarAsync(() => publicador.Enviados.Length >= 4); // 2 solicitudes + 2 cambios
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 9, 1));
        await EsperarAsync(() => publicador.Enviados.Length >= 6);

        corte.Cancel();
        await bucle;
        Assert.Empty(inventario.ConsultarSalidasPendientes());
        Assert.Empty(sincronizador.SolicitudesPendientes());
        Assert.Equal(8, LeerCambio(publicador.Enviados[5].Sobre).Disponible);
    }

    [Fact]
    public async Task Ejecutar_ElBrokerVuelveDespuesDeFallar_CompletaLoPendienteSinNuevosCambios()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = CrearInventario(dir);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        PublicadorFalso publicador = new PublicadorFalso { Falla = true };
        SincronizadorCatalogo sincronizador = CrearSincronizador(inventario, publicador);
        using CancellationTokenSource corte = new CancellationTokenSource();

        Task bucle = sincronizador.EjecutarAsync(corte.Token);
        await Task.Delay(200);
        Assert.Empty(publicador.Enviados);
        Assert.Equal(2, inventario.ConsultarSalidasPendientes().Length);

        publicador.Falla = false; // el broker vuelve; nadie avisa: el reintento periódico basta
        await EsperarAsync(() => inventario.ConsultarSalidasPendientes().Length == 0 && sincronizador.SolicitudesPendientes().Length == 0);

        corte.Cancel();
        await bucle;
        Assert.Equal(4, publicador.Enviados.Length);
    }

    [Fact]
    public async Task Ejecutar_Cancelado_TerminaYDejaDeEscucharLosCambios()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = CrearInventario(dir);
        PublicadorFalso publicador = new PublicadorFalso();
        SincronizadorCatalogo sincronizador = CrearSincronizador(inventario, publicador);
        using CancellationTokenSource corte = new CancellationTokenSource();
        Task bucle = sincronizador.EjecutarAsync(corte.Token);
        await EsperarAsync(() => publicador.Enviados.Length >= 2);

        corte.Cancel();
        await bucle;
        int enviadosAlCerrar = publicador.Enviados.Length;
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        await Task.Delay(150);

        Assert.Equal(enviadosAlCerrar, publicador.Enviados.Length);
        Assert.Equal(2, inventario.ConsultarSalidasPendientes().Length);
    }

    [Fact]
    public async Task Ejecutar_ElRetiroNoSePuedeGuardar_SeguirReintentandoSinCaer()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        bool fallar = false;
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"), etapa =>
        {
            if (fallar && etapa == EtapaGuardado.TemporalEscrito)
            {
                throw new IOException("disco lleno");
            }
        });
        InventarioLocal inventario = CrearInventario(dir, repositorio);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        fallar = true;
        PublicadorFalso publicador = new PublicadorFalso();
        SincronizadorCatalogo sincronizador = CrearSincronizador(inventario, publicador);
        using CancellationTokenSource corte = new CancellationTokenSource();

        Task bucle = sincronizador.EjecutarAsync(corte.Token);
        await EsperarAsync(() => publicador.Enviados.Length >= 6); // reenvía lo mismo mientras el disco falla
        fallar = false;
        await EsperarAsync(() => inventario.ConsultarSalidasPendientes().Length == 0);

        corte.Cancel();
        await bucle;
        Assert.False(bucle.IsFaulted);
    }

    // ---- Argumentos ----

    [Fact]
    public void Constructor_ConArgumentosInvalidos_Lanza()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = CrearInventario(dir);
        PublicadorFalso publicador = new PublicadorFalso();

        Assert.Throws<ArgumentException>(() => new SincronizadorCatalogo(" ", Remotas, inventario, publicador));
        Assert.Throws<ArgumentException>(() => new SincronizadorCatalogo("SJ", new[] { "ALA" }, inventario, publicador)); // sede ajena
        Assert.Throws<ArgumentException>(() => new SincronizadorCatalogo("LIB", new[] { "LIB" }, inventario, publicador));
        Assert.Throws<ArgumentException>(() => new SincronizadorCatalogo("LIB", new[] { "ALA", "ALA" }, inventario, publicador));
        Assert.Throws<ArgumentNullException>(() => new SincronizadorCatalogo("LIB", null!, inventario, publicador));
        Assert.Throws<ArgumentNullException>(() => new SincronizadorCatalogo("LIB", Remotas, null!, publicador));
        Assert.Throws<ArgumentNullException>(() => new SincronizadorCatalogo("LIB", Remotas, inventario, null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SincronizadorCatalogo("LIB", Remotas, inventario, publicador, null, TimeSpan.Zero));
    }

    // ---- Ayudas ----

    private static int CantidadDePropiedades(Sobre sobre)
    {
        int cantidad = 0;
        foreach (System.Text.Json.JsonProperty _ in sobre.Cuerpo.EnumerateObject())
        {
            cantidad++;
        }
        return cantidad;
    }

    private static async Task EsperarAsync(Func<bool> condicion)
    {
        DateTime limite = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condicion())
        {
            Assert.True(DateTime.UtcNow < limite, "La condición no se cumplió a tiempo.");
            await Task.Delay(20);
        }
    }

    /// Publica bien y, justo después de la primera publicación, ejecuta una acción (por ejemplo, un cambio nuevo).
    private sealed class PublicadorSustituyente : IPublicadorMensajes
    {
        private readonly PublicadorFalso _interno = new PublicadorFalso();
        private readonly Action _alPublicar;
        private bool _hecho;

        public PublicadorSustituyente(Action alPublicar)
        {
            _alPublicar = alPublicar;
        }

        public Task PublicarAsync(string claveRuta, Sobre sobre, CancellationToken cancelacion = default)
        {
            Task tarea = _interno.PublicarAsync(claveRuta, sobre, cancelacion);
            if (!_hecho)
            {
                _hecho = true;
                _alPublicar();
            }
            return tarea;
        }
    }

    /// Registra cada intento de publicación, también los que fallan, y puede fallar a voluntad.
    private sealed class PublicadorIntentos : IPublicadorMensajes
    {
        private SobreSaliente[] _intentos = Array.Empty<SobreSaliente>();

        public bool Falla { get; set; }
        public Func<string, bool>? FallaPara { get; set; }
        public SobreSaliente[] Intentos => _intentos;

        public Task PublicarAsync(string claveRuta, Sobre sobre, CancellationToken cancelacion = default)
        {
            SobreSaliente[] nuevos = new SobreSaliente[_intentos.Length + 1];
            Array.Copy(_intentos, nuevos, _intentos.Length);
            nuevos[_intentos.Length] = new SobreSaliente(claveRuta, sobre);
            _intentos = nuevos;

            if (Falla || (FallaPara != null && FallaPara(claveRuta)))
            {
                throw new EnvioFallidoException("Sin destinatario (simulado).");
            }
            return Task.CompletedTask;
        }
    }
}
