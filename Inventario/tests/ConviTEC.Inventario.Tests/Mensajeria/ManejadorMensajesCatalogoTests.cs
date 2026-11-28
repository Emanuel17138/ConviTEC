using System;
using System.IO;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Mensajeria;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Negocio;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Mensajeria;

/// Mensajes de catálogo desde el sobre hasta la réplica y la respuesta de la instantánea, sin broker.
/// La sede bajo prueba es SJ, con LIB y ALA como remotas.
public class ManejadorMensajesCatalogoTests
{
    private static readonly string[] Remotas = { "ALA", "LIB" };

    private const string ProductoCaf =
        "{\"productoId\":\"CAF\",\"nombre\":\"Nombre CAF\",\"descripcion\":\"Descripción\",\"categoria\":\"Categoría\",\"precioUnitarioCentimos\":150000}";

    private sealed class Sede : IDisposable
    {
        public DirectorioTemporal Dir { get; } = new DirectorioTemporal();
        public InventarioLocal Inventario { get; }
        public CatalogoReplicado Catalogo { get; }
        public ManejadorMensajesCatalogo Manejador { get; }

        public Sede(Func<RutasArchivosInventario, RepositorioCatalogo>? repositorioCatalogo = null)
        {
            Inventario = FabricaInventario.CrearCon(
                Dir, "SJ", Remotas, null,
                new[] { FabricaInventario.Stock("ARR", 4, 4), FabricaInventario.Stock("CAF", 5, 2) });
            Catalogo = FabricaCatalogo.Crear(Dir, "SJ", Remotas, repositorioCatalogo);
            Manejador = new ManejadorMensajesCatalogo("SJ", Inventario, Catalogo);
        }

        public void Dispose() => Dir.Dispose();
    }

    private static Sobre Mensaje(string tipo, string origen, string cuerpo, string mensajeId = "m-1", string correlacion = "m-1") =>
        Sobre.ConCuerpoTexto(tipo, mensajeId, correlacion, origen, "SJ", cuerpo);

    private static string Cambio(int disponible, long revision) =>
        $"{{\"producto\":{ProductoCaf},\"disponible\":{disponible},\"revision\":{revision}}}";

    private static Sobre Actualizado(string origen, int disponible, long revision, string mensajeId = "m-1") =>
        Mensaje("catalogo.actualizado", origen, Cambio(disponible, revision), mensajeId);

    private static void AfirmarOferta(Sede sede, string origen, int disponible, long revision)
    {
        Assert.True(sede.Catalogo.BuscarOferta(origen, "CAF", out OfertaReplicada? oferta));
        Assert.Equal(disponible, oferta.Disponible);
        Assert.Equal(revision, oferta.Revision);
    }

    [Fact]
    public void Tipos_SonLosTresMensajesDeCatalogo()
    {
        using Sede sede = ConSede();

        Assert.Equal(new[] { "catalogo.actualizado", "catalogo.solicitar", "catalogo.instantanea" }, sede.Manejador.Tipos);
    }

    [Fact]
    public void Actualizado_CambioNuevo_SeAplicaYSeAcusaSinSalidas()
    {
        using Sede sede = ConSede();

        ResultadoDespacho despacho = sede.Manejador.Manejar(Actualizado("LIB", 2, 10));

        Assert.Equal(AccionConsumo.Acusar, despacho.Accion);
        Assert.Empty(despacho.Salidas);
        AfirmarOferta(sede, "LIB", 2, 10);
        Assert.False(sede.Catalogo.EstaSincronizada("LIB")); // un cambio aislado no completa la sincronización
    }

