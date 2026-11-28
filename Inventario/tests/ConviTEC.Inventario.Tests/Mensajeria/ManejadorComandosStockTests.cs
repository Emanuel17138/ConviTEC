using System;
using System.IO;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Mensajeria;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Negocio;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Mensajeria;

/// Comandos de stock desde el sobre hasta el <c>stock.resultado</c>, sin broker.
public class ManejadorComandosStockTests
{
    private const long Precio = 150000;
    private static readonly string[] Remotas = { "SJ" };

    private static (InventarioLocal Inventario, ManejadorComandosStock Manejador, DirectorioTemporal Dir) Crear(
        RepositorioInventario? repositorio = null, DirectorioTemporal? dir = null)
    {
        dir ??= new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(
            dir, "LIB", Remotas, repositorio, new[] { FabricaInventario.Stock("CAF", 5, 0, Precio) });
        return (inventario, new ManejadorComandosStock("LIB", new ProveedorStock(inventario)), dir);
    }

    private static Sobre Comando(string tipo, string mensajeId, string cuerpo, string origen = "SJ") =>
        Sobre.ConCuerpoTexto(tipo, mensajeId, mensajeId, origen, "LIB", cuerpo);

    private static Sobre Reservar(string mensajeId, string pedidoId, int cantidad) =>
        Comando("stock.reservar", mensajeId,
            $"{{\"pedidoId\":\"{pedidoId}\",\"lineas\":[{{\"productoId\":\"CAF\",\"cantidad\":{cantidad},\"precioUnitarioCentimos\":{Precio}}}]}}");

    private static Sobre Decision(string tipo, string mensajeId, string pedidoId) =>
        Comando(tipo, mensajeId, $"{{\"pedidoId\":\"{pedidoId}\"}}");

    private static ResultadoStock Leer(ResultadoDespacho despacho)
    {
        Assert.Equal(AccionConsumo.Acusar, despacho.Accion);
        SobreSaliente salida = Assert.Single(despacho.Salidas);
        Assert.True(ClienteInventario.TryLeerResultado(salida.Sobre, out ResultadoStock? resultado, out string motivo), motivo);
        return resultado!;
    }

    private static void AfirmarCantidades(InventarioLocal inventario, int existencia, int reservada)
    {
        Assert.True(inventario.BuscarStock("CAF", out StockLocal? stock));
        Assert.Equal(existencia, stock.Existencia);
        Assert.Equal(reservada, stock.Reservada);
    }

    [Fact]
    public void Tipos_SonLosTresComandosDeStock()
    {
        (_, ManejadorComandosStock manejador, DirectorioTemporal dir) = Crear();
        using (dir)
        {
            Assert.Equal(new[] { "stock.reservar", "stock.confirmar", "stock.liberar" }, manejador.Tipos);
        }
    }

    [Fact]
    public void Reservar_ResponderAPedidosDeLaCoordinadora_ConCorrelacionEIdDerivado()
    {
        (InventarioLocal inventario, ManejadorComandosStock manejador, DirectorioTemporal dir) = Crear();
        using (dir)
        {
            ResultadoDespacho despacho = manejador.Manejar(Reservar("m-1", "p-1", 2));

            SobreSaliente salida = Assert.Single(despacho.Salidas);
            Assert.Equal("pedidos.SJ", salida.ClaveRuta);
            Assert.Equal("stock.resultado", salida.Sobre.Tipo);
            Assert.Equal("m-1", salida.Sobre.CorrelacionId);
            Assert.Equal(IdentificadorDerivado.DeComando("m-1"), salida.Sobre.MensajeId);
            Assert.Equal("LIB", salida.Sobre.OrigenSedeId);
            Assert.Equal("SJ", salida.Sobre.DestinoSedeId);

            ResultadoStock resultado = Leer(despacho);
            Assert.Equal("p-1", resultado.PedidoId);
            Assert.Equal("stock.reservar", resultado.Operacion);
            Assert.Equal(EstadoReserva.Reservada, resultado.EstadoReserva);
            Assert.Equal(CodigosResultado.Ok, resultado.Codigo);
            AfirmarCantidades(inventario, 5, 2);
        }
    }

