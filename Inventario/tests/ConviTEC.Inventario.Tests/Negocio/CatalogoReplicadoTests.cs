using System;
using System.Threading.Tasks;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Negocio;

/// Réplica del catálogo (RF03): regla de revisión, primera instantánea, orden, persistencia y cortes.
public class CatalogoReplicadoTests
{
    private static readonly string[] Remotas = { "LIB", "ALA", "LIM" };

    private sealed class CorteSimulado : Exception
    {
    }

    private static EntradaCatalogo Entrada(string productoId, int disponible, long revision) =>
        FabricaCatalogo.Entrada(productoId, disponible, revision);

    private static CatalogoReplicado Crear(DirectorioTemporal dir, params string[] remotas) =>
        FabricaCatalogo.Crear(dir, "SJ", remotas.Length == 0 ? Remotas : remotas);

    private static void AfirmarOferta(CatalogoReplicado catalogo, string sede, string producto, int disponible, long revision)
    {
        Assert.True(catalogo.BuscarOferta(sede, producto, out OfertaReplicada? oferta));
        Assert.Equal(disponible, oferta.Disponible);
        Assert.Equal(revision, oferta.Revision);
        Assert.Equal(sede, oferta.SedeId);
    }

    // ---- Regla de revisión -------------------------------------------------------------------------------------