    [Fact]
    public void Actualizado_RevisionVieja_YDuplicado_NoDeshacenLaCopia()
    {
        using Sede sede = ConSede();
        sede.Manejador.Manejar(Actualizado("LIB", 3, 8, "m-8"));

        sede.Manejador.Manejar(Actualizado("LIB", 2, 10, "m-10"));
        ResultadoDespacho tardia = sede.Manejador.Manejar(Actualizado("LIB", 9, 9, "m-9"));
        ResultadoDespacho duplicado = sede.Manejador.Manejar(Actualizado("LIB", 2, 10, "m-10"));

        Assert.Equal(AccionConsumo.Acusar, tardia.Accion);
        Assert.Equal(AccionConsumo.Acusar, duplicado.Accion);
        AfirmarOferta(sede, "LIB", 2, 10);
    }

    [Fact]
    public void Actualizado_RevisionDeOtraSede_SeComparaPorSeparado()
    {
        using Sede sede = ConSede();
        sede.Manejador.Manejar(Actualizado("LIB", 2, 10));

        sede.Manejador.Manejar(Actualizado("ALA", 7, 1));

        AfirmarOferta(sede, "LIB", 2, 10);
        AfirmarOferta(sede, "ALA", 7, 1);
    }

    [Fact]
    public void Instantanea_ConCeros_AplicaTodoYMarcaLaPrimeraSincronizacion()
    {
        using Sede sede = ConSede();
        string cuerpo = $"{{\"entradas\":[{Cambio(0, 4)}]}}";

        ResultadoDespacho despacho = sede.Manejador.Manejar(Mensaje("catalogo.instantanea", "LIB", cuerpo));

        Assert.Equal(AccionConsumo.Acusar, despacho.Accion);
        Assert.Empty(despacho.Salidas);
        AfirmarOferta(sede, "LIB", 0, 4);
        Assert.True(sede.Catalogo.EstaSincronizada("LIB"));
        Assert.False(sede.Catalogo.EstaInicializado); // falta ALA
    }

    [Fact]
    public void Instantanea_Vacia_MarcaLaSincronizacionSinOfertas()
    {
        using Sede sede = ConSede();

        sede.Manejador.Manejar(Mensaje("catalogo.instantanea", "ALA", "{\"entradas\":[]}"));
        sede.Manejador.Manejar(Mensaje("catalogo.instantanea", "LIB", "{\"entradas\":[]}", "m-2"));

        Assert.True(sede.Catalogo.EstaInicializado);
        Assert.Empty(sede.Catalogo.ConsultarOfertas());
    }

    [Fact]
    public void Instantanea_Tardia_NoDeshaceUnCambioMasReciente()
    {
        using Sede sede = ConSede();
        sede.Manejador.Manejar(Actualizado("LIB", 2, 10));

        sede.Manejador.Manejar(Mensaje("catalogo.instantanea", "LIB", $"{{\"entradas\":[{Cambio(9, 9)}]}}", "m-2"));

        AfirmarOferta(sede, "LIB", 2, 10);
        Assert.True(sede.Catalogo.EstaSincronizada("LIB"));
    }

    [Fact]
    public void Instantanea_ConUnProductoRepetido_SeDescartaYNoAplicaNada()
    {
        using Sede sede = ConSede();
        string cuerpo = $"{{\"entradas\":[{Cambio(1, 1)},{Cambio(2, 2)}]}}";

        ResultadoDespacho despacho = sede.Manejador.Manejar(Mensaje("catalogo.instantanea", "LIB", cuerpo));

        Assert.Equal(AccionConsumo.Descartar, despacho.Accion);
        Assert.False(sede.Catalogo.BuscarOferta("LIB", "CAF", out _));
        Assert.False(sede.Catalogo.EstaSincronizada("LIB"));
    }