    [Fact]
    public void EjemploDelContrato_Reservar_Confirmar_5_2_luego_3_0()
    {
        (InventarioLocal inventario, ManejadorComandosStock manejador, DirectorioTemporal dir) = Crear();
        using (dir)
        {
            manejador.Manejar(Reservar("m-1", "p-1", 2));
            ResultadoStock confirmado = Leer(manejador.Manejar(Decision("stock.confirmar", "m-2", "p-1")));

            Assert.Equal(EstadoReserva.Confirmada, confirmado.EstadoReserva);
            AfirmarCantidades(inventario, 3, 0);
        }
    }

    [Fact]
    public void Liberar_DevuelveLoReservado()
    {
        (InventarioLocal inventario, ManejadorComandosStock manejador, DirectorioTemporal dir) = Crear();
        using (dir)
        {
            manejador.Manejar(Reservar("m-1", "p-1", 2));
            ResultadoStock liberado = Leer(manejador.Manejar(Decision("stock.liberar", "m-2", "p-1")));

            Assert.Equal(EstadoReserva.Liberada, liberado.EstadoReserva);
            AfirmarCantidades(inventario, 5, 0);
        }
    }

    [Fact]
    public void MensajeDuplicado_DevuelveElMismoResultadoYElMismoIdDeRespuesta_SinSegundoEfecto()
    {
        (InventarioLocal inventario, ManejadorComandosStock manejador, DirectorioTemporal dir) = Crear();
        using (dir)
        {
            manejador.Manejar(Reservar("m-1", "p-1", 2));
            ResultadoDespacho confirmar1 = manejador.Manejar(Decision("stock.confirmar", "m-2", "p-1"));
            ResultadoDespacho confirmar2 = manejador.Manejar(Decision("stock.confirmar", "m-2", "p-1"));

            Assert.Equal(confirmar1.Salidas[0].Sobre.MensajeId, confirmar2.Salidas[0].Sobre.MensajeId);
            Assert.Equal(Leer(confirmar1).EstadoReserva, Leer(confirmar2).EstadoReserva);
            AfirmarCantidades(inventario, 3, 0);
        }
    }

    [Fact]
    public void ReservaOriginalReenviadaTrasConfirmar_DevuelveReservadaOriginal()
    {
        (InventarioLocal inventario, ManejadorComandosStock manejador, DirectorioTemporal dir) = Crear();
        using (dir)
        {
            manejador.Manejar(Reservar("m-1", "p-1", 2));
            manejador.Manejar(Decision("stock.confirmar", "m-2", "p-1"));

            ResultadoStock repetido = Leer(manejador.Manejar(Reservar("m-1", "p-1", 2)));

            Assert.Equal(EstadoReserva.Reservada, repetido.EstadoReserva);
            Assert.Equal(CodigosResultado.Ok, repetido.Codigo);
            AfirmarCantidades(inventario, 3, 0);
        }
    }

    [Fact]
    public void MismoMensajeIdConOtroContenido_ClaveReutilizada_SinEfecto()
    {
        (InventarioLocal inventario, ManejadorComandosStock manejador, DirectorioTemporal dir) = Crear();
        using (dir)
        {
            manejador.Manejar(Reservar("m-1", "p-1", 2));

            ResultadoStock resultado = Leer(manejador.Manejar(Reservar("m-1", "p-1", 3)));

            Assert.Equal(CodigosResultado.ClaveReutilizada, resultado.Codigo);
            AfirmarCantidades(inventario, 5, 2);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"pedidoId\":7,\"lineas\":\"x\"}")]
    [InlineData("{\"pedidoId\":\"p-1\"}")]
    [InlineData("{\"pedidoId\":\"p-1\",\"lineas\":[]}")]
    [InlineData("{\"pedidoId\":\"p-1\",\"lineas\":[null]}")]
    [InlineData("{\"pedidoId\":\"p-1\",\"lineas\":[{\"productoId\":\"CAF\",\"cantidad\":2.5,\"precioUnitarioCentimos\":150000}]}")]
    [InlineData("{\"pedidoId\":\"p-1\",\"lineas\":[{\"productoId\":\"CAF\",\"cantidad\":\"2\",\"precioUnitarioCentimos\":150000}]}")]
    [InlineData("{\"pedidoId\":\"p-1\",\"lineas\":[{\"productoId\":\"CAF\",\"cantidad\":2}]}")]
    public void ReservarConCuerpoMalformado_DatosInvalidos_SinCambios(string cuerpo)
    {
        (InventarioLocal inventario, ManejadorComandosStock manejador, DirectorioTemporal dir) = Crear();
        using (dir)
        {
            ResultadoStock resultado = Leer(manejador.Manejar(Comando("stock.reservar", "m-1", cuerpo)));

            Assert.Equal(CodigosResultado.DatosInvalidos, resultado.Codigo);
            AfirmarCantidades(inventario, 5, 0);
        }
    }

