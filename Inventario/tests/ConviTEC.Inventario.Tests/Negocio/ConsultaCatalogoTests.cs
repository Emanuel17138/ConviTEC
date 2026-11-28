using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Negocio;

/// Vista global del catálogo (RF03 · rf03-consulta): combina la oferta propia con la réplica, ordenada por
/// productoId y luego sedeId, con 503 lógico mientras falte una primera instantánea. Se prueba directamente sobre
/// réplicas armadas a mano, sin broker.
public class ConsultaCatalogoTests
{
    private static readonly string[] Remotas = { "LIB", "ALA", "LIM" };

    private static EntradaCatalogo Entrada(string productoId, int disponible, long revision) =>
        FabricaCatalogo.Entrada(productoId, disponible, revision);

    private static ConsultaCatalogo Armar(
        DirectorioTemporal dir, string[] remotas, out InventarioLocal inventario, out CatalogoReplicado catalogo,
        params StockLocal[] stocks)
    {
        inventario = FabricaInventario.CrearCon(dir, "SJ", remotas, null, stocks);
        catalogo = FabricaCatalogo.Crear(dir, "SJ", remotas);
        return new ConsultaCatalogo(inventario, catalogo);
    }

    private static void AfirmarOferta(Oferta oferta, string sedeId, int disponible, long revision)
    {
        Assert.Equal(sedeId, oferta.SedeId);
        Assert.Equal(disponible, oferta.Disponible);
        Assert.Equal(revision, oferta.Revision);
    }

    // ---- Inicialización ----------------------------------------------------------------------------------------

    [Fact]
    public void SinSedesRemotasEstaInicializadaDeInmediatoYMuestraSoloLaOfertaPropia()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        ConsultaCatalogo consulta = Armar(dir, Array.Empty<string>(), out _, out _, FabricaInventario.Stock("CAF", 5, 2));

        Assert.True(consulta.EstaInicializado);
        ProductoGlobal[] vista = consulta.ConsultarCatalogo();

