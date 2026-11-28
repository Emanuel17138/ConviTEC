using System;
using System.Threading.Tasks;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Negocio;

/// Regla de la reposición (RF02): validación, idempotencia por operacionId, revisión y publicaciones pendientes.
public class ServicioReposicionesTests
{
    private static readonly string[] Remotas = { "SJ", "ALA" };

    private const string Op1 = "20000000-0000-4000-8000-000000000001";
    private const string Op2 = "20000000-0000-4000-8000-000000000002";

    private static StockLocal Stock(string id, int existencia, int reservada) => FabricaInventario.Stock(id, existencia, reservada);

    private static InventarioLocal Crear(DirectorioTemporal dir, params StockLocal[] stocks) =>
        FabricaInventario.CrearCon(dir, "LIB", Remotas, null, stocks);

    private static void AfirmarCantidades(StockLocal? stock, int existencia, int reservada, int disponible)
    {
        Assert.NotNull(stock);
        Assert.Equal(existencia, stock.Existencia);
        Assert.Equal(reservada, stock.Reservada);
        Assert.Equal(disponible, stock.Disponible);
    }

    // ---- Aceptación de RF02 ----

    [Fact]
    public void Reponer_CasoDeAceptacion_RepeticionDevuelveElResultadoGuardado()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", 5, 2));
        ServicioReposiciones servicio = new ServicioReposiciones(inventario);

        ResultadoReposicion primera = servicio.Reponer("CAF", Op1, 3);
        ResultadoReposicion segunda = servicio.Reponer("CAF", Op2, 1);
        ResultadoReposicion repetida = servicio.Reponer("CAF", Op1, 3);

        Assert.Equal(EstadoReposicion.Aplicada, primera.Estado);
        AfirmarCantidades(primera.Stock, 8, 2, 6);
        Assert.Equal(EstadoReposicion.Aplicada, segunda.Estado);
        AfirmarCantidades(segunda.Stock, 9, 2, 7);
        Assert.Equal(EstadoReposicion.Repetida, repetida.Estado);
        Assert.True(repetida.EsExito);
        AfirmarCantidades(repetida.Stock, 8, 2, 6);
        Assert.Equal("CAF", repetida.Stock!.Producto.ProductoId);