    [Fact]
    public void ParejaNuevaSeAplica()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);

        Assert.True(catalogo.AplicarCambio("LIB", Entrada("CAF", 4, 1)));

        AfirmarOferta(catalogo, "LIB", "CAF", 4, 1);
    }

    [Fact]
    public void RevisionMayorSustituyeElValorAbsoluto()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);
        catalogo.AplicarCambio("LIB", Entrada("CAF", 4, 1));

        Assert.True(catalogo.AplicarCambio("LIB", Entrada("CAF", 1, 2)));

        AfirmarOferta(catalogo, "LIB", "CAF", 1, 2);
    }

    [Fact]
    public void RevisionMenorOIgualSeIgnoraYNoEscribeElArchivo()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);
        catalogo.AplicarCambio("LIB", Entrada("CAF", 4, 5));
        string antes = dir.Leer(RutasArchivosInventario.NombreCatalogoReplicado);
        DateTime momento = System.IO.File.GetLastWriteTimeUtc(dir.Rutas("SJ").CatalogoReplicado);

        Assert.False(catalogo.AplicarCambio("LIB", Entrada("CAF", 9, 4)));
        Assert.False(catalogo.AplicarCambio("LIB", Entrada("CAF", 9, 5)));

        AfirmarOferta(catalogo, "LIB", "CAF", 4, 5);
        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreCatalogoReplicado));
        Assert.Equal(momento, System.IO.File.GetLastWriteTimeUtc(dir.Rutas("SJ").CatalogoReplicado));
    }

    [Fact]
    public void RevisionOchoRecibeDiezNueveYDiezConservaDiezAntesYDespuesDeReiniciar()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);
        catalogo.AplicarCambio("LIB", Entrada("CAF", 7, 8));
        catalogo.AplicarCambio("ALA", Entrada("CAF", 6, 3));

        catalogo.AplicarCambio("LIB", Entrada("CAF", 2, 10));
        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 5, 9) });
        catalogo.AplicarCambio("LIB", Entrada("CAF", 2, 10));

        AfirmarOferta(catalogo, "LIB", "CAF", 2, 10);
        AfirmarOferta(catalogo, "ALA", "CAF", 6, 3); // La revisión de otra sede se compara por separado.

        CatalogoReplicado reiniciado = FabricaCatalogo.Recargar(dir, "SJ", Remotas);
        AfirmarOferta(reiniciado, "LIB", "CAF", 2, 10);
        AfirmarOferta(reiniciado, "ALA", "CAF", 6, 3);
    }

    [Fact]
    public void LaRevisionDeOtraSedeParaElMismoProductoSeCompararIndependientemente()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);
        catalogo.AplicarCambio("LIB", Entrada("CAF", 2, 10));

        Assert.True(catalogo.AplicarCambio("ALA", Entrada("CAF", 8, 1)));

        AfirmarOferta(catalogo, "LIB", "CAF", 2, 10);
        AfirmarOferta(catalogo, "ALA", "CAF", 8, 1);
    }

    [Fact]
    public void UnaOfertaConCeroDisponiblesEsValidaYReemplazaAlValorAnterior()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);
        catalogo.AplicarCambio("LIB", Entrada("CAF", 4, 1));

        Assert.True(catalogo.AplicarCambio("LIB", Entrada("CAF", 0, 2)));

        AfirmarOferta(catalogo, "LIB", "CAF", 0, 2);
    }

    // ---- Instantáneas y primera sincronización -----------------------------------------------------------------

    [Fact]
    public void InstantaneaAplicaLaReglaPorEntradaYNoBorraLasOfertasAusentes()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);
        catalogo.AplicarCambio("LIB", Entrada("CAF", 4, 5));
        catalogo.AplicarCambio("LIB", Entrada("AZU", 3, 2));

        int aplicadas = catalogo.AplicarInstantanea("LIB", new[]
        {
            Entrada("CAF", 9, 4), // vieja: se ignora
            Entrada("ARR", 8, 1), // nueva: se aplica
            Entrada("AZU", 1, 6), // más reciente: se aplica
        });

        Assert.Equal(2, aplicadas);
        AfirmarOferta(catalogo, "LIB", "CAF", 4, 5);
        AfirmarOferta(catalogo, "LIB", "ARR", 8, 1);
        AfirmarOferta(catalogo, "LIB", "AZU", 1, 6);
    }

    [Fact]
    public void InstantaneaMarcaLaSedeYUnCambioAisladoNo()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);

        catalogo.AplicarCambio("LIB", Entrada("CAF", 4, 1));
        Assert.False(catalogo.EstaSincronizada("LIB"));

        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) });
        Assert.True(catalogo.EstaSincronizada("LIB"));
        Assert.False(catalogo.EstaSincronizada("ALA"));
    }

    [Fact]
    public void InstantaneaVaciaTambienMarcaLaSedeYSeConservaAlReiniciar()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);

        Assert.Equal(0, catalogo.AplicarInstantanea("LIM", Array.Empty<EntradaCatalogo>()));

        Assert.True(catalogo.EstaSincronizada("LIM"));
        Assert.True(FabricaCatalogo.Recargar(dir, "SJ", Remotas).EstaSincronizada("LIM"));
    }

    [Fact]
    public void InstantaneaRepetidaSinNovedadesNoReescribeElArchivo()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);
        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) });
        DateTime momento = System.IO.File.GetLastWriteTimeUtc(dir.Rutas("SJ").CatalogoReplicado);

        Assert.Equal(0, catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) }));

        Assert.Equal(momento, System.IO.File.GetLastWriteTimeUtc(dir.Rutas("SJ").CatalogoReplicado));
    }

    [Fact]
    public void InstantaneaConProductoRepetidoSeRechazaEnteraSinEfecto()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);

        Assert.Throws<ArgumentException>(() =>
            catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1), Entrada("ARR", 1, 1), Entrada("CAF", 5, 2) }));

        Assert.False(catalogo.BuscarOferta("LIB", "ARR", out _));
        Assert.False(catalogo.EstaSincronizada("LIB"));
        Assert.False(dir.Existe(RutasArchivosInventario.NombreCatalogoReplicado));
    }

    // ---- Inicialización -----------------------------------------------------------------------------------------

    [Fact]
    public void PasaDePendienteAInicializadoSoloAlRecibirLaTerceraInstantanea()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);
        Assert.False(catalogo.EstaInicializado);
        Assert.Equal(Remotas, catalogo.SedesPendientes());

        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) });
        catalogo.AplicarInstantanea("ALA", new[] { Entrada("ARR", 2, 1) });
        catalogo.AplicarCambio("LIM", Entrada("CAF", 9, 3));

        Assert.False(catalogo.EstaInicializado);
        Assert.Equal(new[] { "LIM" }, catalogo.SedesPendientes());

        catalogo.AplicarInstantanea("LIM", Array.Empty<EntradaCatalogo>());

        Assert.True(catalogo.EstaInicializado);
        Assert.Empty(catalogo.SedesPendientes());
        AfirmarOferta(catalogo, "LIM", "CAF", 9, 3); // La instantánea vacía no borra el cambio previo.
    }

    [Fact]
    public void VistaInicializadaSinProductosEsVaciaPeroInicializada()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir, "LIB");

        catalogo.AplicarInstantanea("LIB", Array.Empty<EntradaCatalogo>());

        Assert.True(catalogo.EstaInicializado);
        Assert.Empty(catalogo.ConsultarOfertas());
    }

    [Fact]
    public void SinSedesRemotasEstaInicializadaDesdeElInicio()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = FabricaCatalogo.Crear(dir, "SJ", Array.Empty<string>());

        Assert.True(catalogo.EstaInicializado);
        Assert.Empty(catalogo.SedesPendientes());
        Assert.Empty(catalogo.ConsultarOfertas());
    }

    [Fact]
    public void LaInicializacionYLasOfertasSobrevivenAlReinicio()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);
        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) });
        catalogo.AplicarInstantanea("ALA", Array.Empty<EntradaCatalogo>());
        catalogo.AplicarInstantanea("LIM", Array.Empty<EntradaCatalogo>());

        CatalogoReplicado reiniciado = FabricaCatalogo.Recargar(dir, "SJ", Remotas);

        Assert.True(reiniciado.EstaInicializado);
        AfirmarOferta(reiniciado, "LIB", "CAF", 4, 1);
    }

    [Fact]
    public void PrimerArranqueSinArchivoNoEscribeNada()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();

        CatalogoReplicado catalogo = Crear(dir);

        Assert.False(catalogo.EstaInicializado);
        Assert.False(dir.Existe(RutasArchivosInventario.NombreCatalogoReplicado));
    }

    [Fact]
    public void UnaSedeNuevaEnLaConfiguracionVuelveAExigirSuPrimeraInstantanea()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir, "LIB");
        catalogo.AplicarInstantanea("LIB", Array.Empty<EntradaCatalogo>());

        CatalogoReplicado ampliado = FabricaCatalogo.Recargar(dir, "SJ", "LIB", "ALA");

        Assert.False(ampliado.EstaInicializado);
        Assert.Equal(new[] { "ALA" }, ampliado.SedesPendientes());
    }

    [Fact]
    public void LasOfertasDeUnaSedeYaNoConfiguradaSeConservanPeroNoSeConsultan()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir, "LIB", "ALA");
        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) });
        catalogo.AplicarInstantanea("ALA", new[] { Entrada("CAF", 2, 1) });

        CatalogoReplicado reducido = FabricaCatalogo.Recargar(dir, "SJ", "LIB");
        reducido.AplicarCambio("LIB", Entrada("ARR", 1, 1)); // El guardado conserva lo de ALA.

        OfertaReplicada[] ofertas = reducido.ConsultarOfertas();
        Assert.Equal(2, ofertas.Length);
        Assert.All(ofertas, o => Assert.Equal("LIB", o.SedeId));
        Assert.True(FabricaCatalogo.Recargar(dir, "SJ", "LIB", "ALA").BuscarOferta("ALA", "CAF", out _));
    }

    // ---- Orden de la vista --------------------------------------------------------------------------------------

    [Fact]
    public void LasOfertasSalenPorProductoYLuegoPorSedeConCerosYSinSedesSinDatos()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);
        catalogo.AplicarCambio("LIB", Entrada("CAF", 0, 1));
        catalogo.AplicarCambio("ALA", Entrada("CAF", 3, 1));
        catalogo.AplicarCambio("LIM", Entrada("ARR", 5, 1)); // CAF se omite en LIM: no hay datos.
        catalogo.AplicarCambio("LIB", Entrada("CA", 1, 1));

        OfertaReplicada[] ofertas = catalogo.ConsultarOfertas();

        string[] esperado = { "ARR/LIM", "CA/LIB", "CAF/ALA", "CAF/LIB" };
        Assert.Equal(esperado.Length, ofertas.Length);
        for (int i = 0; i < esperado.Length; i++)
        {
            Assert.Equal(esperado[i], ofertas[i].Producto.ProductoId + "/" + ofertas[i].SedeId);
        }
        Assert.Equal(0, ofertas[3].Disponible);
    }

    [Fact]
    public void LaComparacionDeIdentificadoresDistingueMayusculas()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);

        catalogo.AplicarCambio("LIB", Entrada("CAF", 1, 1));
        catalogo.AplicarCambio("LIB", Entrada("caf", 2, 1));

        Assert.Equal(2, catalogo.ConsultarOfertas().Length);
        Assert.Throws<ArgumentException>(() => catalogo.AplicarCambio("lib", Entrada("CAF", 1, 1)));
    }

    // ---- Entradas inválidas -------------------------------------------------------------------------------------

    [Fact]
    public void RechazaOrigenPropioDesconocidoOVacio()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);

        Assert.Throws<ArgumentException>(() => catalogo.AplicarCambio("SJ", Entrada("CAF", 1, 1)));
        Assert.Throws<ArgumentException>(() => catalogo.AplicarCambio("XYZ", Entrada("CAF", 1, 1)));
        Assert.Throws<ArgumentException>(() => catalogo.AplicarCambio(" ", Entrada("CAF", 1, 1)));
        Assert.Throws<ArgumentNullException>(() => catalogo.AplicarCambio(null!, Entrada("CAF", 1, 1)));
        Assert.Throws<ArgumentException>(() => catalogo.AplicarInstantanea("SJ", Array.Empty<EntradaCatalogo>()));
        Assert.False(dir.Existe(RutasArchivosInventario.NombreCatalogoReplicado));
    }

    [Fact]
    public void RechazaEntradasNulasOInstantaneaNula()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);

        Assert.Throws<ArgumentNullException>(() => catalogo.AplicarCambio("LIB", null!));
        Assert.Throws<ArgumentNullException>(() => catalogo.AplicarInstantanea("LIB", null!));
        Assert.Throws<ArgumentException>(() => catalogo.AplicarInstantanea("LIB", new EntradaCatalogo[] { null! }));
    }

    [Fact]
    public void LasEntradasNoPuedenTenerProductoNuloNiValoresNegativos()
    {
        Producto producto = new Producto("CAF", "Café", "d", "c", 100);

        Assert.Throws<ArgumentNullException>(() => new EntradaCatalogo(null!, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntradaCatalogo(producto, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntradaCatalogo(producto, 1, -1));
    }

    [Fact]
    public void LaConfiguracionDeSedesRemotasSeValida()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();

        Assert.Throws<ArgumentException>(() => FabricaCatalogo.Crear(dir, "SJ", new[] { "SJ" }));
        Assert.Throws<ArgumentException>(() => FabricaCatalogo.Crear(dir, "SJ", new[] { "LIB", "LIB" }));
        Assert.Throws<ArgumentNullException>(() => new CargadorCatalogo().Cargar(dir.Rutas("SJ"), null!));
        Assert.Throws<ArgumentNullException>(() => new CargadorCatalogo().Cargar(null!, Remotas));
    }

    // ---- Cortes y fallos de guardado ---------------------------------------------------------------------------

    private static Func<RutasArchivosInventario, RepositorioCatalogo> CortarEn(EtapaGuardado etapa) =>
        rutas => new RepositorioCatalogo(rutas, e =>
        {
            if (e == etapa)
            {
                throw new CorteSimulado();
            }
        });

    [Fact]
    public void CorteConElTemporalCompletoConservaElArchivoAnterior()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado normal = Crear(dir);
        normal.AplicarCambio("LIB", Entrada("CAF", 4, 1));
        CatalogoReplicado conCorte = FabricaCatalogo.Crear(dir, "SJ", Remotas, CortarEn(EtapaGuardado.TemporalEscrito));

        Assert.Throws<CorteSimulado>(() => conCorte.AplicarCambio("LIB", Entrada("CAF", 1, 2)));

        AfirmarOferta(conCorte, "LIB", "CAF", 4, 1); // La memoria no cambió.
        AfirmarOferta(FabricaCatalogo.Recargar(dir, "SJ", Remotas), "LIB", "CAF", 4, 1);
    }

    [Fact]
    public void CorteDespuesDelReemplazoDejaElArchivoNuevoCompleto()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado normal = Crear(dir);
        normal.AplicarCambio("LIB", Entrada("CAF", 4, 1));
        CatalogoReplicado conCorte = FabricaCatalogo.Crear(dir, "SJ", Remotas, CortarEn(EtapaGuardado.Reemplazado));

        Assert.Throws<CorteSimulado>(() => conCorte.AplicarInstantanea("LIB", new[] { Entrada("CAF", 1, 2) }));

        CatalogoReplicado reiniciado = FabricaCatalogo.Recargar(dir, "SJ", Remotas);
        AfirmarOferta(reiniciado, "LIB", "CAF", 1, 2);
        Assert.True(reiniciado.EstaSincronizada("LIB"));
    }

    [Fact]
    public void CorteEnLaPrimeraEscrituraDejaUnaReplicaVaciaYNoInicializada()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado conCorte = FabricaCatalogo.Crear(dir, "SJ", Remotas, CortarEn(EtapaGuardado.TemporalEscrito));

        Assert.Throws<CorteSimulado>(() => conCorte.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) }));

        Assert.False(conCorte.EstaSincronizada("LIB"));
        CatalogoReplicado reiniciado = FabricaCatalogo.Recargar(dir, "SJ", Remotas);
        Assert.False(reiniciado.EstaSincronizada("LIB"));
        Assert.Empty(reiniciado.ConsultarOfertas());
    }

    [Fact]
    public void UnFalloDeGuardadoNoCambiaLaMemoria()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        // Un archivo ocupa el lugar del directorio de la sede: guardar es imposible.
        string bloqueo = dir.Escribir("bloqueo", "x");
        RutasArchivosInventario rutas = RutasArchivosInventario.EnDirectorio(System.IO.Path.Combine(bloqueo, "sede"), "SJ");
        CatalogoReplicado catalogo = new CargadorCatalogo().Cargar(rutas, Remotas);

        Assert.Throws<DatosInventarioInvalidosException>(() => catalogo.AplicarCambio("LIB", Entrada("CAF", 4, 1)));
        Assert.Throws<DatosInventarioInvalidosException>(() => catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) }));

        Assert.False(catalogo.BuscarOferta("LIB", "CAF", out _));
        Assert.False(catalogo.EstaSincronizada("LIB"));
    }

    // ---- Concurrencia -------------------------------------------------------------------------------------------

    [Fact]
    public async Task VariosHilosConRevisionesDistintasDejanLaMayor()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = Crear(dir);
        Task[] tareas = new Task[16];
        for (int i = 0; i < tareas.Length; i++)
        {
            int revision = i + 1;
            tareas[i] = Task.Run(() => catalogo.AplicarCambio("LIB", Entrada("CAF", revision, revision)));
        }
        await Task.WhenAll(tareas);

        AfirmarOferta(catalogo, "LIB", "CAF", 16, 16);
        AfirmarOferta(FabricaCatalogo.Recargar(dir, "SJ", Remotas), "LIB", "CAF", 16, 16);
    }
}
