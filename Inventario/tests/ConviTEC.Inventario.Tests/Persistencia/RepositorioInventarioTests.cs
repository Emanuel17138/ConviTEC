using System;
using System.IO;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Tests.Persistencia;

public class RepositorioInventarioTests
{
    private static StockLocal Stock(string id, int existencia, int reservada, long precio = 150000) =>
        new StockLocal(new Producto(id, "Café ñandú", "Descripción con \"comillas\"", "Bebidas", precio), existencia, reservada);

    private static void Guardar(RepositorioInventario repositorio, params StockLocal[] stocks)
    {
        EntradaStock[] entradas = new EntradaStock[stocks.Length];
        for (int i = 0; i < stocks.Length; i++)
        {
            entradas[i] = new EntradaStock(stocks[i], 0);
        }
        repositorio.GuardarEstado(ConversionEstado.Construir("LIB", entradas));
    }

    [Fact]
    public void GuardarYLeer_TodasLasSeccionesDelSnapshot()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"));
        ResultadoStock resultado = new ResultadoStock("p-1", "stock.reservar", EstadoReserva.Reservada, CodigosResultado.Ok, "Reservado: café ñandú");

        repositorio.GuardarEstado(ConversionEstado.Construir("LIB",
            new[] { new EntradaStock(Stock("CAF", 5, 2), 7) },
            new[] { new LoteReserva("p-1", "SJ", EstadoReserva.Reservada, new[] { new LineaReserva("CAF", 2, 150000) }) },
            new[] { new ComandoRegistrado("m-1", "stock.reservar", "SJ", "contenido", resultado) },
            new[] { new ReposicionRegistrada("op-1", "CAF", 3, 5, 2) },
            new[] { new SalidaPendiente("s-1", "SJ", "CAF", 3, 7) }));
        EstadoInventarioDto estado = repositorio.LeerEstado();