        ProductoGlobal caf = Assert.Single(vista);
        Assert.Equal("CAF", caf.Producto.ProductoId);
        AfirmarOferta(Assert.Single(caf.Ofertas), "SJ", 3, 0);
    }

    [Fact]
    public void VistaInicializadaSinProductosEsUnArregloVacio()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        ConsultaCatalogo consulta = Armar(dir, Array.Empty<string>(), out _, out _);

        Assert.Empty(consulta.ConsultarCatalogo());
    }

    [Fact]
    public void FaltandoUnaPrimeraInstantaneaConsultarLanzaYNombraLasSedesPendientes()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        ConsultaCatalogo consulta = Armar(dir, Remotas, out _, out _, FabricaInventario.Stock("CAF", 5, 0));

        Assert.False(consulta.EstaInicializado);
        CatalogoInicializandoException error = Assert.Throws<CatalogoInicializandoException>(() => consulta.ConsultarCatalogo());

        Assert.Equal(CodigosCatalogo.CatalogoInicializando, error.Codigo);
        Assert.Equal(new[] { "LIB", "ALA", "LIM" }, error.SedesPendientes);
        Assert.Contains("LIB", error.Message);
    }

    [Fact]
    public void PasaDeNoDisponibleADisponibleSoloConLaTerceraInstantaneaAunqueSeaVacia()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        ConsultaCatalogo consulta = Armar(dir, Remotas, out _, out CatalogoReplicado catalogo);

        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) });
        catalogo.AplicarInstantanea("ALA", new[] { Entrada("CAF", 2, 1) });
        // Un cambio aislado de la tercera sede no completa su primera sincronización.
        catalogo.AplicarCambio("LIM", Entrada("CAF", 9, 3));
        CatalogoInicializandoException error = Assert.Throws<CatalogoInicializandoException>(() => consulta.ConsultarCatalogo());
        Assert.Equal(new[] { "LIM" }, error.SedesPendientes);

        catalogo.AplicarInstantanea("LIM", Array.Empty<EntradaCatalogo>());

        ProductoGlobal[] vista = consulta.ConsultarCatalogo();
        ProductoGlobal caf = Assert.Single(vista);
        Assert.Equal(3, caf.Ofertas.Length);
        AfirmarOferta(caf.Ofertas[0], "ALA", 2, 1);
        AfirmarOferta(caf.Ofertas[1], "LIB", 4, 1);
        AfirmarOferta(caf.Ofertas[2], "LIM", 9, 3);
    }

    [Fact]
    public void TodasLasInstantaneasVaciasDejanUnaVistaInicializadaSinProductos()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        ConsultaCatalogo consulta = Armar(dir, Remotas, out _, out CatalogoReplicado catalogo);

        for (int i = 0; i < Remotas.Length; i++)
        {
            catalogo.AplicarInstantanea(Remotas[i], Array.Empty<EntradaCatalogo>());
        }

        Assert.True(consulta.EstaInicializado);
        Assert.Empty(consulta.ConsultarCatalogo());
    }

    // ---- Orden, ceros y omisión --------------------------------------------------------------------------------

    [Fact]
    public void OrdenaPorProductoYSedeIncluyeCerosYOmiteLaSedeSinDatos()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        string[] remotas = { "LIB", "ALA" };
        ConsultaCatalogo consulta = Armar(dir, remotas, out _, out CatalogoReplicado catalogo,
            FabricaInventario.Stock("CAF", 5, 0));
        // LIB informa 0 de CAF y ofrece ARR; ALA no informa CAF.
        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 0, 2), Entrada("ARR", 7, 1) });
        catalogo.AplicarInstantanea("ALA", Array.Empty<EntradaCatalogo>());

        ProductoGlobal[] vista = consulta.ConsultarCatalogo();

        Assert.Equal(2, vista.Length);
        Assert.Equal("ARR", vista[0].Producto.ProductoId);
        Assert.Equal("CAF", vista[1].Producto.ProductoId);

        ProductoGlobal arr = vista[0];
        AfirmarOferta(Assert.Single(arr.Ofertas), "LIB", 7, 1);

        ProductoGlobal caf = vista[1];
        Assert.Equal(2, caf.Ofertas.Length);
        AfirmarOferta(caf.Ofertas[0], "LIB", 0, 2);
        AfirmarOferta(caf.Ofertas[1], "SJ", 5, 0);
        Assert.DoesNotContain(caf.Ofertas, o => o.SedeId == "ALA");
    }

    [Fact]
    public void ElProductoAgotadoLocalApareceConCero()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        ConsultaCatalogo consulta = Armar(dir, Array.Empty<string>(), out _, out _,
            FabricaInventario.Stock("SAL", 0, 0), FabricaInventario.Stock("CAF", 3, 3));

        ProductoGlobal[] vista = consulta.ConsultarCatalogo();

        Assert.Equal(2, vista.Length);
        Assert.Equal("CAF", vista[0].Producto.ProductoId);
        AfirmarOferta(Assert.Single(vista[0].Ofertas), "SJ", 0, 0);
        Assert.Equal("SAL", vista[1].Producto.ProductoId);
        AfirmarOferta(Assert.Single(vista[1].Ofertas), "SJ", 0, 0);
    }

    [Fact]
    public void UnProductoCuyoCodigoEsPrefijoDeOtroNoMezclaSusOfertas()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        ConsultaCatalogo consulta = Armar(dir, new[] { "LIB" }, out _, out CatalogoReplicado catalogo,
            FabricaInventario.Stock("CAF", 1, 0), FabricaInventario.Stock("CA", 2, 0));
        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 8, 1), Entrada("CA", 9, 1) });

        ProductoGlobal[] vista = consulta.ConsultarCatalogo();

        Assert.Equal(2, vista.Length);
        Assert.Equal("CA", vista[0].Producto.ProductoId);
        AfirmarOferta(vista[0].Ofertas[0], "LIB", 9, 1);
        AfirmarOferta(vista[0].Ofertas[1], "SJ", 2, 0);
        Assert.Equal("CAF", vista[1].Producto.ProductoId);
        AfirmarOferta(vista[1].Ofertas[0], "LIB", 8, 1);
        AfirmarOferta(vista[1].Ofertas[1], "SJ", 1, 0);
    }

    [Fact]
    public void LaComparacionDeProductosDistingueMayusculas()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        ConsultaCatalogo consulta = Armar(dir, new[] { "LIB" }, out _, out CatalogoReplicado catalogo);
        catalogo.AplicarInstantanea("LIB", new[] { Entrada("caf", 1, 1), Entrada("CAF", 2, 1) });

        ProductoGlobal[] vista = consulta.ConsultarCatalogo();

        Assert.Equal(2, vista.Length);
        // Orden ordinal: las mayúsculas preceden a las minúsculas.
        Assert.Equal("CAF", vista[0].Producto.ProductoId);
        Assert.Equal("caf", vista[1].Producto.ProductoId);
    }

    [Fact]
    public void LaOfertaDeUnProductoSoloRemotoUsaElProductoDeLaRemota()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        ConsultaCatalogo consulta = Armar(dir, new[] { "LIB" }, out _, out CatalogoReplicado catalogo);
        catalogo.AplicarInstantanea("LIB", new[] { FabricaCatalogo.Entrada("ARR", 1, 1, 90000) });

        ProductoGlobal arr = Assert.Single(consulta.ConsultarCatalogo());

        Assert.Equal(90000, arr.Producto.PrecioUnitarioCentimos);
        Assert.Equal("Nombre ARR", arr.Producto.Nombre);
    }

    // ---- Lectura viva y persistencia ---------------------------------------------------------------------------

    [Fact]
    public void LaOfertaPropiaReflejaDisponibleYRevisionTrasReposicionesYReservas()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        ConsultaCatalogo consulta = Armar(dir, new[] { "LIB" }, out InventarioLocal inventario, out CatalogoReplicado catalogo,
            FabricaInventario.Stock("CAF", 5, 0));
        catalogo.AplicarInstantanea("LIB", Array.Empty<EntradaCatalogo>());

        new ServicioReposiciones(inventario).Reponer("CAF", "30000000-0000-4000-8000-000000000001", 3);

        ProductoGlobal caf = Assert.Single(consulta.ConsultarCatalogo());
        AfirmarOferta(Assert.Single(caf.Ofertas), "SJ", 8, 1);
    }

    [Fact]
    public void ConservaElValorAbsolutoDeRevision10TrasRecibir10_9Y10YDespuesDeReiniciar()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        string[] remotas = { "LIB" };
        ConsultaCatalogo consulta = Armar(dir, remotas, out _, out CatalogoReplicado catalogo);
        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 5, 8) });

        catalogo.AplicarCambio("LIB", Entrada("CAF", 2, 10));
        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 7, 9) });
        catalogo.AplicarCambio("LIB", Entrada("CAF", 2, 10));

        AfirmarOferta(Assert.Single(Assert.Single(consulta.ConsultarCatalogo()).Ofertas), "LIB", 2, 10);

        ConsultaCatalogo reiniciada = new ConsultaCatalogo(
            FabricaInventario.Recargar(dir, "SJ", remotas), FabricaCatalogo.Recargar(dir, "SJ", remotas));
        AfirmarOferta(Assert.Single(Assert.Single(reiniciada.ConsultarCatalogo()).Ofertas), "LIB", 2, 10);
    }

    [Fact]
    public void UnaCopiaInicializadaSeConservaConsultableAlReiniciarSinNuevasInstantaneas()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        string[] remotas = { "LIB", "ALA" };
        ConsultaCatalogo consulta = Armar(dir, remotas, out _, out CatalogoReplicado catalogo,
            FabricaInventario.Stock("CAF", 5, 0));
        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) });
        catalogo.AplicarInstantanea("ALA", Array.Empty<EntradaCatalogo>());
        Assert.True(consulta.EstaInicializado);

        ConsultaCatalogo reiniciada = new ConsultaCatalogo(
            FabricaInventario.Recargar(dir, "SJ", remotas), FabricaCatalogo.Recargar(dir, "SJ", remotas));

        Assert.True(reiniciada.EstaInicializado);
        ProductoGlobal caf = Assert.Single(reiniciada.ConsultarCatalogo());
        Assert.Equal(2, caf.Ofertas.Length);
        AfirmarOferta(caf.Ofertas[0], "LIB", 4, 1);
        AfirmarOferta(caf.Ofertas[1], "SJ", 5, 0);
    }

    [Fact]
    public void UnaCopiaSinInicializarSigueSinInicializarseTrasReiniciar()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        string[] remotas = { "LIB", "ALA" };
        ConsultaCatalogo consulta = Armar(dir, remotas, out _, out CatalogoReplicado catalogo);
        catalogo.AplicarInstantanea("LIB", new[] { Entrada("CAF", 4, 1) });
        Assert.False(consulta.EstaInicializado);

        ConsultaCatalogo reiniciada = new ConsultaCatalogo(
            FabricaInventario.Recargar(dir, "SJ", remotas), FabricaCatalogo.Recargar(dir, "SJ", remotas));

        CatalogoInicializandoException error = Assert.Throws<CatalogoInicializandoException>(() => reiniciada.ConsultarCatalogo());
        Assert.Equal(new[] { "ALA" }, error.SedesPendientes);
    }

    // ---- Aislamiento y validación ------------------------------------------------------------------------------

    [Fact]
    public void ModificarLoDevueltoNoAlteraLaVista()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        ConsultaCatalogo consulta = Armar(dir, Array.Empty<string>(), out _, out _, FabricaInventario.Stock("CAF", 5, 0));

        ProductoGlobal[] primera = consulta.ConsultarCatalogo();
        primera[0].Ofertas[0] = new Oferta("XXX", 99, 99);
        primera[0] = null!;

        ProductoGlobal caf = Assert.Single(consulta.ConsultarCatalogo());
        AfirmarOferta(Assert.Single(caf.Ofertas), "SJ", 5, 0);
    }

    [Fact]
    public void InventarioYCatalogoDeSedesDistintasSeRechazan()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        using DirectorioTemporal otro = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.Crear(dir, "SJ");
        CatalogoReplicado catalogo = FabricaCatalogo.Crear(otro, "LIB", new[] { "SJ" });

        Assert.Throws<ArgumentException>(() => new ConsultaCatalogo(inventario, catalogo));
        Assert.Throws<ArgumentNullException>(() => new ConsultaCatalogo(null!, catalogo));
        Assert.Throws<ArgumentNullException>(() => new ConsultaCatalogo(inventario, null!));
    }

    [Fact]
    public void OfertaYProductoGlobalValidanSusDatos()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Oferta("LIB", -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Oferta("LIB", 0, -1));
        Assert.Throws<ArgumentException>(() => new Oferta(" ", 0, 0));
        Producto producto = new Producto("CAF", "Café", "", "Bebidas", 1);
        Assert.Throws<ArgumentNullException>(() => new ProductoGlobal(producto, null!));
        Assert.Throws<ArgumentException>(() => new ProductoGlobal(producto, new Oferta[] { null! }));
    }
}