    [Fact]
    public void ConfirmarSinPedidoId_DatosInvalidos()
    {
        (_, ManejadorComandosStock manejador, DirectorioTemporal dir) = Crear();
        using (dir)
        {
            ResultadoStock resultado = Leer(manejador.Manejar(Comando("stock.confirmar", "m-1", "{}")));

            Assert.Equal(CodigosResultado.DatosInvalidos, resultado.Codigo);
            Assert.Equal("", resultado.PedidoId);
        }
    }

    [Fact]
    public void ConfirmarSinReserva_Inexistente()
    {
        (_, ManejadorComandosStock manejador, DirectorioTemporal dir) = Crear();
        using (dir)
        {
            ResultadoStock resultado = Leer(manejador.Manejar(Decision("stock.confirmar", "m-1", "p-9")));

            Assert.Equal(EstadoReserva.Inexistente, resultado.EstadoReserva);
            Assert.Equal(CodigosResultado.EstadoIncompatible, resultado.Codigo);
        }
    }

    [Fact]
    public void LiberarAntesDeReservar_LaReservaTardiaEsIncompatible()
    {
        (InventarioLocal inventario, ManejadorComandosStock manejador, DirectorioTemporal dir) = Crear();
        using (dir)
        {
            manejador.Manejar(Decision("stock.liberar", "m-1", "p-1"));

            ResultadoStock tardia = Leer(manejador.Manejar(Reservar("m-2", "p-1", 2)));

            Assert.Equal(CodigosResultado.EstadoIncompatible, tardia.Codigo);
            AfirmarCantidades(inventario, 5, 0);
        }
    }

    [Fact]
    public void OtraCoordinadora_RespondeAlEmisorConEstadoIncompatible()
    {
        (InventarioLocal inventario, ManejadorComandosStock manejador, DirectorioTemporal dir) = Crear();
        using (dir)
        {
            manejador.Manejar(Reservar("m-1", "p-1", 2));

            ResultadoDespacho despacho = manejador.Manejar(Comando("stock.confirmar", "m-2", "{\"pedidoId\":\"p-1\"}", origen: "ALA"));

            Assert.Equal("pedidos.ALA", despacho.Salidas[0].ClaveRuta);
            Assert.Equal(CodigosResultado.EstadoIncompatible, Leer(despacho).Codigo);
            AfirmarCantidades(inventario, 5, 2);
        }
    }

    [Fact]
    public void FalloDeGuardado_Reintentar_SinRespuestaNiCambios_YAlRepetirSeProcesaComoNuevo()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        bool cortar = true;
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"), etapa =>
        {
            if (cortar && etapa == EtapaGuardado.TemporalEscrito)
            {
                throw new IOException("disco lleno (simulado)");
            }
        });
        (InventarioLocal inventario, ManejadorComandosStock manejador, _) = Crear(repositorio, dir);

        ResultadoDespacho fallido = manejador.Manejar(Reservar("m-1", "p-1", 2));

        Assert.Equal(AccionConsumo.Reintentar, fallido.Accion);
        Assert.Empty(fallido.Salidas);
        AfirmarCantidades(inventario, 5, 0);

        cortar = false;
        ResultadoStock reintento = Leer(manejador.Manejar(Reservar("m-1", "p-1", 2)));

        Assert.Equal(EstadoReserva.Reservada, reintento.EstadoReserva);
        AfirmarCantidades(inventario, 5, 2);
    }

    [Fact]
    public void Constructor_ArgumentosInvalidos_Lanza()
    {
        Assert.Throws<ArgumentException>(() => new ManejadorComandosStock(" ", null!));
        Assert.Throws<ArgumentNullException>(() => new ManejadorComandosStock("LIB", null!));
    }
}