        Assert.Equal(7L, Assert.Single(estado.Stock!)!.Revision);
        LoteGuardadoDto lote = Assert.Single(estado.Lotes!)!;
        Assert.Equal("RESERVADA", lote.Estado);
        Assert.Equal(2, Assert.Single(lote.Lineas!)!.Cantidad);
        ComandoGuardadoDto comando = Assert.Single(estado.Comandos!)!;
        Assert.Equal("Reservado: café ñandú", comando.Resultado!.Mensaje);
        Assert.Equal("op-1", Assert.Single(estado.Reposiciones!)!.OperacionId);
        SalidaPendienteGuardadaDto salida = Assert.Single(estado.SalidasPendientes!)!;
        Assert.Equal("SJ", salida.DestinoSedeId);
        Assert.Equal(7L, salida.Revision);
        Assert.Contains("\"salidasPendientes\"", dir.Leer(RutasArchivosInventario.NombreEstado));
    }

    [Fact]
    public void GuardarYLeer_ConservaCantidadesPrecioYTextoUtf8()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"));

        Guardar(repositorio, new[] { Stock("CAF", 4, 1) });
        EstadoInventarioDto estado = repositorio.LeerEstado();

        Assert.Equal(EstadoInventarioDto.VersionActual, estado.Version);
        Assert.Equal("LIB", estado.SedeId);
        StockGuardadoDto entrada = Assert.Single(estado.Stock!)!;
        Assert.Equal("CAF", entrada.Producto!.ProductoId);
        Assert.Equal("Café ñandú", entrada.Producto.Nombre);
        Assert.Equal(150000L, entrada.Producto.PrecioUnitarioCentimos);
        Assert.Equal(4, entrada.Existencia);
        Assert.Equal(1, entrada.Reservada);
    }

    [Fact]
    public void GuardarEstado_UsaCamelCaseYNoDejaTemporal()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"));

        Guardar(repositorio, new[] { Stock("CAF", 4, 1) });

        string json = dir.Leer(RutasArchivosInventario.NombreEstado);
        Assert.Contains("\"productoId\"", json);
        Assert.Contains("\"precioUnitarioCentimos\"", json);
        Assert.DoesNotContain("\"ProductoId\"", json);
        Assert.False(dir.Existe(RutasArchivosInventario.NombreEstado + ".tmp"));
    }

    [Fact]
    public void GuardarEstado_SobrescribeElSnapshotAnterior()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"));

        Guardar(repositorio, new[] { Stock("CAF", 4, 1) });
        Guardar(repositorio, new[] { Stock("CAF", 9, 0), Stock("ARR", 2, 0) });

        EstadoInventarioDto estado = repositorio.LeerEstado();
        Assert.Equal(2, estado.Stock!.Length);
    }

    [Fact]
    public void GuardarEstado_SinProductos_GuardaArregloVacio()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"));

        Guardar(repositorio, Array.Empty<StockLocal>());

        Assert.Empty(repositorio.LeerEstado().Stock!);
    }

    [Fact]
    public void GuardarEstado_CreaElDirectorioDeLaSedeSiNoExiste()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        string subdirectorio = Path.Combine(dir.Ruta, "sede-nueva");
        RepositorioInventario repositorio = new RepositorioInventario(RutasArchivosInventario.EnDirectorio(subdirectorio, "LIB"));

        Guardar(repositorio, new[] { Stock("CAF", 1, 0) });

        Assert.True(repositorio.ExisteEstadoGuardado());
    }

    [Fact]
    public void ExisteEstadoGuardado_FalsoSiNoHayArchivo()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();

        Assert.False(new RepositorioInventario(dir.Rutas("LIB")).ExisteEstadoGuardado());
    }

    [Fact]
    public void Leer_ArchivoInexistente_Lanza()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"));

        Assert.Throws<DatosInventarioInvalidosException>(() => repositorio.LeerMaestro());
        Assert.Throws<DatosInventarioInvalidosException>(() => repositorio.LeerExistenciasIniciales());
        Assert.Throws<DatosInventarioInvalidosException>(() => repositorio.LeerEstado());
    }

    [Fact]
    public void Leer_ArchivoVacioONull_Lanza()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        dir.Escribir(RutasArchivosInventario.NombreMaestro, "");
        dir.Escribir(RutasArchivosInventario.NombreEstado, "null");
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"));

        Assert.Throws<DatosInventarioInvalidosException>(() => repositorio.LeerMaestro());
        Assert.Throws<DatosInventarioInvalidosException>(() => repositorio.LeerEstado());
    }

    [Fact]
    public void Leer_ArchivoConBomUtf8_Funciona()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        File.WriteAllText(Path.Combine(dir.Ruta, RutasArchivosInventario.NombreMaestro),
            """{"productos":[]}""", new System.Text.UTF8Encoding(true));
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"));

        Assert.Empty(repositorio.LeerMaestro().Productos!);
    }

    [Fact]
    public void GuardarEstado_DirectorioImposible_LanzaYNoDejaTemporal()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        // Un archivo en el lugar del directorio impide crear la ruta de la sede.
        string bloqueo = dir.Escribir("bloqueo", "x");
        RepositorioInventario repositorio = new RepositorioInventario(
            RutasArchivosInventario.EnDirectorio(Path.Combine(bloqueo, "sede"), "LIB"));

        Assert.Throws<DatosInventarioInvalidosException>(() => Guardar(repositorio, new[] { Stock("CAF", 1, 0) }));
    }

    [Fact]
    public void Constructor_ArgumentosInvalidos_Lanza()
    {
        Assert.Throws<ArgumentNullException>(() => new RepositorioInventario(null!));
        Assert.Throws<ArgumentException>(() => RutasArchivosInventario.EnDirectorio("dir", " "));
        Assert.Throws<ArgumentException>(() => RutasArchivosInventario.EnDirectorio("", "LIB"));
    }
}