    [Theory]
    [InlineData("catalogo.actualizado")]
    [InlineData("catalogo.instantanea")]
    [InlineData("catalogo.solicitar")]
    public void MensajeDeUnaSedeQueNoEsRemota_SeDescartaSinTocarNada(string tipo)
    {
        using Sede sede = ConSede();
        string cuerpo = tipo == "catalogo.actualizado" ? Cambio(1, 1)
            : tipo == "catalogo.instantanea" ? $"{{\"entradas\":[{Cambio(1, 1)}]}}" : "{}";

        ResultadoDespacho ajena = sede.Manejador.Manejar(Mensaje(tipo, "XYZ", cuerpo));
        ResultadoDespacho propia = sede.Manejador.Manejar(Mensaje(tipo, "SJ", cuerpo));

        Assert.Equal(AccionConsumo.Descartar, ajena.Accion);
        Assert.Equal(AccionConsumo.Descartar, propia.Accion);
        Assert.Empty(sede.Catalogo.ConsultarOfertas());
    }

    [Theory]
    [InlineData("catalogo.actualizado", "{}")]
    [InlineData("catalogo.actualizado", "null")]
    [InlineData("catalogo.instantanea", "{}")]
    [InlineData("catalogo.instantanea", "{\"entradas\":[{}]}")]
    public void CuerpoInvalido_SeDescartaConSuMotivo(string tipo, string cuerpo)
    {
        using Sede sede = ConSede();

        ResultadoDespacho despacho = sede.Manejador.Manejar(Mensaje(tipo, "LIB", cuerpo));

        Assert.Equal(AccionConsumo.Descartar, despacho.Accion);
        Assert.NotEmpty(despacho.Motivo);
        Assert.Empty(sede.Catalogo.ConsultarOfertas());
    }

