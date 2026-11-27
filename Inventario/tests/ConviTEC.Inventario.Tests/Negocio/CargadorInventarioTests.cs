using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Negocio;

public class CargadorInventarioTests
{
    private const string Maestro = """
        {"productos":[
          {"productoId":"SAL","nombre":"Sal","descripcion":"Sal marina 1 kg","categoria":"Despensa","precioUnitarioCentimos":50000},
          {"productoId":"CAF","nombre":"Café","descripcion":"Café molido 500 g","categoria":"Bebidas","precioUnitarioCentimos":150000},
          {"productoId":"ARR","nombre":"Arroz","descripcion":"Arroz 2 kg","categoria":"Despensa","precioUnitarioCentimos":90000}
        ]}
        """;

    private static string Semilla(string sedeId, string existencias) =>
        "{\"sedeId\":\"" + sedeId + "\",\"existencias\":" + existencias + "}";

    private static string Estado(string sedeId, string stock, int version = 1) =>
        "{\"version\":" + version + ",\"sedeId\":\"" + sedeId + "\",\"stock\":" + stock + "}";

    private static string EntradaCafe(int existencia, int reservada) =>
        "{\"producto\":{\"productoId\":\"CAF\",\"nombre\":\"Café\",\"descripcion\":\"d\",\"categoria\":\"c\",\"precioUnitarioCentimos\":150000}," +
        "\"existencia\":" + existencia + ",\"reservada\":" + reservada + "}";

    private static ResultadoArranque Cargar(DirectorioTemporal dir, string sedeId = "LIB") =>
        new CargadorInventario().Cargar(dir.Rutas(sedeId));

