using System;
using System.Text.Json;
using System.Threading.Tasks;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Mensajeria;

namespace ConviTEC.Inventario.Tests.Mensajeria;

/// Cliente de inventario para Pedidos: armado de comandos, envío y lectura del resultado.
public class ClienteInventarioTests
{
    private static ClienteInventario Crear(PublicadorFalso publicador) => new ClienteInventario("SJ", publicador);

    private static Sobre Resultado(string cuerpo) =>
        Sobre.ConCuerpoTexto("stock.resultado", "r-1", "m-1", "LIB", "SJ", cuerpo);

    private const string ResultadoValido =
        "{\"pedidoId\":\"p-1\",\"operacion\":\"stock.reservar\",\"estadoReserva\":\"RESERVADA\",\"codigo\":\"OK\",\"mensaje\":\"Lote reservado.\"}";

    [Fact]
    public void CrearReservar_PeticionNueva_CorrelacionIgualAlMensajeId()
    {
        ClienteInventario cliente = Crear(new PublicadorFalso());

        Sobre sobre = cliente.CrearReservar("m-1", "LIB", "p-1", new[] { new LineaReserva("CAF", 2, 150000) });

        Assert.Equal("stock.reservar", sobre.Tipo);
        Assert.Equal("m-1", sobre.MensajeId);
        Assert.Equal("m-1", sobre.CorrelacionId);
        Assert.Equal("SJ", sobre.OrigenSedeId);
        Assert.Equal("LIB", sobre.DestinoSedeId);
        Assert.Equal("p-1", sobre.Cuerpo.GetProperty("pedidoId").GetString());
        JsonElement linea = sobre.Cuerpo.GetProperty("lineas")[0];
        Assert.Equal("CAF", linea.GetProperty("productoId").GetString());
        Assert.Equal(2, linea.GetProperty("cantidad").GetInt32());
        Assert.Equal(150000, linea.GetProperty("precioUnitarioCentimos").GetInt64());
    }

    [Fact]
    public void CrearConfirmarYLiberar_LlevanSoloElPedidoId()
    {
        ClienteInventario cliente = Crear(new PublicadorFalso());

        Sobre confirmar = cliente.CrearConfirmar("m-2", "LIB", "p-1");
        Sobre liberar = cliente.CrearLiberar("m-3", "LIB", "p-1");

        Assert.Equal("stock.confirmar", confirmar.Tipo);
        Assert.Equal("stock.liberar", liberar.Tipo);
        Assert.Equal("p-1", confirmar.Cuerpo.GetProperty("pedidoId").GetString());
        Assert.Equal("m-3", liberar.CorrelacionId);
    }

    [Fact]
    public async Task EnviarAsync_VaALaColaDeInventarioDeLaProveedora()
    {
        PublicadorFalso publicador = new PublicadorFalso();
        ClienteInventario cliente = Crear(publicador);

        await cliente.EnviarAsync(cliente.CrearConfirmar("m-2", "LIB", "p-1"));

        SobreSaliente enviado = Assert.Single(publicador.Enviados);
        Assert.Equal("inventario.LIB", enviado.ClaveRuta);
    }

    [Fact]
    public async Task Reintento_ReenviaElMismoSobreConElMismoMensajeId()
    {
        PublicadorFalso publicador = new PublicadorFalso { Falla = true };
        ClienteInventario cliente = Crear(publicador);
        Sobre comando = cliente.CrearConfirmar("m-2", "LIB", "p-1");

        await Assert.ThrowsAsync<EnvioFallidoException>(() => cliente.EnviarAsync(comando));
        publicador.Falla = false;
        await cliente.EnviarAsync(comando);

        Assert.Equal("m-2", Assert.Single(publicador.Enviados).Sobre.MensajeId);
    }

    [Fact]
    public async Task EnviarAsync_RechazaSobresQueNoSonComandosPropios()
    {
        ClienteInventario cliente = Crear(new PublicadorFalso());
        Sobre ajeno = Sobre.ConCuerpoTexto("stock.confirmar", "m-1", "m-1", "ALA", "LIB", "{}");
        Sobre otroTipo = Sobre.ConCuerpoTexto("catalogo.solicitar", "m-2", "m-2", "SJ", "LIB", "{}");

        await Assert.ThrowsAsync<ArgumentException>(() => cliente.EnviarAsync(ajeno));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.EnviarAsync(otroTipo));
    }

    [Fact]
    public void Crear_ArgumentosInvalidos_Lanza()
    {
        ClienteInventario cliente = Crear(new PublicadorFalso());

        Assert.Throws<ArgumentException>(() => cliente.CrearConfirmar("", "LIB", "p-1"));
        Assert.Throws<ArgumentException>(() => cliente.CrearConfirmar("m-1", " ", "p-1"));
        Assert.Throws<ArgumentException>(() => cliente.CrearLiberar("m-1", "LIB", ""));
        Assert.Throws<ArgumentNullException>(() => cliente.CrearReservar("m-1", "LIB", "p-1", null!));
        Assert.Throws<ArgumentNullException>(() => cliente.CrearReservar("m-1", "LIB", "p-1", new LineaReserva[1]));
    }

    [Fact]
    public void TryLeerResultado_Valido()
    {
        Assert.True(ClienteInventario.TryLeerResultado(Resultado(ResultadoValido), out ResultadoStock? resultado, out string motivo), motivo);

        Assert.Equal("p-1", resultado!.PedidoId);
        Assert.Equal("stock.reservar", resultado.Operacion);
        Assert.Equal(EstadoReserva.Reservada, resultado.EstadoReserva);
        Assert.Equal(CodigosResultado.Ok, resultado.Codigo);
        Assert.Equal("Lote reservado.", resultado.Mensaje);
    }

    [Fact]
    public void TryLeerResultado_PedidoIdVacioEsValido()
    {
        string cuerpo = ResultadoValido.Replace("\"p-1\"", "\"\"");

        Assert.True(ClienteInventario.TryLeerResultado(Resultado(cuerpo), out ResultadoStock? resultado, out _));
        Assert.Equal("", resultado!.PedidoId);
    }

    [Theory]
    [InlineData("RESERVADA", "RESERVADO")]
    [InlineData("RESERVADA", "reservada")]
    [InlineData("\"OK\"", "\"TODO_BIEN\"")]
    [InlineData("stock.reservar", "stock.cancelar")]
    [InlineData("\"codigo\":\"OK\"", "\"codigo\":7")]
    [InlineData("\"mensaje\":\"Lote reservado.\"", "\"mensaje\":null")]
    public void TryLeerResultado_ValoresInvalidos_SeRechazan(string buscado, string reemplazo)
    {
        string cuerpo = ResultadoValido.Replace(buscado, reemplazo);

        Assert.False(ClienteInventario.TryLeerResultado(Resultado(cuerpo), out ResultadoStock? resultado, out string motivo));
        Assert.Null(resultado);
        Assert.False(string.IsNullOrEmpty(motivo));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void TryLeerResultado_CuerpoInvalido_SeRechaza(string cuerpo)
    {
        Assert.False(ClienteInventario.TryLeerResultado(Resultado(cuerpo), out _, out _));
    }

    [Fact]
    public void TryLeerResultado_OtroTipo_SeRechaza()
    {
        Sobre otro = Sobre.ConCuerpoTexto("catalogo.instantanea", "r-1", "m-1", "LIB", "SJ", ResultadoValido);

        Assert.False(ClienteInventario.TryLeerResultado(otro, out _, out string motivo));
        Assert.Contains("catalogo.instantanea", motivo);
    }
}
