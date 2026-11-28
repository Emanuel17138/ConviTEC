using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Negocio;

/// Reconstrucción de la réplica desde catalogo-replicado.json: formato aceptado y rechazo con archivo y posición.
public class CargadorCatalogoTests
{
    private static readonly string[] Remotas = { "LIB", "ALA" };

    private const string Producto =
        "{\"productoId\":\"CAF\",\"nombre\":\"Café\",\"descripcion\":\"d\",\"categoria\":\"c\",\"precioUnitarioCentimos\":150000}";

    private static string Oferta(string sede, string producto = Producto, string disponible = "3", string revision = "7") =>
        $"{{\"sedeId\":\"{sede}\",\"producto\":{producto},\"disponible\":{disponible},\"revision\":{revision}}}";

    private static string Archivo(
        string version = "1", string sede = "\"SJ\"", string sincronizadas = "[\"LIB\"]", string ofertas = "[]") =>
        $"{{\"version\":{version},\"sedeId\":{sede},\"sedesSincronizadas\":{sincronizadas},\"ofertas\":{ofertas}}}";

    private static DatosInventarioInvalidosException Rechaza(string contenido)
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreCatalogoReplicado, contenido);
        return Assert.Throws<DatosInventarioInvalidosException>(() => FabricaCatalogo.Recargar(dir, "SJ", Remotas));
    }

    [Fact]
    public void LeeUnArchivoValidoYReconstruyeLasOfertasYLasMarcas()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreCatalogoReplicado,
            Archivo(sincronizadas: "[\"LIB\",\"ALA\"]", ofertas: "[" + Oferta("LIB") + "," + Oferta("ALA", disponible: "0", revision: "2") + "]"));

        CatalogoReplicado catalogo = FabricaCatalogo.Recargar(dir, "SJ", Remotas);

        Assert.True(catalogo.EstaInicializado);
        Assert.True(catalogo.BuscarOferta("LIB", "CAF", out OfertaReplicada? lib));
        Assert.Equal(3, lib.Disponible);
        Assert.Equal(7, lib.Revision);
        Assert.Equal("Café", lib.Producto.Nombre);
        Assert.Equal(150000, lib.Producto.PrecioUnitarioCentimos);
        Assert.True(catalogo.BuscarOferta("ALA", "CAF", out OfertaReplicada? ala));
        Assert.Equal(0, ala.Disponible);
    }

    [Fact]
    public void ElArchivoGuardadoSeLeeDeNuevoIgual()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = FabricaCatalogo.Crear(dir, "SJ", Remotas);
        catalogo.AplicarInstantanea("LIB", new[] { FabricaCatalogo.Entrada("CAF", 3, 7), FabricaCatalogo.Entrada("ARR", 0, 1) });
        string contenido = dir.Leer(RutasArchivosInventario.NombreCatalogoReplicado);

        FabricaCatalogo.Recargar(dir, "SJ", Remotas).AplicarCambio("ALA", FabricaCatalogo.Entrada("CAF", 1, 1));

        Assert.Contains("\"sedesSincronizadas\"", contenido);
        Assert.Contains("\"revision\": 7", contenido);
        Assert.True(FabricaCatalogo.Recargar(dir, "SJ", Remotas).BuscarOferta("ALA", "CAF", out _));
    }

    [Fact]
    public void UnArchivoSinOfertasNiMarcasEsUnaReplicaVaciaNoInicializada()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreCatalogoReplicado, Archivo(sincronizadas: "[]"));

        CatalogoReplicado catalogo = FabricaCatalogo.Recargar(dir, "SJ", Remotas);

        Assert.False(catalogo.EstaInicializado);
        Assert.Empty(catalogo.ConsultarOfertas());
    }

    [Fact]
    public void RechazaJsonMalFormadoYArchivoVacio()
    {
        Assert.Contains("JSON inválido", Rechaza("{ no es json").Detalle);
        Assert.Contains("JSON inválido", Rechaza("").Detalle);
        Assert.Contains("no contiene un objeto", Rechaza("null").Detalle);
    }

    [Fact]
    public void RechazaVersionDesconocidaOAusente()
    {
        Assert.Contains("versión", Rechaza(Archivo(version: "2")).Detalle);
        Assert.Contains("(falta)", Rechaza("{\"sedeId\":\"SJ\",\"sedesSincronizadas\":[],\"ofertas\":[]}").Detalle);
    }

    [Fact]
    public void RechazaElArchivoDeOtraSede()
    {
        Assert.Contains("'LIB'", Rechaza(Archivo(sede: "\"LIB\"")).Detalle);
        Assert.Contains("(falta)", Rechaza("{\"version\":1,\"sedesSincronizadas\":[],\"ofertas\":[]}").Detalle);
    }

    [Fact]
    public void RechazaSeccionesFaltantes()
    {
        Assert.Contains("sedesSincronizadas", Rechaza("{\"version\":1,\"sedeId\":\"SJ\",\"ofertas\":[]}").Detalle);
        Assert.Contains("ofertas", Rechaza("{\"version\":1,\"sedeId\":\"SJ\",\"sedesSincronizadas\":[]}").Detalle);
    }

    [Fact]
    public void RechazaMarcasInvalidasConPosicion()
    {
        Assert.Contains("sedesSincronizadas[1]", Rechaza(Archivo(sincronizadas: "[\"LIB\",null]")).Detalle);
        Assert.Contains("sedesSincronizadas[1]", Rechaza(Archivo(sincronizadas: "[\"LIB\",\" \"]")).Detalle);
        Assert.Contains("duplicada", Rechaza(Archivo(sincronizadas: "[\"LIB\",\"LIB\"]")).Detalle);
        Assert.Contains("propio catálogo", Rechaza(Archivo(sincronizadas: "[\"SJ\"]")).Detalle);
    }

    [Fact]
    public void RechazaOfertasInvalidasConPosicion()
    {
        Assert.Contains("ofertas[0] es nulo", Rechaza(Archivo(ofertas: "[null]")).Detalle);
        Assert.Contains("ofertas[1]: falta 'sedeId'", Rechaza(Archivo(ofertas: "[" + Oferta("LIB") + ",{\"producto\":" + Producto + ",\"disponible\":1,\"revision\":1}]")).Detalle);
        Assert.Contains("propio catálogo", Rechaza(Archivo(ofertas: "[" + Oferta("SJ") + "]")).Detalle);
        Assert.Contains("ofertas[0].producto es nulo", Rechaza(Archivo(ofertas: "[{\"sedeId\":\"LIB\",\"disponible\":1,\"revision\":1}]")).Detalle);
        Assert.Contains("falta 'precioUnitarioCentimos'", Rechaza(Archivo(ofertas:
            "[" + Oferta("LIB", "{\"productoId\":\"CAF\",\"nombre\":\"n\",\"descripcion\":\"d\",\"categoria\":\"c\"}") + "]")).Detalle);
        Assert.Contains("falta 'disponible'", Rechaza(Archivo(ofertas: "[{\"sedeId\":\"LIB\",\"producto\":" + Producto + ",\"revision\":1}]")).Detalle);
        Assert.Contains("falta 'revision'", Rechaza(Archivo(ofertas: "[{\"sedeId\":\"LIB\",\"producto\":" + Producto + ",\"disponible\":1}]")).Detalle);
    }

    [Fact]
    public void RechazaDisponibleORevisionNegativosOFraccionarios()
    {
        Assert.Contains("ofertas[0]", Rechaza(Archivo(ofertas: "[" + Oferta("LIB", disponible: "-1") + "]")).Detalle);
        Assert.Contains("ofertas[0]", Rechaza(Archivo(ofertas: "[" + Oferta("LIB", revision: "-1") + "]")).Detalle);
        Assert.Contains("JSON inválido", Rechaza(Archivo(ofertas: "[" + Oferta("LIB", disponible: "1.5") + "]")).Detalle);
    }

    [Fact]
    public void RechazaParejaSedeProductoRepetida()
    {
        DatosInventarioInvalidosException e =
            Rechaza(Archivo(ofertas: "[" + Oferta("LIB") + "," + Oferta("ALA") + "," + Oferta("LIB", revision: "9") + "]"));

        Assert.Contains("ofertas[2]", e.Detalle);
        Assert.Contains("duplicada", e.Detalle);
    }

    [Fact]
    public void ElMismoProductoEnSedesDistintasNoEsDuplicado()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreCatalogoReplicado, Archivo(ofertas: "[" + Oferta("LIB") + "," + Oferta("ALA") + "]"));

        Assert.Equal(2, FabricaCatalogo.Recargar(dir, "SJ", Remotas).ConsultarOfertas().Length);
    }

    [Fact]
    public void ElMensajeDeErrorIncluyeLaRutaDelArchivo()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreCatalogoReplicado, Archivo(version: "9"));

        DatosInventarioInvalidosException e =
            Assert.Throws<DatosInventarioInvalidosException>(() => FabricaCatalogo.Recargar(dir, "SJ", Remotas));

        Assert.Equal(dir.Rutas("SJ").CatalogoReplicado, e.Archivo);
        Assert.Contains(RutasArchivosInventario.NombreCatalogoReplicado, e.Message);
    }

    [Fact]
    public void UnTemporalSobranteSeIgnoraAlRecuperar()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        CatalogoReplicado catalogo = FabricaCatalogo.Crear(dir, "SJ", Remotas);
        catalogo.AplicarCambio("LIB", FabricaCatalogo.Entrada("CAF", 4, 1));
        dir.Escribir(RutasArchivosInventario.NombreCatalogoReplicado + ".tmp", "basura {{{");

        CatalogoReplicado reiniciado = FabricaCatalogo.Recargar(dir, "SJ", Remotas);

        Assert.True(reiniciado.BuscarOferta("LIB", "CAF", out _));
        reiniciado.AplicarCambio("LIB", FabricaCatalogo.Entrada("CAF", 5, 2)); // El siguiente guardado reutiliza el .tmp.
        Assert.True(FabricaCatalogo.Recargar(dir, "SJ", Remotas).BuscarOferta("LIB", "CAF", out OfertaReplicada? oferta));
        Assert.Equal(5, oferta.Disponible);
    }

    [Fact]
    public void ElCatalogoNoAfectaAlArchivoDeInventario()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        FabricaCatalogo.Crear(dir, "SJ", Remotas).AplicarInstantanea("LIB", Array.Empty<EntradaCatalogo>());

        Assert.True(dir.Existe(RutasArchivosInventario.NombreCatalogoReplicado));
        Assert.False(dir.Existe(RutasArchivosInventario.NombreEstado));
    }
}