    [Fact]
    public void ArranqueInicial_ConstruyeInventarioOrdenadoConAgotadosYGuardaSnapshot()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB",
            """[{"productoId":"SAL","existencia":0},{"productoId":"CAF","existencia":5}]"""));

        ResultadoArranque resultado = Cargar(dir);

        Assert.Equal(ModoArranque.Inicial, resultado.Modo);
        StockLocal[] stock = resultado.Inventario.ConsultarInventario();
        Assert.Equal(2, stock.Length);
        Assert.Equal("CAF", stock[0].Producto.ProductoId);
        Assert.Equal(5, stock[0].Existencia);
        Assert.Equal(0, stock[0].Reservada);
        Assert.Equal(5, stock[0].Disponible);
        Assert.Equal(150000L, stock[0].Producto.PrecioUnitarioCentimos);
        Assert.Equal("SAL", stock[1].Producto.ProductoId);
        Assert.Equal(0, stock[1].Disponible);
        Assert.True(dir.Existe(RutasArchivosInventario.NombreEstado));
    }

    [Fact]
    public void ArranqueInicial_ProductoDelMaestroSinExistenciaNoEsDeLaSede()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", """[{"productoId":"ARR","existencia":3}]"""));

        InventarioLocal inventario = Cargar(dir).Inventario;

        Assert.Equal(1, inventario.Cantidad);
        Assert.False(inventario.Contiene("CAF"));
    }

    [Fact]
    public void ArranqueInicial_SedeSinProductos_DevuelveArregloVacio()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", "[]"));

        ResultadoArranque resultado = Cargar(dir);

        Assert.Empty(resultado.Inventario.ConsultarInventario());
        Assert.Equal(0, resultado.Inventario.Cantidad);
    }

    [Fact]
    public void ArranqueInicial_MaestroVacio_EsValido()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, """{"productos":[]}""");
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", "[]"));

        Assert.Empty(Cargar(dir).Inventario.ConsultarInventario());
    }

    [Fact]
    public void Recuperacion_ConservaCantidadesGuardadasEIgnoraLaSemilla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, Estado("LIB", "[" + EntradaCafe(4, 1) + "]"));
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", """[{"productoId":"CAF","existencia":8}]"""));

        ResultadoArranque resultado = Cargar(dir);

        Assert.Equal(ModoArranque.Recuperacion, resultado.Modo);
        StockLocal[] stock = resultado.Inventario.ConsultarInventario();
        Assert.Single(stock);
        Assert.Equal(4, stock[0].Existencia);
        Assert.Equal(1, stock[0].Reservada);
        Assert.Equal(3, stock[0].Disponible);
    }

    [Fact]
    public void Recuperacion_NoNecesitaMaestroNiSemilla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, Estado("LIB", "[" + EntradaCafe(4, 1) + "]"));

        Assert.Equal(ModoArranque.Recuperacion, Cargar(dir).Modo);
    }

    [Fact]
    public void ReiniciarDespuesDelArranqueInicial_RecuperaElMismoEstadoSinLeerSemillas()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", """[{"productoId":"CAF","existencia":5}]"""));
        Cargar(dir);
        // Si el segundo arranque leyera las semillas, fallaría o cambiaría la existencia.
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", """[{"productoId":"CAF","existencia":99}]"""));

        ResultadoArranque segundo = Cargar(dir);

        Assert.Equal(ModoArranque.Recuperacion, segundo.Modo);
        Assert.Equal(5, segundo.Inventario.ConsultarInventario()[0].Existencia);
    }

    [Fact]
    public void ProductoIdDistingueMayusculas()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", """[{"productoId":"CAF","existencia":5}]"""));

        InventarioLocal inventario = Cargar(dir).Inventario;

        Assert.True(inventario.Contiene("CAF"));
        Assert.False(inventario.Contiene("caf"));
    }

    [Fact]
    public void DosInstancias_CadaUnaDevuelveSoloSusCantidades()
    {
        using DirectorioTemporal dirLiberia = new DirectorioTemporal();
        using DirectorioTemporal dirAlajuela = new DirectorioTemporal();
        foreach ((DirectorioTemporal dir, string sede, int cantidad) in new[] { (dirLiberia, "LIB", 5), (dirAlajuela, "ALA", 9) })
        {
            dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
            dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales,
                Semilla(sede, "[{\"productoId\":\"CAF\",\"existencia\":" + cantidad + "}]"));
        }

        Assert.Equal(5, Cargar(dirLiberia, "LIB").Inventario.ConsultarInventario()[0].Existencia);
        Assert.Equal(9, Cargar(dirAlajuela, "ALA").Inventario.ConsultarInventario()[0].Existencia);
    }

    [Fact]
    public void ArranqueInicial_CodigoDuplicadoEnMaestro_FallaYNoGuardaSnapshot()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, """
            {"productos":[
              {"productoId":"CAF","nombre":"A","descripcion":"d","categoria":"c","precioUnitarioCentimos":1},
              {"productoId":"CAF","nombre":"B","descripcion":"d","categoria":"c","precioUnitarioCentimos":2}
            ]}
            """);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", "[]"));

        DatosInventarioInvalidosException error = Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));

        Assert.Contains("duplicado", error.Detalle);
        Assert.False(dir.Existe(RutasArchivosInventario.NombreEstado));
    }

    [Fact]
    public void ArranqueInicial_ProductoDuplicadoEnExistencias_FallaYNoGuardaSnapshot()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB",
            """[{"productoId":"CAF","existencia":1},{"productoId":"CAF","existencia":2}]"""));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
        Assert.False(dir.Existe(RutasArchivosInventario.NombreEstado));
    }

    [Fact]
    public void Recuperacion_ProductoDuplicadoEnSnapshot_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, Estado("LIB", "[" + EntradaCafe(4, 1) + "," + EntradaCafe(5, 0) + "]"));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void Recuperacion_ReservadaMayorQueExistencia_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, Estado("LIB", "[" + EntradaCafe(1, 2) + "]"));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void Recuperacion_ReservadaNegativa_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, Estado("LIB", "[" + EntradaCafe(5, -1) + "]"));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void Recuperacion_SnapshotDeOtraSede_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, Estado("ALA", "[" + EntradaCafe(4, 1) + "]"));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir, "LIB"));
    }

    [Fact]
    public void Recuperacion_VersionDesconocida_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, Estado("LIB", "[]", version: 2));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void Recuperacion_SnapshotCorrupto_FallaSinVolverALaSemilla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, "{ esto no es json");
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", """[{"productoId":"CAF","existencia":8}]"""));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void Recuperacion_CampoFaltante_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, Estado("LIB",
            """[{"producto":{"productoId":"CAF","nombre":"Café","descripcion":"d","categoria":"c","precioUnitarioCentimos":1},"existencia":4}]"""));

        DatosInventarioInvalidosException error = Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));

        Assert.Contains("reservada", error.Detalle);
    }

    [Fact]
    public void ArranqueInicial_ExistenciaNegativa_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", """[{"productoId":"CAF","existencia":-1}]"""));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
        Assert.False(dir.Existe(RutasArchivosInventario.NombreEstado));
    }

    [Fact]
    public void ArranqueInicial_ProductoAjenoAlMaestro_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", """[{"productoId":"XYZ","existencia":1}]"""));

        DatosInventarioInvalidosException error = Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));

        Assert.Contains("XYZ", error.Detalle);
    }

    [Fact]
    public void ArranqueInicial_ProductoIdConOtraCapitalizacion_NoCoincideConElMaestro()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", """[{"productoId":"caf","existencia":1}]"""));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void ArranqueInicial_SemillaDeOtraSede_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("ALA", "[]"));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir, "LIB"));
    }

    [Fact]
    public void ArranqueInicial_ProductoSinNombre_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro,
            """{"productos":[{"productoId":"CAF","descripcion":"d","categoria":"c","precioUnitarioCentimos":1}]}""");
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", "[]"));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void ArranqueInicial_PrecioNegativo_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro,
            """{"productos":[{"productoId":"CAF","nombre":"Café","descripcion":"d","categoria":"c","precioUnitarioCentimos":-5}]}""");
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", "[]"));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void ArranqueInicial_ElementoNuloEnArreglo_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, """{"productos":[null]}""");
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", "[]"));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void ArranqueInicial_MaestroInexistente_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", "[]"));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void ArranqueInicial_JsonMalFormado_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, "{\"productos\": [");
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", "[]"));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void ArranqueInicial_ExistenciaNoEntera_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        dir.Escribir(RutasArchivosInventario.NombreExistenciasIniciales, Semilla("LIB", """[{"productoId":"CAF","existencia":2.5}]"""));

        Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void Cargar_RutasNulas_Lanza()
    {
        Assert.Throws<System.ArgumentNullException>(() => new CargadorInventario().Cargar(null!));
    }
}
