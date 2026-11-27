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

    // ---- RF02: revisión y secciones nuevas del snapshot ----

    private static string EstadoConSecciones(string seccionesJson) =>
        "{\"version\":1,\"sedeId\":\"LIB\",\"stock\":[" + EntradaCafe(4, 1) + "]," + seccionesJson + "}";

    private const string Resultado =
        "{\"pedidoId\":\"p-1\",\"operacion\":\"stock.reservar\",\"estadoReserva\":\"RESERVADA\",\"codigo\":\"OK\",\"mensaje\":\"ok\"}";

    private static DatosInventarioInvalidosException RecuperarFallido(string seccionesJson)
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, EstadoConSecciones(seccionesJson));
        return Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));
    }

    [Fact]
    public void Recuperacion_SnapshotDeRf01SinCamposNuevos_RevisionCeroYSeccionesVacias()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, Estado("LIB", "[" + EntradaCafe(4, 1) + "]"));

        InventarioLocal inventario = Cargar(dir).Inventario;

        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(0, revision);
        Assert.Empty(inventario.ConsultarSalidasPendientes());
        Assert.False(inventario.BuscarLote("p-1", out _));
    }

    [Fact]
    public void Recuperacion_ConservaRevisionYSecciones()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, "{\"version\":1,\"sedeId\":\"LIB\",\"stock\":[" +
            EntradaCafe(4, 1).Replace("\"reservada\":1", "\"reservada\":1,\"revision\":9") + "]," +
            "\"lotes\":[{\"pedidoId\":\"p-1\",\"coordinadoraSedeId\":\"SJ\",\"estado\":\"RESERVADA\"," +
            "\"lineas\":[{\"productoId\":\"CAF\",\"cantidad\":1,\"precioUnitarioCentimos\":150000}]}]," +
            "\"comandos\":[{\"mensajeId\":\"m-1\",\"tipo\":\"stock.reservar\",\"origenSedeId\":\"SJ\",\"contenido\":\"c\",\"resultado\":" + Resultado + "}]," +
            "\"reposiciones\":[{\"operacionId\":\"op-1\",\"productoId\":\"CAF\",\"cantidad\":3,\"existencia\":4,\"reservada\":1}]," +
            "\"salidasPendientes\":[{\"mensajeId\":\"s-1\",\"destinoSedeId\":\"SJ\",\"productoId\":\"CAF\",\"disponible\":3,\"revision\":9}]}");

        InventarioLocal inventario = Cargar(dir).Inventario;

        Assert.True(inventario.BuscarRevision("CAF", out long revision));
        Assert.Equal(9, revision);
        Assert.True(inventario.BuscarLote("p-1", out LoteReserva? lote));
        Assert.Equal(EstadoReserva.Reservada, lote.Estado);
        Assert.True(inventario.BuscarComando("m-1", out ComandoRegistrado? comando));
        Assert.Equal("ok", comando.Resultado.Mensaje);
        Assert.True(inventario.BuscarReposicion("op-1", out ReposicionRegistrada? reposicion));
        Assert.Equal(3, reposicion.Disponible);
        Assert.Equal(9L, Assert.Single(inventario.ConsultarSalidasPendientes()).Revision);
    }

    [Fact]
    public void Recuperacion_RevisionNegativa_Falla()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, Estado("LIB",
            "[" + EntradaCafe(4, 1).Replace("\"reservada\":1", "\"reservada\":1,\"revision\":-1") + "]"));

        DatosInventarioInvalidosException error = Assert.Throws<DatosInventarioInvalidosException>(() => Cargar(dir));

        Assert.Contains("revisión", error.Detalle);
    }

    [Fact]
    public void Recuperacion_LoteDuplicado_Falla()
    {
        const string lote = "{\"pedidoId\":\"p-1\",\"coordinadoraSedeId\":\"SJ\",\"estado\":\"LIBERADA\",\"lineas\":[]}";

        DatosInventarioInvalidosException error = RecuperarFallido("\"lotes\":[" + lote + "," + lote + "]");

        Assert.Contains("lotes[1]", error.Detalle);
        Assert.Contains("duplicado", error.Detalle);
    }

    [Theory]
    [InlineData("\"estado\":\"INEXISTENTE\",\"lineas\":[]")]
    [InlineData("\"estado\":\"reservada\",\"lineas\":[]")]
    [InlineData("\"estado\":\"RESERVADA\",\"lineas\":[]")]
    [InlineData("\"estado\":\"RESERVADA\",\"lineas\":[{\"productoId\":\"XYZ\",\"cantidad\":1,\"precioUnitarioCentimos\":1}]")]
    [InlineData("\"estado\":\"CONFIRMADA\",\"lineas\":[{\"productoId\":\"CAF\",\"cantidad\":0,\"precioUnitarioCentimos\":1}]")]
    [InlineData("\"estado\":\"RESERVADA\",\"lineas\":[{\"productoId\":\"CAF\",\"cantidad\":1,\"precioUnitarioCentimos\":1},{\"productoId\":\"CAF\",\"cantidad\":2,\"precioUnitarioCentimos\":1}]")]
    [InlineData("\"estado\":\"RESERVADA\"")]
    public void Recuperacion_LoteInvalido_Falla(string estadoYLineas)
    {
        DatosInventarioInvalidosException error = RecuperarFallido(
            "\"lotes\":[{\"pedidoId\":\"p-1\",\"coordinadoraSedeId\":\"SJ\"," + estadoYLineas + "}]");

        Assert.Contains("lotes[0]", error.Detalle);
    }

    [Fact]
    public void Recuperacion_LoteRechazadoPuedeTenerLineasDeProductoAjeno()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, EstadoConSecciones(
            "\"lotes\":[{\"pedidoId\":\"p-1\",\"coordinadoraSedeId\":\"SJ\",\"estado\":\"RECHAZADA\"," +
            "\"lineas\":[{\"productoId\":\"XYZ\",\"cantidad\":1,\"precioUnitarioCentimos\":1}]}]"));

        Assert.True(Cargar(dir).Inventario.BuscarLote("p-1", out LoteReserva? lote));
        Assert.Equal(EstadoReserva.Rechazada, lote.Estado);
    }

    [Fact]
    public void Recuperacion_ComandoDuplicadoOSinResultado_Falla()
    {
        const string comando = "{\"mensajeId\":\"m-1\",\"tipo\":\"stock.reservar\",\"origenSedeId\":\"SJ\",\"contenido\":\"c\",\"resultado\":" + Resultado + "}";

        Assert.Contains("comandos[1]", RecuperarFallido("\"comandos\":[" + comando + "," + comando + "]").Detalle);
        Assert.Contains("resultado", RecuperarFallido(
            "\"comandos\":[{\"mensajeId\":\"m-1\",\"tipo\":\"t\",\"origenSedeId\":\"SJ\",\"contenido\":\"c\"}]").Detalle);
    }

    [Fact]
    public void Recuperacion_ReposicionDuplicadaOConProductoAjeno_Falla()
    {
        const string reposicion = "{\"operacionId\":\"op-1\",\"productoId\":\"CAF\",\"cantidad\":3,\"existencia\":4,\"reservada\":1}";

        Assert.Contains("duplicado", RecuperarFallido("\"reposiciones\":[" + reposicion + "," + reposicion + "]").Detalle);
        Assert.Contains("XYZ", RecuperarFallido(
            "\"reposiciones\":[{\"operacionId\":\"op-1\",\"productoId\":\"XYZ\",\"cantidad\":3,\"existencia\":4,\"reservada\":1}]").Detalle);
        Assert.Contains("reposiciones[0]", RecuperarFallido(
            "\"reposiciones\":[{\"operacionId\":\"op-1\",\"productoId\":\"CAF\",\"cantidad\":0,\"existencia\":4,\"reservada\":1}]").Detalle);
    }

    [Fact]
    public void Recuperacion_SalidaDuplicadaHaciaLaPropiaSedeOConProductoAjeno_Falla()
    {
        const string salida = "{\"mensajeId\":\"s-1\",\"destinoSedeId\":\"SJ\",\"productoId\":\"CAF\",\"disponible\":3,\"revision\":1}";

        Assert.Contains("salidasPendientes[1]", RecuperarFallido("\"salidasPendientes\":[" + salida + "," + salida + "]").Detalle);
        Assert.Contains("a sí misma", RecuperarFallido(
            "\"salidasPendientes\":[" + salida.Replace("\"SJ\"", "\"LIB\"") + "]").Detalle);
        Assert.Contains("XYZ", RecuperarFallido(
            "\"salidasPendientes\":[" + salida.Replace("\"CAF\"", "\"XYZ\"") + "]").Detalle);
    }

    [Fact]
    public void Recuperacion_ElementoNuloEnSeccionNueva_Falla()
    {
        Assert.Contains("lotes[0]", RecuperarFallido("\"lotes\":[null]").Detalle);
        Assert.Contains("comandos[0]", RecuperarFallido("\"comandos\":[null]").Detalle);
        Assert.Contains("reposiciones[0]", RecuperarFallido("\"reposiciones\":[null]").Detalle);
        Assert.Contains("salidasPendientes[0]", RecuperarFallido("\"salidasPendientes\":[null]").Detalle);
    }

    [Fact]
    public void Cargar_SedesRemotasInvalidas_Lanza()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreEstado, Estado("LIB", "[]"));

        Assert.Throws<System.ArgumentException>(() => new CargadorInventario().Cargar(dir.Rutas("LIB"), new[] { "LIB" }));
        Assert.Throws<System.ArgumentNullException>(() => new CargadorInventario().Cargar(dir.Rutas("LIB"), null!));
    }

    [Fact]
    public void Cargar_RutasNulas_Lanza()
    {
        Assert.Throws<System.ArgumentNullException>(() => new CargadorInventario().Cargar(null!));
    }
}
