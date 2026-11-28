using System;
using System.Threading.Tasks;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Negocio;

/// Lado proveedor de RF02: reservar, confirmar y liberar lotes, con idempotencia por mensajeId.
public class ProveedorStockTests
{
    private const long Precio = 150000;
    private static readonly string[] Remotas = { "SJ" };

    private static StockLocal Stock(string id, int existencia, int reservada) => FabricaInventario.Stock(id, existencia, reservada, Precio);

    private static (InventarioLocal Inventario, ProveedorStock Proveedor, DirectorioTemporal Dir) Crear(params StockLocal[] stocks)
    {
        DirectorioTemporal dir = new DirectorioTemporal();
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, null, stocks);
        return (inventario, new ProveedorStock(inventario), dir);
    }

    private static LineaSolicitada Linea(string productoId, int cantidad, long precio = Precio) => new LineaSolicitada(productoId, cantidad, precio);

    private static SolicitudReserva Reserva(string mensajeId, string pedidoId, params LineaSolicitada?[] lineas) =>
        new SolicitudReserva(mensajeId, "SJ", pedidoId, lineas);

    private static SolicitudDecision Decision(string mensajeId, string pedidoId, string origen = "SJ") =>
        new SolicitudDecision(mensajeId, origen, pedidoId);

    private static void AfirmarCantidades(InventarioLocal inventario, string productoId, int existencia, int reservada)
    {
        Assert.True(inventario.BuscarStock(productoId, out StockLocal? stock));
        Assert.Equal(existencia, stock.Existencia);
        Assert.Equal(reservada, stock.Reservada);
        Assert.Equal(existencia - reservada, stock.Disponible);
    }

    private static void AfirmarResultado(ResultadoStock r, string operacion, EstadoReserva estado, string codigo)
    {
        Assert.Equal(operacion, r.Operacion);
        Assert.Equal(estado, r.EstadoReserva);
        Assert.Equal(codigo, r.Codigo);
        Assert.False(string.IsNullOrEmpty(r.Mensaje));
    }

    private static long Revision(InventarioLocal inventario, string productoId)
    {
        Assert.True(inventario.BuscarRevision(productoId, out long revision));
        return revision;
    }

    // ---- Flujo feliz (ejemplo del contrato) ----

    [Fact]
    public void ReservarYConfirmar_EjemploDelContrato_5_0_luego_5_2_luego_3_0()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            ResultadoStock reservado = proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));
            AfirmarResultado(reservado, "stock.reservar", EstadoReserva.Reservada, CodigosResultado.Ok);
            Assert.Equal("p-1", reservado.PedidoId);
            AfirmarCantidades(inventario, "CAF", 5, 2);
            Assert.Equal(3, inventario.ConsultarInventario()[0].Disponible);

            ResultadoStock confirmado = proveedor.Confirmar(Decision("m-2", "p-1"));
            AfirmarResultado(confirmado, "stock.confirmar", EstadoReserva.Confirmada, CodigosResultado.Ok);
            AfirmarCantidades(inventario, "CAF", 3, 0);
            Assert.Equal(2, Revision(inventario, "CAF")); // confirmar sube la revisión aunque el disponible siga en 3

            SalidaPendiente salida = Assert.Single(inventario.ConsultarSalidasPendientes());
            Assert.Equal(3, salida.Disponible);
            Assert.Equal(2, salida.Revision);
            Assert.True(inventario.BuscarLote("p-1", out LoteReserva? lote));
            Assert.Equal(EstadoReserva.Confirmada, lote.Estado);
        }
    }

    [Fact]
    public void ReservarYLiberar_EjemploDelContrato_5_0_luego_5_2_luego_5_0()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));

            ResultadoStock liberado = proveedor.Liberar(Decision("m-2", "p-1"));

            AfirmarResultado(liberado, "stock.liberar", EstadoReserva.Liberada, CodigosResultado.Ok);
            AfirmarCantidades(inventario, "CAF", 5, 0);
            Assert.Equal(2, Revision(inventario, "CAF"));
        }
    }

    [Fact]
    public void Reservar_LoteConVariasLineas_RetieneTodasLasLineas()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0), Stock("ARR", 4, 1));
        using (dir)
        {
            ResultadoStock r = proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2), Linea("ARR", 3)));

            AfirmarResultado(r, "stock.reservar", EstadoReserva.Reservada, CodigosResultado.Ok);
            AfirmarCantidades(inventario, "CAF", 5, 2);
            AfirmarCantidades(inventario, "ARR", 4, 4);
            Assert.Equal(1, Revision(inventario, "CAF"));
            Assert.Equal(1, Revision(inventario, "ARR"));
        }
    }

    [Fact]
    public void Reservar_TodoElDisponible_EsValido()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            AfirmarResultado(proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 5))), "stock.reservar", EstadoReserva.Reservada, CodigosResultado.Ok);
            AfirmarCantidades(inventario, "CAF", 5, 5);
        }
    }

    // ---- Rechazos: ninguna línea se retiene ----

    [Fact]
    public async Task Reservar_DosSolicitudesConcurrentesDe4_Con5Disponibles_SoloUnLoteQuedaReservado()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            ResultadoStock[] resultados = new ResultadoStock[2];
            Task[] tareas = new Task[2];
            for (int t = 0; t < 2; t++)
            {
                int i = t;
                tareas[i] = Task.Run(() => resultados[i] = proveedor.Reservar(Reserva("m-" + i, "p-" + i, Linea("CAF", 4))));
            }
            await Task.WhenAll(tareas);

            int reservadas = 0;
            int rechazadas = 0;
            for (int i = 0; i < 2; i++)
            {
                if (resultados[i].EstadoReserva == EstadoReserva.Reservada)
                {
                    reservadas++;
                }
                else
                {
                    rechazadas++;
                    Assert.Equal(EstadoReserva.Rechazada, resultados[i].EstadoReserva);
                    Assert.Equal(CodigosResultado.StockInsuficiente, resultados[i].Codigo);
                }
            }
            Assert.Equal(1, reservadas);
            Assert.Equal(1, rechazadas);
            Assert.True(inventario.BuscarStock("CAF", out StockLocal? stock));
            Assert.Equal(1, stock.Disponible);
        }
    }

    [Fact]
    public void Reservar_UnaLineaValidaYOtraAgotada_NoRetieneNingunaLinea()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0), Stock("ARR", 2, 2));
        using (dir)
        {
            ResultadoStock r = proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2), Linea("ARR", 1)));

            AfirmarResultado(r, "stock.reservar", EstadoReserva.Rechazada, CodigosResultado.StockInsuficiente);
            Assert.Contains("ARR", r.Mensaje);
            AfirmarCantidades(inventario, "CAF", 5, 0);
            AfirmarCantidades(inventario, "ARR", 2, 2);
            Assert.Equal(0, Revision(inventario, "CAF"));
            Assert.Empty(inventario.ConsultarSalidasPendientes());
            Assert.True(inventario.BuscarLote("p-1", out LoteReserva? lote));
            Assert.Equal(EstadoReserva.Rechazada, lote.Estado);
        }
    }

    [Fact]
    public void Reservar_ProductoDesconocidoOMayusculasDistintas_ProductoDesconocido()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            ResultadoStock distinto = proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 1), Linea("XYZ", 1)));
            ResultadoStock mayusculas = proveedor.Reservar(Reserva("m-2", "p-2", Linea("caf", 1)));

            AfirmarResultado(distinto, "stock.reservar", EstadoReserva.Rechazada, CodigosResultado.ProductoDesconocido);
            AfirmarResultado(mayusculas, "stock.reservar", EstadoReserva.Rechazada, CodigosResultado.ProductoDesconocido);
            AfirmarCantidades(inventario, "CAF", 5, 0);
        }
    }

    [Fact]
    public void Reservar_PrecioDistinto_RechazaSinRetener()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0), Stock("ARR", 5, 0));
        using (dir)
        {
            ResultadoStock r = proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 1), Linea("ARR", 1, Precio + 1)));

            AfirmarResultado(r, "stock.reservar", EstadoReserva.Rechazada, CodigosResultado.PrecioDistinto);
            AfirmarCantidades(inventario, "CAF", 5, 0);
            AfirmarCantidades(inventario, "ARR", 5, 0);
        }
    }

    [Fact]
    public void Reservar_DatosInvalidos_RechazaSinRetenerNiCambiarRevision()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            LineaSolicitada?[][] casos =
            {
                new LineaSolicitada?[] { Linea("CAF", 1), Linea("CAF", 2) },        // producto repetido
                new LineaSolicitada?[] { Linea("CAF", 0) },                         // cantidad cero
                new LineaSolicitada?[] { Linea("CAF", -3) },                        // cantidad negativa
                new LineaSolicitada?[] { new LineaSolicitada("CAF", null, Precio) }, // sin cantidad
                new LineaSolicitada?[] { new LineaSolicitada("CAF", 1, null) },     // sin precio
                new LineaSolicitada?[] { new LineaSolicitada(null, 1, Precio) },    // sin productoId
                new LineaSolicitada?[] { null },                                    // línea nula
                new LineaSolicitada?[0],                                            // sin líneas
            };

            for (int i = 0; i < casos.Length; i++)
            {
                ResultadoStock r = proveedor.Reservar(new SolicitudReserva("m-" + i, "SJ", "p-" + i, casos[i]));
                AfirmarResultado(r, "stock.reservar", EstadoReserva.Rechazada, CodigosResultado.DatosInvalidos);
            }
            ResultadoStock sinLineas = proveedor.Reservar(new SolicitudReserva("m-n", "SJ", "p-n", null));
            AfirmarResultado(sinLineas, "stock.reservar", EstadoReserva.Rechazada, CodigosResultado.DatosInvalidos);

            AfirmarCantidades(inventario, "CAF", 5, 0);
            Assert.Equal(0, Revision(inventario, "CAF"));
        }
    }

    [Fact]
    public void Reservar_SinPedidoId_DatosInvalidosYNoCreaLote()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            ResultadoStock nulo = proveedor.Reservar(new SolicitudReserva("m-1", "SJ", null, new LineaSolicitada?[] { Linea("CAF", 1) }));
            ResultadoStock vacio = proveedor.Reservar(Reserva("m-2", "  ", Linea("CAF", 1)));

            AfirmarResultado(nulo, "stock.reservar", EstadoReserva.Rechazada, CodigosResultado.DatosInvalidos);
            AfirmarResultado(vacio, "stock.reservar", EstadoReserva.Rechazada, CodigosResultado.DatosInvalidos);
            Assert.Equal(string.Empty, nulo.PedidoId);
            AfirmarCantidades(inventario, "CAF", 5, 0);
            Assert.True(inventario.BuscarComando("m-1", out _));
        }
    }

    [Fact]
    public void Reservar_RechazoPorStock_QuedaDurableTrasReiniciar()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 1, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 9)));

            InventarioLocal recargado = FabricaInventario.Recargar(dir, "LIB", Remotas);

            Assert.True(recargado.BuscarLote("p-1", out LoteReserva? lote));
            Assert.Equal(EstadoReserva.Rechazada, lote.Estado);
            Assert.True(recargado.BuscarComando("m-1", out ComandoRegistrado? comando));
            Assert.Equal(CodigosResultado.StockInsuficiente, comando.Resultado.Codigo);
            AfirmarCantidades(recargado, "CAF", 1, 0);
        }
    }

    // ---- Liberación anticipada y estados incompatibles ----

    [Fact]
    public void Liberar_AntesDeLaReserva_QuedaGuardadaYLaReservaTardiaNoRetieneNada()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            ResultadoStock liberado = proveedor.Liberar(Decision("m-1", "p-1"));
            AfirmarResultado(liberado, "stock.liberar", EstadoReserva.Liberada, CodigosResultado.Ok);
            Assert.True(inventario.BuscarLote("p-1", out LoteReserva? lote));
            Assert.Equal(EstadoReserva.Liberada, lote.Estado);

            ResultadoStock tardia = proveedor.Reservar(Reserva("m-2", "p-1", Linea("CAF", 2)));

            AfirmarResultado(tardia, "stock.reservar", EstadoReserva.Liberada, CodigosResultado.EstadoIncompatible);
            AfirmarCantidades(inventario, "CAF", 5, 0);
            Assert.Equal(0, Revision(inventario, "CAF"));
            AfirmarCantidades(FabricaInventario.Recargar(dir, "LIB", Remotas), "CAF", 5, 0);
        }
    }

    [Fact]
    public void Confirmar_SinReserva_Inexistente()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            ResultadoStock r = proveedor.Confirmar(Decision("m-1", "p-1"));

            AfirmarResultado(r, "stock.confirmar", EstadoReserva.Inexistente, CodigosResultado.EstadoIncompatible);
            Assert.False(inventario.BuscarLote("p-1", out _));
            AfirmarCantidades(inventario, "CAF", 5, 0);
        }
    }

    [Fact]
    public void Liberar_TrasUnRechazo_CierraEnLiberadaSinMoverUnidades()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 1, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 3)));

            ResultadoStock r = proveedor.Liberar(Decision("m-2", "p-1"));

            AfirmarResultado(r, "stock.liberar", EstadoReserva.Liberada, CodigosResultado.Ok);
            Assert.True(inventario.BuscarLote("p-1", out LoteReserva? lote));
            Assert.Equal(EstadoReserva.Liberada, lote.Estado);
            AfirmarCantidades(inventario, "CAF", 1, 0);
        }
    }

    [Fact]
    public void Liberar_DespuesDeConfirmar_EsIncompatibleYNoDevuelveUnidades()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));
            proveedor.Confirmar(Decision("m-2", "p-1"));

            ResultadoStock r = proveedor.Liberar(Decision("m-3", "p-1"));

            AfirmarResultado(r, "stock.liberar", EstadoReserva.Confirmada, CodigosResultado.EstadoIncompatible);
            AfirmarCantidades(inventario, "CAF", 3, 0);
        }
    }

    [Fact]
    public void Confirmar_UnLoteLiberadoORechazado_EsIncompatible()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));
            proveedor.Liberar(Decision("m-2", "p-1"));
            proveedor.Reservar(Reserva("m-3", "p-2", Linea("CAF", 9))); // rechazado

            ResultadoStock liberado = proveedor.Confirmar(Decision("m-4", "p-1"));
            ResultadoStock rechazado = proveedor.Confirmar(Decision("m-5", "p-2"));

            AfirmarResultado(liberado, "stock.confirmar", EstadoReserva.Liberada, CodigosResultado.EstadoIncompatible);
            AfirmarResultado(rechazado, "stock.confirmar", EstadoReserva.Rechazada, CodigosResultado.EstadoIncompatible);
            AfirmarCantidades(inventario, "CAF", 5, 0);
        }
    }

    [Fact]
    public void Decision_SinPedidoId_DatosInvalidos()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            AfirmarResultado(proveedor.Confirmar(new SolicitudDecision("m-1", "SJ", null)), "stock.confirmar", EstadoReserva.Inexistente, CodigosResultado.DatosInvalidos);
            AfirmarResultado(proveedor.Liberar(new SolicitudDecision("m-2", "SJ", "")), "stock.liberar", EstadoReserva.Inexistente, CodigosResultado.DatosInvalidos);
            AfirmarCantidades(inventario, "CAF", 5, 0);
        }
    }

    // ---- Idempotencia ----

    [Fact]
    public void Confirmar_DosVecesConOtroMensajeId_DescuentaUnaSolaVez()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));
            proveedor.Confirmar(Decision("m-2", "p-1"));
            long revision = Revision(inventario, "CAF");

            ResultadoStock segunda = proveedor.Confirmar(Decision("m-3", "p-1"));

            AfirmarResultado(segunda, "stock.confirmar", EstadoReserva.Confirmada, CodigosResultado.Ok);
            AfirmarCantidades(inventario, "CAF", 3, 0);
            Assert.Equal(revision, Revision(inventario, "CAF"));
        }
    }

    [Fact]
    public void Confirmar_ReinicioEntreElDescuentoYElAcuse_RepetirDevuelveExitoSinDescontarDeNuevo()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));
            ResultadoStock original = proveedor.Confirmar(Decision("m-2", "p-1"));

            InventarioLocal reiniciado = FabricaInventario.Recargar(dir, "LIB", Remotas);
            ResultadoStock repetido = new ProveedorStock(reiniciado).Confirmar(Decision("m-2", "p-1"));

            Assert.Equal(original.Codigo, repetido.Codigo);
            Assert.Equal(original.EstadoReserva, repetido.EstadoReserva);
            Assert.Equal(original.Mensaje, repetido.Mensaje);
            AfirmarResultado(repetido, "stock.confirmar", EstadoReserva.Confirmada, CodigosResultado.Ok);
            AfirmarCantidades(reiniciado, "CAF", 3, 0);
            Assert.Equal(2, Revision(reiniciado, "CAF"));
        }
    }

    [Fact]
    public void Reservar_DuplicadoExactoDespuesDeConfirmar_DevuelveElResultadoOriginalReservada()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));
            proveedor.Confirmar(Decision("m-2", "p-1"));

            ResultadoStock duplicado = proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));

            AfirmarResultado(duplicado, "stock.reservar", EstadoReserva.Reservada, CodigosResultado.Ok);
            AfirmarCantidades(inventario, "CAF", 3, 0);
        }
    }

    [Fact]
    public void MismoMensajeIdConOtroContenido_ClaveReutilizadaSinEfecto()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));

            ResultadoStock otraCantidad = proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 3)));
            ResultadoStock otroPedido = proveedor.Reservar(Reserva("m-1", "p-9", Linea("CAF", 2)));
            ResultadoStock otroTipo = proveedor.Confirmar(Decision("m-1", "p-1"));
            ResultadoStock otroOrigen = proveedor.Reservar(new SolicitudReserva("m-1", "ALA", "p-1", new LineaSolicitada?[] { Linea("CAF", 2) }));

            AfirmarResultado(otraCantidad, "stock.reservar", EstadoReserva.Reservada, CodigosResultado.ClaveReutilizada);
            AfirmarResultado(otroPedido, "stock.reservar", EstadoReserva.Rechazada, CodigosResultado.ClaveReutilizada);
            AfirmarResultado(otroTipo, "stock.confirmar", EstadoReserva.Reservada, CodigosResultado.ClaveReutilizada);
            AfirmarResultado(otroOrigen, "stock.reservar", EstadoReserva.Reservada, CodigosResultado.ClaveReutilizada);
            AfirmarCantidades(inventario, "CAF", 5, 2);
            Assert.False(inventario.BuscarLote("p-9", out _));
            // El comando original sigue intacto.
            Assert.True(inventario.BuscarComando("m-1", out ComandoRegistrado? original));
            Assert.Equal(CodigosResultado.Ok, original.Resultado.Codigo);
        }
    }

    [Fact]
    public void Liberar_RepetidaConOtroMensajeId_EsExito()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));
            proveedor.Liberar(Decision("m-2", "p-1"));
            long revision = Revision(inventario, "CAF");

            ResultadoStock segunda = proveedor.Liberar(Decision("m-3", "p-1"));

            AfirmarResultado(segunda, "stock.liberar", EstadoReserva.Liberada, CodigosResultado.Ok);
            AfirmarCantidades(inventario, "CAF", 5, 0);
            Assert.Equal(revision, Revision(inventario, "CAF"));
        }
    }

    [Fact]
    public void Reservar_OtroContenidoParaElMismoPedido_SeRechazaSinTocarElLoteOriginal()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));

            ResultadoStock otra = proveedor.Reservar(Reserva("m-2", "p-1", Linea("CAF", 1)));

            AfirmarResultado(otra, "stock.reservar", EstadoReserva.Reservada, CodigosResultado.EstadoIncompatible);
            AfirmarCantidades(inventario, "CAF", 5, 2);
            Assert.True(inventario.BuscarLote("p-1", out LoteReserva? lote));
            Assert.Equal(EstadoReserva.Reservada, lote.Estado);
            Assert.Equal(2, Assert.Single(lote.Lineas()).Cantidad);
        }
    }

    [Fact]
    public void ComandoDeOtraCoordinadora_EsIncompatibleYNoCambiaUnidades()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));

            ResultadoStock confirmar = proveedor.Confirmar(Decision("m-2", "p-1", "ALA"));
            ResultadoStock liberar = proveedor.Liberar(Decision("m-3", "p-1", "ALA"));

            AfirmarResultado(confirmar, "stock.confirmar", EstadoReserva.Reservada, CodigosResultado.EstadoIncompatible);
            AfirmarResultado(liberar, "stock.liberar", EstadoReserva.Reservada, CodigosResultado.EstadoIncompatible);
            AfirmarCantidades(inventario, "CAF", 5, 2);
            Assert.True(inventario.BuscarLote("p-1", out LoteReserva? lote));
            Assert.Equal(EstadoReserva.Reservada, lote.Estado);
            Assert.Equal("SJ", lote.CoordinadoraSedeId);
        }
    }

    [Fact]
    public void ComandoDeLaPropiaSede_SeProcesaConLasMismasReglas()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            ResultadoStock r = proveedor.Reservar(new SolicitudReserva("m-1", "LIB", "p-1", new LineaSolicitada?[] { Linea("CAF", 2) }));

            AfirmarResultado(r, "stock.reservar", EstadoReserva.Reservada, CodigosResultado.Ok);
            AfirmarCantidades(inventario, "CAF", 5, 2);
        }
    }

    // ---- Persistencia ----

    [Fact]
    public void Reservar_DejaLoteCantidadesRevisionResultadoYSalidaDurablesJuntos()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2)));

            InventarioLocal recargado = FabricaInventario.Recargar(dir, "LIB", Remotas);

            AfirmarCantidades(recargado, "CAF", 5, 2);
            Assert.Equal(1, Revision(recargado, "CAF"));
            Assert.True(recargado.BuscarLote("p-1", out LoteReserva? lote));
            Assert.Equal(EstadoReserva.Reservada, lote.Estado);
            Assert.True(recargado.BuscarComando("m-1", out ComandoRegistrado? comando));
            Assert.Equal(EstadoReserva.Reservada, comando.Resultado.EstadoReserva);
            SalidaPendiente salida = Assert.Single(recargado.ConsultarSalidasPendientes());
            Assert.Equal(3, salida.Disponible);
            Assert.Equal(1, salida.Revision);
        }
    }

    [Fact]
    public void CorteAntesDeReemplazarElSnapshot_NoQuedaNiLoteNiResultadoNiRetencion()
    {
        using DirectorioTemporal dir = new DirectorioTemporal();
        bool cortar = false;
        RepositorioInventario repositorio = new RepositorioInventario(dir.Rutas("LIB"), etapa =>
        {
            if (cortar && etapa == EtapaGuardado.TemporalEscrito)
            {
                throw new InvalidOperationException("corte simulado");
            }
        });
        InventarioLocal inventario = FabricaInventario.CrearCon(dir, "LIB", Remotas, repositorio, new[] { Stock("CAF", 5, 0) });
        ProveedorStock proveedor = new ProveedorStock(inventario);
        cortar = true;

        Assert.Throws<InvalidOperationException>(() => proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2))));

        AfirmarCantidades(inventario, "CAF", 5, 0);
        Assert.False(inventario.BuscarLote("p-1", out _));
        Assert.False(inventario.BuscarComando("m-1", out _));
        InventarioLocal recargado = FabricaInventario.Recargar(dir, "LIB", Remotas);
        AfirmarCantidades(recargado, "CAF", 5, 0);
        Assert.False(recargado.BuscarComando("m-1", out _));

        // Al reintentar el mismo comando tras la recuperación, se procesa como nuevo y queda un solo efecto.
        cortar = false;
        AfirmarResultado(proveedor.Reservar(Reserva("m-1", "p-1", Linea("CAF", 2))), "stock.reservar", EstadoReserva.Reservada, CodigosResultado.Ok);
        AfirmarCantidades(inventario, "CAF", 5, 2);
    }

    // ---- Argumentos ----

    [Fact]
    public void Argumentos_Invalidos_Lanzan()
    {
        (InventarioLocal inventario, ProveedorStock proveedor, DirectorioTemporal dir) = Crear(Stock("CAF", 5, 0));
        using (dir)
        {
            Assert.Throws<ArgumentNullException>(() => new ProveedorStock(null!));
            Assert.Throws<ArgumentNullException>(() => proveedor.Reservar(null!));
            Assert.Throws<ArgumentNullException>(() => proveedor.Confirmar(null!));
            Assert.Throws<ArgumentNullException>(() => proveedor.Liberar(null!));
            Assert.ThrowsAny<ArgumentException>(() => new SolicitudReserva("", "SJ", "p-1", null));
            Assert.ThrowsAny<ArgumentException>(() => new SolicitudReserva("m-1", " ", "p-1", null));
            Assert.ThrowsAny<ArgumentException>(() => new SolicitudDecision(null!, "SJ", "p-1"));
            Assert.ThrowsAny<ArgumentException>(() => new SolicitudDecision("m-1", "", "p-1"));
            AfirmarCantidades(inventario, "CAF", 5, 0);
        }
    }

    [Fact]
    public void HuellaDeReserva_EsEstableYDistingueContenidos()
    {
        string a = HuellaComando.DeReserva(Reserva("x", "p-1", Linea("CAF", 2)));
        string igual = HuellaComando.DeReserva(Reserva("otro-id", "p-1", Linea("CAF", 2)));
        string distinta = HuellaComando.DeReserva(Reserva("x", "p-1", Linea("CAF", 3)));
        string otroOrden = HuellaComando.DeReserva(Reserva("x", "p-1", Linea("ARR", 1), Linea("CAF", 2)));
        string orden = HuellaComando.DeReserva(Reserva("x", "p-1", Linea("CAF", 2), Linea("ARR", 1)));

        Assert.Equal(a, igual);
        Assert.NotEqual(a, distinta);
        Assert.NotEqual(otroOrden, orden);
    }
}
