using System;
using System.IO;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Negocio;

/// Retiro de las publicaciones de catálogo ya entregadas al broker (RF03, sincronización).
public class RetiroSalidasTests
{
    private static readonly string[] Remotas = { "ALA", "SJ" };

    private static InventarioLocal Crear(DirectorioTemporal dir, RepositorioInventario? repositorio = null) =>
        FabricaInventario.CrearCon(
            dir, "LIB", Remotas, repositorio,
            new[] { FabricaInventario.Stock("CAF", 5, 0), FabricaInventario.Stock("ARR", 4, 0) });

    [Fact]
    public void RetirarSalidas_TodasLasEnviadas_DejaLaListaVaciaTambienTrasRecargar()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        SalidaPendiente[] pendientes = inventario.ConsultarSalidasPendientes();
        Assert.Equal(2, pendientes.Length);

        int retiradas = inventario.RetirarSalidas(pendientes);

        Assert.Equal(2, retiradas);
        Assert.Empty(inventario.ConsultarSalidasPendientes());
        Assert.Empty(FabricaInventario.Recargar(dir, "LIB", Remotas).ConsultarSalidasPendientes());
    }

    [Fact]
    public void RetirarSalidas_SoloUnDestino_ConservaLasDemasEnDisco()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        SalidaPendiente[] pendientes = inventario.ConsultarSalidasPendientes();
        SalidaPendiente haciaAla = pendientes[0].DestinoSedeId == "ALA" ? pendientes[0] : pendientes[1];

        Assert.Equal(1, inventario.RetirarSalidas(new[] { haciaAla }));

        SalidaPendiente restante = Assert.Single(inventario.ConsultarSalidasPendientes());
        Assert.Equal("SJ", restante.DestinoSedeId);
        SalidaPendiente recargada = Assert.Single(FabricaInventario.Recargar(dir, "LIB", Remotas).ConsultarSalidasPendientes());
        Assert.Equal(restante.MensajeId, recargada.MensajeId);
    }

    [Fact]
    public void RetirarSalidas_SalidaYaSustituidaPorUnCambioPosterior_NoQuitaLaNueva()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        SalidaPendiente[] viejas = inventario.ConsultarSalidasPendientes();
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 9, 1));

        int retiradas = inventario.RetirarSalidas(viejas);

        Assert.Equal(0, retiradas);
        SalidaPendiente[] vigentes = inventario.ConsultarSalidasPendientes();
        Assert.Equal(2, vigentes.Length);
        Assert.Equal(2L, vigentes[0].Revision);
        Assert.Equal(8, vigentes[0].Disponible);
    }

    [Fact]
    public void RetirarSalidas_ArregloVacioOSalidaDesconocida_NoReescribeElArchivo()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);
        SalidaPendiente ajena = new SalidaPendiente(Guid.NewGuid().ToString(), "ALA", "XYZ", 1, 1);

        Assert.Equal(0, inventario.RetirarSalidas(Array.Empty<SalidaPendiente>()));
        Assert.Equal(0, inventario.RetirarSalidas(new[] { ajena }));

        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
        Assert.Equal(2, inventario.ConsultarSalidasPendientes().Length);
    }

    [Fact]
    public void RetirarSalidas_Repetido_LaSegundaVezNoRetiraNada()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        SalidaPendiente[] pendientes = inventario.ConsultarSalidasPendientes();

        Assert.Equal(2, inventario.RetirarSalidas(pendientes));
        Assert.Equal(0, inventario.RetirarSalidas(pendientes));
    }

    [Fact]
    public void RetirarSalidas_NoToca_Cantidades_NiRevisiones()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));

        inventario.RetirarSalidas(inventario.ConsultarSalidasPendientes());

        Assert.True(inventario.BuscarStock("CAF", out StockLocal? stock));
        Assert.Equal(1, stock.Reservada);
        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(1, revision);
        Assert.True(inventario.BuscarRevision("ARR", out long revisionArr));
        Assert.Equal(0, revisionArr);
    }

    [Fact]
    public void RetirarSalidas_ConLaListaNula_Lanza()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir);

        Assert.Throws<ArgumentNullException>(() => inventario.RetirarSalidas(null!));
    }

    [Fact]
    public void RetirarSalidas_FallaElGuardado_LaSalidaSigueEnMemoriaYEnDisco()
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
        InventarioLocal inventario = Crear(dir, repositorio);
        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        SalidaPendiente[] pendientes = inventario.ConsultarSalidasPendientes();
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);
        fallar = true;

        Assert.ThrowsAny<Exception>(() => inventario.RetirarSalidas(pendientes));

        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
        Assert.Equal(2, inventario.ConsultarSalidasPendientes().Length);
    }

    [Fact]
    public void Aplicar_ConSedesRemotas_DisparaSalidasRegistradasUnaVezPorCambio()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir);
        int avisos = 0;
        inventario.SalidasRegistradas += () => avisos++;

        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));
        inventario.Aplicar(cambio => cambio.BuscarStock("CAF", out _)); // sin cambios: no avisa

        Assert.Equal(1, avisos);
    }

    [Fact]
    public void Aplicar_SinSedesRemotas_NoDisparaSalidasRegistradas()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.Crear(dir, "LIB", FabricaInventario.Stock("CAF", 5, 0));
        int avisos = 0;
        inventario.SalidasRegistradas += () => avisos++;

        inventario.Aplicar(cambio => cambio.ModificarStock("CAF", 5, 1));

        Assert.Equal(0, avisos);
    }
}
