using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Negocio;

/// Escritor único de RF02: cambio completo bajo exclusión mutua, guardado atómico y recuperación.
public class EscrituraInventarioTests
{
    private static readonly string[] Remotas = { "SJ" };

    private sealed class CorteSimulado : Exception
    {
    }

    private static StockLocal Stock(string id, int existencia, int reservada) => FabricaInventario.Stock(id, existencia, reservada);

    private static ResultadoStock Resultado(string pedidoId, EstadoReserva estado, string operacion = "stock.reservar") =>
        new ResultadoStock(pedidoId, operacion, estado, CodigosResultado.Ok, "ok");

    /// CAF pasa de 5/0 a 5/2 (RESERVADA): lote, comando y reposición quedan registrados.
    private static void CambioA(CambioInventario cambio)
    {
        cambio.ModificarStock("CAF", 5, 2);
        cambio.CrearLote(new LoteReserva("p-1", "SJ", EstadoReserva.Reservada, new[] { new LineaReserva("CAF", 2, 150000) }));
        cambio.RegistrarComando(new ComandoRegistrado("m-1", "stock.reservar", "SJ", "contenido-a", Resultado("p-1", EstadoReserva.Reservada)));
        cambio.RegistrarReposicion(new ReposicionRegistrada("op-1", "CAF", 2, 5, 2));
    }

    /// CAF pasa de 5/2 a 3/0 (CONFIRMADA).
    private static void CambioB(CambioInventario cambio)
    {
        cambio.ModificarStock("CAF", 3, 0);
        Assert.True(cambio.BuscarLote("p-1", out LoteReserva? lote));
        cambio.ActualizarLote(lote.ConEstado(EstadoReserva.Confirmada));
        cambio.RegistrarComando(new ComandoRegistrado("m-2", "stock.confirmar", "SJ", "contenido-b", Resultado("p-1", EstadoReserva.Confirmada, "stock.confirmar")));
    }

    private static void AfirmarEstadoA(InventarioLocal inventario)
    {
        Assert.True(inventario.BuscarStock("CAF", out StockLocal? stock));
        Assert.Equal(5, stock.Existencia);
        Assert.Equal(2, stock.Reservada);
        Assert.Equal(3, stock.Disponible);
        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(1, revision);
        Assert.True(inventario.BuscarLote("p-1", out LoteReserva? lote));
        Assert.Equal(EstadoReserva.Reservada, lote.Estado);
        Assert.True(inventario.BuscarComando("m-1", out ComandoRegistrado? comando));
        Assert.Equal("contenido-a", comando.Contenido);
        Assert.False(inventario.BuscarComando("m-2", out _));
        Assert.True(inventario.BuscarReposicion("op-1", out ReposicionRegistrada? reposicion));
        Assert.Equal(2, reposicion.Cantidad);
        SalidaPendiente salida = Assert.Single(inventario.ConsultarSalidasPendientes());
        Assert.Equal("SJ", salida.DestinoSedeId);
        Assert.Equal(3, salida.Disponible);
        Assert.Equal(1, salida.Revision);
    }

    private static void AfirmarEstadoB(InventarioLocal inventario)
    {
        Assert.True(inventario.BuscarStock("CAF", out StockLocal? stock));
        Assert.Equal(3, stock.Existencia);
        Assert.Equal(0, stock.Reservada);
        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(2, revision);
        Assert.True(inventario.BuscarLote("p-1", out LoteReserva? lote));
        Assert.Equal(EstadoReserva.Confirmada, lote.Estado);
        Assert.True(inventario.BuscarComando("m-1", out _));
        Assert.True(inventario.BuscarComando("m-2", out _));
        Assert.True(inventario.BuscarReposicion("op-1", out _));
        SalidaPendiente salida = Assert.Single(inventario.ConsultarSalidasPendientes());
        Assert.Equal(3, salida.Disponible);
        Assert.Equal(2, salida.Revision);
    }

    // ---- Cambio completo ----