    [Fact]
    public void Solicitar_ResponderConTodasLasOfertasPropiasIncluidosLosCeros()
    {
        using Sede sede = ConSede();
        sede.Inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 3)); // CAF revisión 1, disponible 2
        Sobre solicitud = Mensaje("catalogo.solicitar", "LIB", "{}", "sol-1", "sol-1");

        ResultadoDespacho despacho = sede.Manejador.Manejar(solicitud);

        Assert.Equal(AccionConsumo.Acusar, despacho.Accion);
        SobreSaliente salida = Assert.Single(despacho.Salidas);
        Assert.Equal("inventario.LIB", salida.ClaveRuta);
        Assert.Equal("catalogo.instantanea", salida.Sobre.Tipo);
        Assert.Equal("SJ", salida.Sobre.OrigenSedeId);
        Assert.Equal("LIB", salida.Sobre.DestinoSedeId);
        Assert.Equal("sol-1", salida.Sobre.CorrelacionId);
        Assert.Equal(IdentificadorDerivado.DeSolicitudCatalogo("sol-1"), salida.Sobre.MensajeId);

        Assert.True(CuerpoCatalogo.TryLeerInstantanea(salida.Sobre.Cuerpo, out EntradaCatalogo[]? entradas, out string motivo), motivo);
        Assert.Equal(2, entradas!.Length);
        Assert.Equal("ARR", entradas[0].Producto.ProductoId);
        Assert.Equal(0, entradas[0].Disponible); // agotado: se informa el cero
        Assert.Equal(0L, entradas[0].Revision);
        Assert.Equal("CAF", entradas[1].Producto.ProductoId);
        Assert.Equal(2, entradas[1].Disponible);
        Assert.Equal(1L, entradas[1].Revision);
    }

    [Fact]
    public void Solicitar_Repetida_LlevaElMismoMensajeIdDeRespuesta()
    {
        using Sede sede = ConSede();
        Sobre solicitud = Mensaje("catalogo.solicitar", "LIB", "{}", "sol-1", "sol-1");

        string primera = Assert.Single(sede.Manejador.Manejar(solicitud).Salidas).Sobre.MensajeId;
        string segunda = Assert.Single(sede.Manejador.Manejar(solicitud).Salidas).Sobre.MensajeId;

        Assert.Equal(primera, segunda);
    }

    [Fact]
    public void Solicitar_SinProductos_ResponderInstantaneaVacia()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal vacio = FabricaInventario.CrearCon(dir, "SJ", Remotas, null, Array.Empty<StockLocal>());
        CatalogoReplicado catalogo = FabricaCatalogo.Crear(dir, "SJ", Remotas);
        ManejadorMensajesCatalogo manejador = new ManejadorMensajesCatalogo("SJ", vacio, catalogo);

        ResultadoDespacho despacho = manejador.Manejar(Mensaje("catalogo.solicitar", "LIB", "{}"));

        SobreSaliente salida = Assert.Single(despacho.Salidas);
        Assert.True(CuerpoCatalogo.TryLeerInstantanea(salida.Sobre.Cuerpo, out EntradaCatalogo[]? entradas, out _));
        Assert.Empty(entradas!);
    }

    [Fact]
    public void Actualizado_ElDiscoFalla_DevuelveReintentarYNadaCambia()
    {
        bool fallar = false;
        using Sede sede = ConSede(rutas => new RepositorioCatalogo(rutas, etapa =>
        {
            if (fallar && etapa == EtapaGuardado.TemporalEscrito)
            {
                throw new IOException("disco lleno");
            }
        }));
        fallar = true;

        ResultadoDespacho fallido = sede.Manejador.Manejar(Actualizado("LIB", 2, 10));

        Assert.Equal(AccionConsumo.Reintentar, fallido.Accion);
        Assert.False(sede.Catalogo.BuscarOferta("LIB", "CAF", out _));

        fallar = false;
        ResultadoDespacho reprocesado = sede.Manejador.Manejar(Actualizado("LIB", 2, 10));
        Assert.Equal(AccionConsumo.Acusar, reprocesado.Accion);
        AfirmarOferta(sede, "LIB", 2, 10);
    }

    [Fact]
    public void Instantanea_ElDiscoFalla_DevuelveReintentarYNoMarcaLaSede()
    {
        bool fallar = false;
        using Sede sede = ConSede(rutas => new RepositorioCatalogo(rutas, etapa =>
        {
            if (fallar && etapa == EtapaGuardado.TemporalEscrito)
            {
                throw new IOException("disco lleno");
            }
        }));
        fallar = true;

        ResultadoDespacho fallido = sede.Manejador.Manejar(Mensaje("catalogo.instantanea", "LIB", "{\"entradas\":[]}"));

        Assert.Equal(AccionConsumo.Reintentar, fallido.Accion);
        Assert.False(sede.Catalogo.EstaSincronizada("LIB"));
    }

    [Fact]
    public void Constructor_ConSedeDistintaOArgumentosNulos_Lanza()
    {
        using Sede sede = ConSede();

        Assert.Throws<ArgumentException>(() => new ManejadorMensajesCatalogo("LIB", sede.Inventario, sede.Catalogo));
        Assert.Throws<ArgumentNullException>(() => new ManejadorMensajesCatalogo("SJ", null!, sede.Catalogo));
        Assert.Throws<ArgumentNullException>(() => new ManejadorMensajesCatalogo("SJ", sede.Inventario, null!));
        Assert.Throws<ArgumentNullException>(() => sede.Manejador.Manejar(null!));
    }

    [Fact]
    public void Despachador_RegistradoConStockYCatalogo_AtiendeLosSeisTipos()
    {
        using Sede sede = ConSede();
        DespachadorInventario despachador = new DespachadorInventario("SJ");
        despachador.Registrar(new ManejadorComandosStock("SJ", new ProveedorStock(sede.Inventario)));
        despachador.Registrar(sede.Manejador);

        Assert.True(despachador.AtiendeTipo("stock.reservar"));
        Assert.True(despachador.AtiendeTipo("catalogo.actualizado"));
        Assert.True(despachador.AtiendeTipo("catalogo.solicitar"));
        Assert.True(despachador.AtiendeTipo("catalogo.instantanea"));
        Assert.Equal(AccionConsumo.Acusar, despachador.Despachar(Actualizado("LIB", 1, 1)).Accion);
    }

    private static Sede ConSede(Func<RutasArchivosInventario, RepositorioCatalogo>? repositorioCatalogo = null) =>
        new Sede(repositorioCatalogo);
}