        Assert.True(inventario.BuscarStock("CAF", out StockLocal? actual));
        AfirmarCantidades(actual, 9, 2, 7);
        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(2, revision);
    }

    [Fact]
    public void Reponer_MismaClaveConOtraCantidadOtroProducto_EsClaveReutilizadaSinCambios()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", 5, 0), Stock("ARR", 4, 0));
        ServicioReposiciones servicio = new ServicioReposiciones(inventario);
        servicio.Reponer("CAF", Op1, 3);
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);

        ResultadoReposicion otraCantidad = servicio.Reponer("CAF", Op1, 4);
        ResultadoReposicion otroProducto = servicio.Reponer("ARR", Op1, 3);

        Assert.Equal(EstadoReposicion.ClaveReutilizada, otraCantidad.Estado);
        Assert.Equal(CodigosReposicion.ClaveReutilizada, otraCantidad.Codigo);
        Assert.False(otraCantidad.EsExito);
        Assert.Null(otraCantidad.Stock);
        Assert.Equal(EstadoReposicion.ClaveReutilizada, otroProducto.Estado);
        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
        Assert.True(inventario.BuscarStock("ARR", out StockLocal? arroz));
        Assert.Equal(4, arroz.Existencia);
    }

    // ---- Validación: nada cambia ----

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Reponer_CantidadNoPositiva_EsCantidadInvalidaYNoCambiaNada(int cantidad)
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", 5, 2));
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);

        ResultadoReposicion resultado = new ServicioReposiciones(inventario).Reponer("CAF", Op1, cantidad);

        Assert.Equal(EstadoReposicion.CantidadInvalida, resultado.Estado);
        Assert.Equal("CANTIDAD_INVALIDA", resultado.Codigo);
        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
        Assert.False(inventario.BuscarReposicion(Op1, out _));
    }

    [Fact]
    public void Reponer_CantidadAusente_EsCantidadInvalida()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", 5, 2));

        ResultadoReposicion resultado = new ServicioReposiciones(inventario).Reponer("CAF", Op1, null);

        Assert.Equal(EstadoReposicion.CantidadInvalida, resultado.Estado);
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("caf")]
    [InlineData("")]
    public void Reponer_ProductoDesconocido_EsProductoDesconocidoYNoRegistraLaClave(string productoId)
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", 5, 2));
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);

        ResultadoReposicion resultado = new ServicioReposiciones(inventario).Reponer(productoId, Op1, 3);

        Assert.Equal(EstadoReposicion.ProductoDesconocido, resultado.Estado);
        Assert.Equal("PRODUCTO_DESCONOCIDO", resultado.Codigo);
        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
        Assert.False(inventario.BuscarReposicion(Op1, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("op-1")]
    public void Reponer_OperacionIdAusenteOQueNoEsUuid_EsDatosInvalidos(string? operacionId)
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", 5, 2));
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);

        ResultadoReposicion resultado = new ServicioReposiciones(inventario).Reponer("CAF", operacionId, 3);

        Assert.Equal(EstadoReposicion.DatosInvalidos, resultado.Estado);
        Assert.Equal("DATOS_INVALIDOS", resultado.Codigo);
        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
    }

    [Fact]
    public void Reponer_QueDesbordaLaExistencia_EsCantidadInvalidaYNoCambiaNada()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", int.MaxValue - 1, 0));
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);

        ResultadoReposicion resultado = new ServicioReposiciones(inventario).Reponer("CAF", Op1, 2);

        Assert.Equal(EstadoReposicion.CantidadInvalida, resultado.Estado);
        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
        ResultadoReposicion alLimite = new ServicioReposiciones(inventario).Reponer("CAF", Op2, 1);
        Assert.Equal(EstadoReposicion.Aplicada, alLimite.Estado);
        AfirmarCantidades(alLimite.Stock, int.MaxValue, 0, int.MaxValue);
    }

    [Fact]
    public void Reponer_ArgumentoNulo_Lanza()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", 5, 0));

        Assert.Throws<ArgumentNullException>(() => new ServicioReposiciones(null!));
        Assert.Throws<ArgumentNullException>(() => new ServicioReposiciones(inventario).Reponer(null!, Op1, 1));
    }

    // ---- Revisión y publicaciones pendientes ----

    [Fact]
    public void Reponer_SubeLaRevisionYDejaUnaPublicacionPendienteParaCadaOtraSede()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", 5, 2), Stock("ARR", 4, 0));

        new ServicioReposiciones(inventario).Reponer("CAF", Op1, 3);

        SalidaPendiente[] salidas = inventario.ConsultarSalidasPendientes();
        Assert.Equal(2, salidas.Length);
        Assert.Equal("ALA", salidas[0].DestinoSedeId);
        Assert.Equal("SJ", salidas[1].DestinoSedeId);
        Assert.All(salidas, s =>
        {
            Assert.Equal("CAF", s.ProductoId);
            Assert.Equal(6, s.Disponible);
            Assert.Equal(1, s.Revision);
        });
        Assert.True(inventario.BuscarRevision("ARR", out long revisionArroz));
        Assert.Equal(0, revisionArroz);
    }

    [Fact]
    public void Reponer_Repetida_NoSubeLaRevisionNiReescribeNiCambiaLasSalidas()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", 5, 2));
        ServicioReposiciones servicio = new ServicioReposiciones(inventario);
        servicio.Reponer("CAF", Op1, 3);
        string antes = dir.Leer(RutasArchivosInventario.NombreEstado);
        SalidaPendiente[] salidasAntes = inventario.ConsultarSalidasPendientes();

        servicio.Reponer("CAF", Op1, 3);

        Assert.Equal(antes, dir.Leer(RutasArchivosInventario.NombreEstado));
        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(1, revision);
        SalidaPendiente[] salidasDespues = inventario.ConsultarSalidasPendientes();
        Assert.Equal(salidasAntes.Length, salidasDespues.Length);
        Assert.Equal(salidasAntes[0].MensajeId, salidasDespues[0].MensajeId);
    }

    // ---- Persistencia ----

    [Fact]
    public void Reponer_TrasReiniciar_ElEfectoYLaClaveSobreviven()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", 5, 2));
        new ServicioReposiciones(inventario).Reponer("CAF", Op1, 3);
        new ServicioReposiciones(inventario).Reponer("CAF", Op2, 1);

        InventarioLocal reiniciado = FabricaInventario.Recargar(dir, "LIB", Remotas);
        ServicioReposiciones servicio = new ServicioReposiciones(reiniciado);
        ResultadoReposicion repetida = servicio.Reponer("CAF", Op1, 3);
        ResultadoReposicion conflicto = servicio.Reponer("CAF", Op1, 9);

        Assert.Equal(EstadoReposicion.Repetida, repetida.Estado);
        AfirmarCantidades(repetida.Stock, 8, 2, 6);
        Assert.Equal(EstadoReposicion.ClaveReutilizada, conflicto.Estado);
        Assert.True(reiniciado.BuscarStock("CAF", out StockLocal? actual));
        AfirmarCantidades(actual, 9, 2, 7);
        Assert.True(reiniciado.BuscarRevision("CAF", out long revision));
        Assert.Equal(2, revision);
    }

    [Fact]
    public void Reponer_FalloDeDisco_LanzaYNoDejaElEfectoNiLaClave()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        bool fallar = false;
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"), etapa =>
        {
            if (fallar && etapa == EtapaGuardado.TemporalEscrito)
            {
                throw new System.IO.IOException("disco lleno");
            }
        });
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, repositorio, new[] { Stock("CAF", 5, 2) });
        ServicioReposiciones servicio = new ServicioReposiciones(inventario);

        fallar = true;
        Assert.Throws<DatosInventarioInvalidosException>(() => servicio.Reponer("CAF", Op1, 3));

        Assert.True(inventario.BuscarStock("CAF", out StockLocal? intacto));
        AfirmarCantidades(intacto, 5, 2, 3);
        Assert.False(inventario.BuscarReposicion(Op1, out _));

        fallar = false;
        ResultadoReposicion reintento = servicio.Reponer("CAF", Op1, 3); // el cliente reintenta con la misma clave
        Assert.Equal(EstadoReposicion.Aplicada, reintento.Estado);
        AfirmarCantidades(reintento.Stock, 8, 2, 6);
    }

    // ---- Concurrencia ----

    [Fact]
    public async Task Reponer_MismaClaveDesdeVariosHilos_SeAplicaUnaSolaVez()
    {
        const int hilos = 8;
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", 5, 0));
        ServicioReposiciones servicio = new ServicioReposiciones(inventario);
        ResultadoReposicion[] resultados = new ResultadoReposicion[hilos];

        Task[] tareas = new Task[hilos];
        for (int h = 0; h < hilos; h++)
        {
            int indice = h;
            tareas[h] = Task.Run(() => resultados[indice] = servicio.Reponer("CAF", Op1, 3));
        }
        await Task.WhenAll(tareas);

        int aplicadas = 0;
        for (int h = 0; h < hilos; h++)
        {
            Assert.True(resultados[h].EsExito);
            AfirmarCantidades(resultados[h].Stock, 8, 0, 8);
            if (resultados[h].Estado == EstadoReposicion.Aplicada)
            {
                aplicadas++;
            }
        }
        Assert.Equal(1, aplicadas);
        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(1, revision);
    }

    [Fact]
    public async Task Reponer_ClavesDistintasDesdeVariosHilos_SumaExactoYLaRevisionCuenta()
    {
        const int hilos = 8;
        const int repeticiones = 25;
        using DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = Crear(dir, Stock("CAF", 0, 0));
        ServicioReposiciones servicio = new ServicioReposiciones(inventario);

        Task[] tareas = new Task[hilos];
        for (int h = 0; h < hilos; h++)
        {
            tareas[h] = Task.Run(() =>
            {
                for (int i = 0; i < repeticiones; i++)
                {
                    Assert.Equal(EstadoReposicion.Aplicada, servicio.Reponer("CAF", Guid.NewGuid().ToString(), 2).Estado);
                }
            });
        }
        await Task.WhenAll(tareas);

        const int total = hilos * repeticiones;
        Assert.True(inventario.BuscarStock("CAF", out StockLocal? final));
        Assert.Equal(total * 2, final.Existencia);
        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(total, revision);
        Assert.Equal(total * 2, FabricaInventario.Recargar(dir, "LIB", Remotas).ConsultarInventario()[0].Existencia);
    }
}
