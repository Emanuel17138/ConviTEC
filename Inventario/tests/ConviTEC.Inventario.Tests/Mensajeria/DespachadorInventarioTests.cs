using System;
using System.Text;
using ConviTEC.Inventario.Mensajeria;

namespace ConviTEC.Inventario.Tests.Mensajeria;

/// Despacho por tipo: el mismo mecanismo que reutilizará RF03 para <c>catalogo.*</c>.
public class DespachadorInventarioTests
{
    private sealed class ManejadorContador : IManejadorMensaje
    {
        public string[] Tipos { get; }
        public int Llamadas { get; private set; }
        public Sobre? Ultimo { get; private set; }

        public ManejadorContador(params string[] tipos)
        {
            Tipos = tipos;
        }

        public ResultadoDespacho Manejar(Sobre sobre)
        {
            Llamadas++;
            Ultimo = sobre;
            return ResultadoDespacho.Acusar();
        }
    }

    private static Sobre Mensaje(string tipo, string destino = "LIB") =>
        Sobre.ConCuerpoTexto(tipo, "m-1", "m-1", "SJ", destino, "{}");

    [Fact]
    public void Despachar_EntregaAlManejadorDelTipo()
    {
        DespachadorInventario despachador = new DespachadorInventario("LIB");
        ManejadorContador stock = new ManejadorContador("stock.reservar");
        ManejadorContador catalogo = new ManejadorContador("catalogo.solicitar", "catalogo.actualizado");
        despachador.Registrar(stock);
        despachador.Registrar(catalogo);

        ResultadoDespacho resultado = despachador.Despachar(Mensaje("catalogo.actualizado"));

        Assert.Equal(AccionConsumo.Acusar, resultado.Accion);
        Assert.Equal(0, stock.Llamadas);
        Assert.Equal(1, catalogo.Llamadas);
        Assert.Equal("catalogo.actualizado", catalogo.Ultimo!.Tipo);
    }

    [Fact]
    public void Despachar_DesdeBytes_DecodificaYEntrega()
    {
        DespachadorInventario despachador = new DespachadorInventario("LIB");
        ManejadorContador manejador = new ManejadorContador("stock.confirmar");
        despachador.Registrar(manejador);

        ResultadoDespacho resultado = despachador.Despachar(CodificadorSobre.Codificar(Mensaje("stock.confirmar")));

        Assert.Equal(AccionConsumo.Acusar, resultado.Accion);
        Assert.Equal(1, manejador.Llamadas);
    }

    [Fact]
    public void Despachar_TipoSinManejador_SeDescarta()
    {
        DespachadorInventario despachador = new DespachadorInventario("LIB");
        despachador.Registrar(new ManejadorContador("stock.reservar"));

        ResultadoDespacho resultado = despachador.Despachar(Mensaje("tipo.raro"));

        Assert.Equal(AccionConsumo.Descartar, resultado.Accion);
        Assert.Contains("tipo.raro", resultado.Motivo);
        Assert.Empty(resultado.Salidas);
    }

    [Fact]
    public void Despachar_DespachadorVacio_SeDescarta()
    {
        DespachadorInventario despachador = new DespachadorInventario("LIB");

        Assert.Equal(AccionConsumo.Descartar, despachador.Despachar(Mensaje("stock.reservar")).Accion);
    }

    [Fact]
    public void Despachar_DestinoDeOtraSede_SeDescartaSinLlamarAlManejador()
    {
        DespachadorInventario despachador = new DespachadorInventario("LIB");
        ManejadorContador manejador = new ManejadorContador("stock.reservar");
        despachador.Registrar(manejador);

        ResultadoDespacho resultado = despachador.Despachar(Mensaje("stock.reservar", destino: "ALA"));

        Assert.Equal(AccionConsumo.Descartar, resultado.Accion);
        Assert.Equal(0, manejador.Llamadas);
    }

    [Fact]
    public void Despachar_DestinoSensibleAMayusculas()
    {
        DespachadorInventario despachador = new DespachadorInventario("LIB");
        despachador.Registrar(new ManejadorContador("stock.reservar"));

        Assert.Equal(AccionConsumo.Descartar, despachador.Despachar(Mensaje("stock.reservar", destino: "lib")).Accion);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("{}")]
    [InlineData("")]
    public void Despachar_BytesIlegibles_SeDescartan(string texto)
    {
        DespachadorInventario despachador = new DespachadorInventario("LIB");
        despachador.Registrar(new ManejadorContador("stock.reservar"));

        ResultadoDespacho resultado = despachador.Despachar(Encoding.UTF8.GetBytes(texto));

        Assert.Equal(AccionConsumo.Descartar, resultado.Accion);
    }

    [Fact]
    public void Despachar_BytesNulos_SeDescartan()
    {
        DespachadorInventario despachador = new DespachadorInventario("LIB");

        Assert.Equal(AccionConsumo.Descartar, despachador.Despachar((byte[]?)null).Accion);
    }

    [Fact]
    public void Registrar_TipoRepetido_Lanza_SinRegistrarNadaDelSegundo()
    {
        DespachadorInventario despachador = new DespachadorInventario("LIB");
        despachador.Registrar(new ManejadorContador("stock.reservar"));

        Assert.Throws<InvalidOperationException>(
            () => despachador.Registrar(new ManejadorContador("catalogo.solicitar", "stock.reservar")));
        Assert.False(despachador.AtiendeTipo("catalogo.solicitar"));
    }

    [Fact]
    public void Registrar_ManejadorSinTipos_Lanza()
    {
        DespachadorInventario despachador = new DespachadorInventario("LIB");

        Assert.Throws<ArgumentException>(() => despachador.Registrar(new ManejadorContador()));
        Assert.Throws<ArgumentNullException>(() => despachador.Registrar(null!));
    }

    [Fact]
    public void Constructor_SedeVacia_Lanza()
    {
        Assert.Throws<ArgumentException>(() => new DespachadorInventario(" "));
    }
}