    [Fact]
    public void Aplicar_CambioCompleto_QuedaEnMemoriaYEnDisco()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0) });

        inventario.Aplicar(CambioA);

        AfirmarEstadoA(inventario);
        AfirmarEstadoA(FabricaInventario.Recargar(dir, "LIB", Remotas));
        Assert.False(dir.Existe(RutasArchivosInventario.NombreEstado + ".tmp"));
    }

    [Fact]
    public void Aplicar_ParaCadaOtraSedeGeneraUnaSalidaConDisponibleYRevisionAbsolutos()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        string[] remotas = { "SJ", "ALA" };
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", remotas, null, new[] { Stock("CAF", 5, 0), Stock("ARR", 4, 0) });

        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 2));

        SalidaPendiente[] salidas = inventario.ConsultarSalidasPendientes();
        Assert.Equal(2, salidas.Length);
        Assert.Equal("ALA", salidas[0].DestinoSedeId); // ordenadas por destino y producto
        Assert.Equal("SJ", salidas[1].DestinoSedeId);
        Assert.All(salidas, s =>
        {
            Assert.Equal("CAF", s.ProductoId);
            Assert.Equal(3, s.Disponible);
            Assert.Equal(1, s.Revision);
        });
        Assert.NotEqual(salidas[0].MensajeId, salidas[1].MensajeId);
        Assert.True(Guid.TryParse(salidas[0].MensajeId, out _));
        // ARR no cambió: ni salida ni revisión.
        Assert.True(inventario.BuscarRevision("ARR", out long revisionArr));
        Assert.Equal(0, revisionArr);
    }

    [Fact]
    public void Aplicar_VariasModificacionesDelMismoProducto_SubenLaRevisionUnaSolaVez()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0), Stock("ARR", 4, 0) });

        inventario.Aplicar(cambio =>
        {
            cambio.ModificarStock("CAF", 5, 1);
            cambio.ModificarStock("CAF", 5, 2);
            cambio.ModificarStock("ARR", 4, 1);
        });

        Assert.True(inventario.BuscarRevision("CAF", out long revisionCaf));
        Assert.True(inventario.BuscarRevision("ARR", out long revisionArr));
        Assert.Equal(1, revisionCaf);
        Assert.Equal(1, revisionArr);
        Assert.True(inventario.BuscarStock("CAF", out StockLocal? caf));
        Assert.Equal(2, caf.Reservada);
        Assert.Equal(2, inventario.ConsultarSalidasPendientes().Length);
    }

    [Fact]
    public void Aplicar_ConfirmarSinCambiarElDisponible_IgualSubeLaRevision()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0) });
        inventario.Aplicar(CambioA);

        inventario.Aplicar(CambioB);

        AfirmarEstadoB(inventario);
        AfirmarEstadoB(FabricaInventario.Recargar(dir, "LIB", Remotas));
    }

    [Fact]
    public void Aplicar_UnaOperacionVeSusPropiasEscrituras()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0) });

        inventario.Aplicar(cambio =>
        {
            cambio.ModificarStock("CAF", 5, 2);
            Assert.True(cambio.BuscarStock("CAF", out StockLocal? preparado));
            Assert.Equal(3, preparado.Disponible);
            Assert.True(inventario.BuscarStock("CAF", out _)); // el estado vivo sigue siendo el anterior
            cambio.CrearLote(new LoteReserva("p-1", "SJ", EstadoReserva.Liberada, Array.Empty<LineaReserva>()));
            Assert.True(cambio.BuscarLote("p-1", out _));
            Assert.False(cambio.BuscarLote("p-2", out _));
        });

        Assert.True(inventario.BuscarLote("p-1", out LoteReserva? lote));
        Assert.Equal(EstadoReserva.Liberada, lote.Estado);
    }

    [Fact]
    public void Aplicar_DevuelveElValorDeLaOperacionDespuesDeGuardar()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0) });

        int disponible = inventario.Aplicar(cambio =>
        {
            cambio.ModificarStock("CAF", 5, 2);
            cambio.BuscarStock("CAF", out StockLocal? stock);
            return stock!.Disponible;
        });

        Assert.Equal(3, disponible);
    }

    [Fact]
    public void Aplicar_SoloRegistraUnRechazo_NoCambiaCantidadesNiRevisionNiSalidas()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 1, 0) });

        inventario.Aplicar(cambio =>
        {
            cambio.CrearLote(new LoteReserva("p-1", "SJ", EstadoReserva.Rechazada, Array.Empty<LineaReserva>()));
            cambio.RegistrarComando(new ComandoRegistrado("m-1", "stock.reservar", "SJ", "c",
                new ResultadoStock("p-1", "stock.reservar", EstadoReserva.Rechazada, CodigosResultado.StockInsuficiente, "agotado")));
        });

        InventarioLocal recargado = FabricaInventario.Recargar(dir, "LIB", Remotas);
        Assert.True(recargado.BuscarComando("m-1", out ComandoRegistrado? comando));
        Assert.Equal(CodigosResultado.StockInsuficiente, comando.Resultado.Codigo);
        Assert.True(recargado.BuscarLote("p-1", out _));
        Assert.True(recargado.BuscarRevision("CAF", out long revision));
        Assert.Equal(0, revision);
        Assert.Empty(recargado.ConsultarSalidasPendientes());
    }

    [Fact]
    public void Aplicar_SinSedesRemotas_NoGeneraSalidasPeroSiSubeLaRevision()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.Crear(dir, "LIB", Stock("CAF", 5, 0));

        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 7, 0));

        Assert.Empty(inventario.ConsultarSalidasPendientes());
        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(1, revision);
    }

    [Fact]
    public void Aplicar_DosCambiosSeguidosDelMismoProducto_ConservanUnaSolaSalidaPorDestino()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0) });
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        SalidaPendiente primera = Assert.Single(inventario.ConsultarSalidasPendientes());

        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 9, 1));

        SalidaPendiente segunda = Assert.Single(inventario.ConsultarSalidasPendientes());
        Assert.Equal(2, segunda.Revision);
        Assert.Equal(8, segunda.Disponible);
        Assert.NotEqual(primera.MensajeId, segunda.MensajeId);
        SalidaPendiente recargada = Assert.Single(FabricaInventario.Recargar(dir, "LIB", Remotas).ConsultarSalidasPendientes());
        Assert.Equal(segunda.MensajeId, recargada.MensajeId);
    }

    // ---- Operaciones que fallan ----

    [Fact]
    public void Aplicar_SinCambios_NoReescribeElArchivoNiSubeRevision()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0) });
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);

        bool encontrado = inventario.Aplicar(cambio => cambio.BuscarStock("CAF", out _));

        Assert.True(encontrado);
        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(0, revision);
    }

    [Fact]
    public void Aplicar_LaOperacionLanza_NadaCambiaEnMemoriaNiEnDisco()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0) });
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);

        Assert.Throws<InvalidOperationException>(() => inventario.Aplicar(cambio =>
        {
            CambioA(cambio);
            throw new InvalidOperationException("falla a mitad");
        }));

        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
        Assert.True(inventario.BuscarStock("CAF", out StockLocal? stock));
        Assert.Equal(0, stock.Reservada);
        Assert.False(inventario.BuscarLote("p-1", out _));
        Assert.False(inventario.BuscarComando("m-1", out _));
        Assert.Empty(inventario.ConsultarSalidasPendientes());
    }

    [Fact]
    public void ModificarStock_ProductoDesconocidoOCantidadesInvalidas_LanzaYNoDejaNada()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0) });
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);

        ProductoDesconocidoException desconocido = Assert.Throws<ProductoDesconocidoException>(
            () => inventario.Aplicar(cambio => cambio.ModificarStock("XYZ", 1, 0)));
        Assert.Equal("XYZ", desconocido.ProductoId);
        Assert.Throws<ProductoDesconocidoException>(() => inventario.Aplicar(cambio => cambio.ModificarStock("caf", 1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 6)));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventario.Aplicar(cambio => cambio.ModificarStock("CAF", -1, 0)));

        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
    }

    [Fact]
    public void Registros_ClaveRepetida_LanzaYElCambioCompletoSeDescarta()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0) });
        inventario.Aplicar(CambioA);
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);
        ComandoRegistrado repetido = new ComandoRegistrado("m-1", "stock.reservar", "SJ", "otro", Resultado("p-1", EstadoReserva.Reservada));

        // Contra lo ya guardado y contra lo preparado en el mismo cambio.
        Assert.Throws<InvalidOperationException>(() => inventario.Aplicar(cambio =>
        {
            cambio.ModificarStock("CAF", 9, 0);
            cambio.RegistrarComando(repetido);
        }));
        Assert.Throws<InvalidOperationException>(() => inventario.Aplicar(cambio =>
        {
            cambio.RegistrarComando(new ComandoRegistrado("m-9", "t", "SJ", "c", Resultado("p-9", EstadoReserva.Reservada)));
            cambio.RegistrarComando(new ComandoRegistrado("m-9", "t", "SJ", "c", Resultado("p-9", EstadoReserva.Reservada)));
        }));
        Assert.Throws<InvalidOperationException>(() => inventario.Aplicar(cambio =>
            cambio.RegistrarReposicion(new ReposicionRegistrada("op-1", "CAF", 1, 5, 2))));
        Assert.Throws<InvalidOperationException>(() => inventario.Aplicar(cambio =>
            cambio.CrearLote(new LoteReserva("p-1", "SJ", EstadoReserva.Liberada, Array.Empty<LineaReserva>()))));

        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
        AfirmarEstadoA(inventario);
    }

    [Fact]
    public void ActualizarLote_SinLoteExistente_Lanza()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0) });

        Assert.Throws<InvalidOperationException>(() => inventario.Aplicar(cambio =>
            cambio.ActualizarLote(new LoteReserva("p-1", "SJ", EstadoReserva.Liberada, Array.Empty<LineaReserva>()))));
    }

    [Fact]
    public void Aplicar_ArgumentosNulos_Lanza()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.Crear(dir, "LIB", Stock("CAF", 5, 0));

        Assert.Throws<ArgumentNullException>(() => inventario.Aplicar((Func<CambioInventario, int>)null!));
        Assert.Throws<ArgumentNullException>(() => inventario.Aplicar((Action<CambioInventario>)null!));
        Assert.Throws<ArgumentNullException>(() => inventario.Aplicar(cambio => cambio.CrearLote(null!)));
        Assert.Throws<ArgumentNullException>(() => inventario.Aplicar(cambio => cambio.RegistrarComando(null!)));
        Assert.Throws<ArgumentNullException>(() => inventario.Aplicar(cambio => cambio.RegistrarReposicion(null!)));
        Assert.Throws<ArgumentNullException>(() => inventario.Aplicar(cambio => cambio.ModificarStock(null!, 1, 0)));
    }

    // ---- Interrupciones durante el guardado (RF02-escritura) ----

    private static (InventarioLocal Inventario, Action<EtapaGuardado?> Armar) ConHook(DirectorioTemporal dir)
    {
        EtapaGuardado? objetivo = null;
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"), etapa =>
        {
            if (objetivo == etapa)
            {
                throw new CorteSimulado();
            }
        });
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, repositorio, new[] { Stock("CAF", 5, 0) });
        return (inventario, etapa => objetivo = etapa);
    }

    [Fact]
    public void CorteAntesDelReemplazo_SeRecuperaElEstadoAnteriorCompleto()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        (InventarioLocal inventario, Action<EtapaGuardado?> armar) = ConHook(dir);
        inventario.Aplicar(CambioA);
        string snapshotA = dir.Leer(RutasArchivosInventario.NombreEstado);

        armar(EtapaGuardado.TemporalEscrito);
        Assert.Throws<CorteSimulado>(() => inventario.Aplicar(CambioB));

        // El temporal con el estado B quedó completo pero sin reemplazar al definitivo.
        Assert.True(dir.Existe(RutasArchivosInventario.NombreEstado + ".tmp"));
        Assert.Equal(snapshotA, dir.Leer(RutasArchivosInventario.NombreEstado));
        AfirmarEstadoA(inventario); // la memoria tampoco instaló B
        AfirmarEstadoA(FabricaInventario.Recargar(dir, "LIB", Remotas));
    }

    [Fact]
    public void CorteDespuesDelReemplazo_SeRecuperaElEstadoNuevoCompleto()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        (InventarioLocal inventario, Action<EtapaGuardado?> armar) = ConHook(dir);
        inventario.Aplicar(CambioA);

        armar(EtapaGuardado.Reemplazado);
        Assert.Throws<CorteSimulado>(() => inventario.Aplicar(CambioB));

        // El proceso "murió": solo cuenta lo que hay en disco, y es el estado B completo.
        Assert.False(dir.Existe(RutasArchivosInventario.NombreEstado + ".tmp"));
        AfirmarEstadoB(FabricaInventario.Recargar(dir, "LIB", Remotas));
    }

    [Fact]
    public void TrasUnCorteAntesDelReemplazo_ElSiguienteGuardadoSobrescribeElTemporalSobrante()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        (InventarioLocal inventario, Action<EtapaGuardado?> armar) = ConHook(dir);
        inventario.Aplicar(CambioA);
        armar(EtapaGuardado.TemporalEscrito);
        Assert.Throws<CorteSimulado>(() => inventario.Aplicar(CambioB));

        InventarioLocal reiniciado = FabricaInventario.Recargar(dir, "LIB", Remotas);
        reiniciado.Aplicar(CambioB);

        Assert.False(dir.Existe(RutasArchivosInventario.NombreEstado + ".tmp"));
        AfirmarEstadoB(reiniciado);
        AfirmarEstadoB(FabricaInventario.Recargar(dir, "LIB", Remotas));
    }

    [Fact]
    public void TemporalCorruptoSobrante_SeIgnoraAlRecuperar()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0) });
        inventario.Aplicar(CambioA);
        // Corte durante la escritura del temporal: quedó a medias, el definitivo no se tocó.
        dir.Escribir(RutasArchivosInventario.NombreEstado + ".tmp", "{ \"version\": 1, \"sedeId\": \"LI");

        InventarioLocal reiniciado = FabricaInventario.Recargar(dir, "LIB", Remotas);

        AfirmarEstadoA(reiniciado);
        reiniciado.Aplicar(CambioB);
        Assert.False(dir.Existe(RutasArchivosInventario.NombreEstado + ".tmp"));
        AfirmarEstadoB(FabricaInventario.Recargar(dir, "LIB", Remotas));
    }

    [Fact]
    public void FalloDeEscrituraEnDisco_LanzaLimpiaElTemporalYConservaElEstadoAnterior()
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
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, repositorio, new[] { Stock("CAF", 5, 0) });
        inventario.Aplicar(CambioA);
        string snapshotA = dir.Leer(RutasArchivosInventario.NombreEstado);

        fallar = true;
        DatosInventarioInvalidosException error = Assert.Throws<DatosInventarioInvalidosException>(() => inventario.Aplicar(CambioB));

        Assert.Contains("disco lleno", error.Detalle);
        Assert.False(dir.Existe(RutasArchivosInventario.NombreEstado + ".tmp"));
        Assert.Equal(snapshotA, dir.Leer(RutasArchivosInventario.NombreEstado));
        AfirmarEstadoA(inventario);

        fallar = false;
        inventario.Aplicar(CambioB); // se recupera sin reiniciar
        AfirmarEstadoB(inventario);
    }

    // ---- Concurrencia ----

    [Fact]
    public async Task Aplicar_ConcurrenteDesdeVariosHilos_NoPierdeNingunCambio()
    {
        const int hilos = 8;
        const int incrementosPorHilo = 50;
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 1000, 0) });
        bool terminado = false;
        Exception? falloLector = null;

        Task lector = Task.Run(() =>
        {
            try
            {
                while (!Volatile.Read(ref terminado))
                {
                    StockLocal[] vista = inventario.ConsultarInventario();
                    Assert.Equal(vista[0].Existencia - vista[0].Reservada, vista[0].Disponible);
                    inventario.ConsultarSalidasPendientes();
                    inventario.BuscarRevision("CAF", out _);
                }
            }
            catch (Exception e)
            {
                falloLector = e;
            }
        });

        Task[] escritores = new Task[hilos];
        for (int h = 0; h < hilos; h++)
        {
            escritores[h] = Task.Run(() =>
            {
                for (int i = 0; i < incrementosPorHilo; i++)
                {
                    inventario.Aplicar(cambio =>
                    {
                        cambio.BuscarStock("CAF", out StockLocal? actual);
                        cambio.ModificarStock("CAF", actual!.Existencia, actual.Reservada + 1);
                    });
                }
            });
        }
        await Task.WhenAll(escritores);
        Volatile.Write(ref terminado, true);
        await lector;

        Assert.Null(falloLector);
        const int total = hilos * incrementosPorHilo;
        Assert.True(inventario.BuscarStock("CAF", out StockLocal? final));
        Assert.Equal(total, final.Reservada);
        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(total, revision);

        InventarioLocal recargado = FabricaInventario.Recargar(dir, "LIB", Remotas);
        Assert.True(recargado.BuscarStock("CAF", out StockLocal? enDisco));
        Assert.Equal(total, enDisco.Reservada);
        Assert.True(recargado.BuscarRevision("CAF", out long revisionEnDisco));
        Assert.Equal(total, revisionEnDisco);
        Assert.Equal(total, Assert.Single(recargado.ConsultarSalidasPendientes()).Revision);
    }

    [Fact]
    public async Task Aplicar_DosSolicitudesConcurrentesDe4Unidades_ConDisponible5_SoloUnaSeAcepta()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, new[] { Stock("CAF", 5, 0) });
        bool[] aceptadas = new bool[2];

        Task[] tareas = new Task[2];
        for (int t = 0; t < 2; t++)
        {
            int indice = t;
            tareas[t] = Task.Run(() => aceptadas[indice] = inventario.Aplicar(cambio =>
            {
                cambio.BuscarStock("CAF", out StockLocal? actual);
                if (actual!.Disponible < 4)
                {
                    return false;
                }
                cambio.ModificarStock("CAF", actual.Existencia, actual.Reservada + 4);
                return true;
            }));
        }
        await Task.WhenAll(tareas);

        Assert.Equal(1, (aceptadas[0] ? 1 : 0) + (aceptadas[1] ? 1 : 0));
        Assert.True(inventario.BuscarStock("CAF", out StockLocal? stock));
        Assert.Equal(1, stock.Disponible);
    }
}
