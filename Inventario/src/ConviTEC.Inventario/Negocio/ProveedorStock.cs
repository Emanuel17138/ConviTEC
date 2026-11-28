using System;
using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Negocio;

/// Lado proveedor de los comandos de stock (<c>stock.reservar</c>, <c>stock.confirmar</c>, <c>stock.liberar</c>).
/// Cada comando corre entero dentro de <see cref="InventarioLocal.Aplicar{T}"/>: exclusión mutua local, cambio
/// completo (cantidades, lote y resultado) guardado de forma atómica y resultado devuelto solo después de persistir.
/// Un mensajeId repetido con el mismo contenido devuelve el resultado original sin tocar nada, aunque el lote
/// haya avanzado; con otro contenido da CLAVE_REUTILIZADA sin efecto.
public sealed class ProveedorStock
{
    private readonly InventarioLocal _inventario;

    public ProveedorStock(InventarioLocal inventario)
    {
        ArgumentNullException.ThrowIfNull(inventario, nameof(inventario));
        _inventario = inventario;
    }

    public ResultadoStock Reservar(SolicitudReserva solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud, nameof(solicitud));
        string huella = HuellaComando.DeReserva(solicitud);
        return Procesar(TiposComandoStock.Reservar, solicitud.MensajeId, solicitud.OrigenSedeId, solicitud.PedidoId,
            huella, EstadoReserva.Rechazada, (cambio, pedidoId) => ReservarLote(cambio, solicitud, pedidoId));
    }

    public ResultadoStock Confirmar(SolicitudDecision solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud, nameof(solicitud));
        return Procesar(TiposComandoStock.Confirmar, solicitud.MensajeId, solicitud.OrigenSedeId, solicitud.PedidoId,
            HuellaComando.DeDecision(solicitud), EstadoReserva.Inexistente, ConfirmarLote);
    }

    public ResultadoStock Liberar(SolicitudDecision solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud, nameof(solicitud));
        return Procesar(TiposComandoStock.Liberar, solicitud.MensajeId, solicitud.OrigenSedeId, solicitud.PedidoId,
            HuellaComando.DeDecision(solicitud), EstadoReserva.Inexistente, (cambio, pedidoId) => LiberarLote(cambio, solicitud, pedidoId));
    }

    /// Pasos comunes: duplicados, pedidoId válido y coordinadora autorizada. Lo demás lo decide <paramref name="efecto"/>.
    /// <paramref name="estadoSinLote"/> es el estado que se informa cuando no hay lote que describir.
    private ResultadoStock Procesar(
        string tipo, string mensajeId, string origenSedeId, string? pedidoIdRecibido, string huella,
        EstadoReserva estadoSinLote, Func<CambioInventario, string, ResultadoStock> efecto)
    {
        return _inventario.Aplicar(cambio =>
        {
            string pedidoMostrado = pedidoIdRecibido ?? string.Empty;

            if (cambio.BuscarComando(mensajeId, out ComandoRegistrado? previo))
            {
                if (EsMismoComando(previo, tipo, origenSedeId, huella))
                {
                    return previo.Resultado;
                }
                EstadoReserva estadoActual = cambio.BuscarLote(pedidoMostrado, out LoteReserva? existente) ? existente.Estado : estadoSinLote;
                return Resultado(tipo, pedidoMostrado, estadoActual, CodigosResultado.ClaveReutilizada,
                    $"El mensajeId '{mensajeId}' ya se usó con otro contenido.");
            }

            ResultadoStock resultado;
            if (string.IsNullOrWhiteSpace(pedidoIdRecibido))
            {
                resultado = Resultado(tipo, string.Empty, estadoSinLote, CodigosResultado.DatosInvalidos, "Falta el pedidoId.");
            }
            else if (cambio.BuscarLote(pedidoIdRecibido, out LoteReserva? lote) && lote.CoordinadoraSedeId != origenSedeId)
            {
                resultado = Resultado(tipo, pedidoIdRecibido, lote.Estado, CodigosResultado.EstadoIncompatible,
                    $"El pedido '{pedidoIdRecibido}' pertenece a otra coordinadora.");
            }
            else
            {
                resultado = efecto(cambio, pedidoIdRecibido);
            }

            cambio.RegistrarComando(new ComandoRegistrado(mensajeId, tipo, origenSedeId, huella, resultado));
            return resultado;
        });
    }

    private static bool EsMismoComando(ComandoRegistrado previo, string tipo, string origenSedeId, string huella) =>
        string.Equals(previo.Tipo, tipo, StringComparison.Ordinal)
        && string.Equals(previo.OrigenSedeId, origenSedeId, StringComparison.Ordinal)
        && string.Equals(previo.Contenido, huella, StringComparison.Ordinal);

    private static ResultadoStock Resultado(string tipo, string pedidoId, EstadoReserva estado, string codigo, string mensaje) =>
        new ResultadoStock(pedidoId, tipo, estado, codigo, mensaje);

    // ---- stock.reservar ----

    private static ResultadoStock ReservarLote(CambioInventario cambio, SolicitudReserva solicitud, string pedidoId)
    {
        const string tipo = TiposComandoStock.Reservar;

        if (cambio.BuscarLote(pedidoId, out LoteReserva? existente))
        {
            // Incluye la reserva tardía tras una liberación anticipada y la repetida con otro contenido.
            return Resultado(tipo, pedidoId, existente.Estado, CodigosResultado.EstadoIncompatible,
                $"El pedido '{pedidoId}' ya tiene un lote {TextoEstadoReserva.ATexto(existente.Estado)} en esta sede.");
        }

        string? errorDatos = ValidarDatos(solicitud.Lineas);
        if (errorDatos != null)
        {
            return Rechazar(cambio, solicitud, pedidoId, Array.Empty<LineaReserva>(), CodigosResultado.DatosInvalidos, errorDatos);
        }

        LineaReserva[] lineas = ConvertirLineas(solicitud.Lineas!);
        string? falla = null;
        string codigo = CodigosResultado.Ok;
        for (int i = 0; i < lineas.Length && falla == null; i++)
        {
            if (!cambio.BuscarStock(lineas[i].ProductoId, out _))
            {
                codigo = CodigosResultado.ProductoDesconocido;
                falla = $"El producto '{lineas[i].ProductoId}' no existe en esta sede.";
            }
        }
        for (int i = 0; i < lineas.Length && falla == null; i++)
        {
            cambio.BuscarStock(lineas[i].ProductoId, out StockLocal? stock);
            if (stock!.Producto.PrecioUnitarioCentimos != lineas[i].PrecioUnitarioCentimos)
            {
                codigo = CodigosResultado.PrecioDistinto;
                falla = $"El precio de '{lineas[i].ProductoId}' ({lineas[i].PrecioUnitarioCentimos}) no coincide con el de catálogo ({stock.Producto.PrecioUnitarioCentimos}).";
            }
        }
        for (int i = 0; i < lineas.Length && falla == null; i++)
        {
            cambio.BuscarStock(lineas[i].ProductoId, out StockLocal? stock);
            if (lineas[i].Cantidad > stock!.Disponible)
            {
                codigo = CodigosResultado.StockInsuficiente;
                falla = $"Stock insuficiente de '{lineas[i].ProductoId}': se piden {lineas[i].Cantidad} y hay {stock.Disponible} disponibles.";
            }
        }
        if (falla != null)
        {
            return Rechazar(cambio, solicitud, pedidoId, lineas, codigo, falla);
        }

        // Todo el lote es válido: ahora sí se retiene, línea por línea, dentro del mismo cambio.
        for (int i = 0; i < lineas.Length; i++)
        {
            cambio.BuscarStock(lineas[i].ProductoId, out StockLocal? stock);
            cambio.ModificarStock(lineas[i].ProductoId, stock!.Existencia, stock.Reservada + lineas[i].Cantidad);
        }
        cambio.CrearLote(new LoteReserva(pedidoId, solicitud.OrigenSedeId, EstadoReserva.Reservada, lineas));
        return Resultado(tipo, pedidoId, EstadoReserva.Reservada, CodigosResultado.Ok, "Lote reservado.");
    }

    private static ResultadoStock Rechazar(
        CambioInventario cambio, SolicitudReserva solicitud, string pedidoId, LineaReserva[] lineas, string codigo, string mensaje)
    {
        cambio.CrearLote(new LoteReserva(pedidoId, solicitud.OrigenSedeId, EstadoReserva.Rechazada, lineas));
        return Resultado(TiposComandoStock.Reservar, pedidoId, EstadoReserva.Rechazada, codigo, mensaje);
    }

    /// Devuelve el motivo del primer problema de forma, o null si todas las líneas son representables.
    private static string? ValidarDatos(LineaSolicitada?[]? lineas)
    {
        if (lineas == null || lineas.Length == 0)
        {
            return "El lote no trae líneas.";
        }
        for (int i = 0; i < lineas.Length; i++)
        {
            LineaSolicitada? linea = lineas[i];
            if (linea == null)
            {
                return $"La línea {i} es nula.";
            }
            if (string.IsNullOrWhiteSpace(linea.ProductoId))
            {
                return $"La línea {i} no trae productoId.";
            }
            if (linea.Cantidad == null || linea.Cantidad <= 0)
            {
                return $"La cantidad de '{linea.ProductoId}' debe ser un entero positivo.";
            }
            if (linea.PrecioUnitarioCentimos == null || linea.PrecioUnitarioCentimos < 0)
            {
                return $"El precio de '{linea.ProductoId}' debe ser un entero no negativo en céntimos.";
            }
            for (int k = 0; k < i; k++)
            {
                if (string.Equals(lineas[k]!.ProductoId, linea.ProductoId, StringComparison.Ordinal))
                {
                    return $"El producto '{linea.ProductoId}' está repetido en el lote.";
                }
            }
        }
        return null;
    }

    private static LineaReserva[] ConvertirLineas(LineaSolicitada?[] lineas)
    {
        LineaReserva[] convertidas = new LineaReserva[lineas.Length];
        for (int i = 0; i < lineas.Length; i++)
        {
            LineaSolicitada linea = lineas[i]!;
            convertidas[i] = new LineaReserva(linea.ProductoId!, linea.Cantidad!.Value, linea.PrecioUnitarioCentimos!.Value);
        }
        return convertidas;
    }

    // ---- stock.confirmar ----

    private static ResultadoStock ConfirmarLote(CambioInventario cambio, string pedidoId)
    {
        const string tipo = TiposComandoStock.Confirmar;

        if (!cambio.BuscarLote(pedidoId, out LoteReserva? lote))
        {
            return Resultado(tipo, pedidoId, EstadoReserva.Inexistente, CodigosResultado.EstadoIncompatible,
                $"No hay reserva del pedido '{pedidoId}' en esta sede.");
        }

        switch (lote.Estado)
        {
            case EstadoReserva.Reservada:
                LineaReserva[] lineas = lote.Lineas();
                for (int i = 0; i < lineas.Length; i++)
                {
                    cambio.BuscarStock(lineas[i].ProductoId, out StockLocal? stock);
                    cambio.ModificarStock(lineas[i].ProductoId, stock!.Existencia - lineas[i].Cantidad, stock.Reservada - lineas[i].Cantidad);
                }
                cambio.ActualizarLote(lote.ConEstado(EstadoReserva.Confirmada));
                return Resultado(tipo, pedidoId, EstadoReserva.Confirmada, CodigosResultado.Ok, "Lote confirmado.");
            case EstadoReserva.Confirmada:
                return Resultado(tipo, pedidoId, EstadoReserva.Confirmada, CodigosResultado.Ok, "El lote ya estaba confirmado.");
            default:
                return Resultado(tipo, pedidoId, lote.Estado, CodigosResultado.EstadoIncompatible,
                    $"No se puede confirmar un lote {TextoEstadoReserva.ATexto(lote.Estado)}.");
        }
    }

    // ---- stock.liberar ----

    private static ResultadoStock LiberarLote(CambioInventario cambio, SolicitudDecision solicitud, string pedidoId)
    {
        const string tipo = TiposComandoStock.Liberar;

        if (!cambio.BuscarLote(pedidoId, out LoteReserva? lote))
        {
            // Liberación anticipada: queda guardada para que una reserva tardía no retenga unidades.
            cambio.CrearLote(new LoteReserva(pedidoId, solicitud.OrigenSedeId, EstadoReserva.Liberada, Array.Empty<LineaReserva>()));
            return Resultado(tipo, pedidoId, EstadoReserva.Liberada, CodigosResultado.Ok,
                "Liberación registrada antes de la reserva.");
        }

        switch (lote.Estado)
        {
            case EstadoReserva.Reservada:
                LineaReserva[] lineas = lote.Lineas();
                for (int i = 0; i < lineas.Length; i++)
                {
                    cambio.BuscarStock(lineas[i].ProductoId, out StockLocal? stock);
                    cambio.ModificarStock(lineas[i].ProductoId, stock!.Existencia, stock.Reservada - lineas[i].Cantidad);
                }
                cambio.ActualizarLote(lote.ConEstado(EstadoReserva.Liberada));
                return Resultado(tipo, pedidoId, EstadoReserva.Liberada, CodigosResultado.Ok, "Lote liberado.");
            case EstadoReserva.Rechazada:
                cambio.ActualizarLote(lote.ConEstado(EstadoReserva.Liberada));
                return Resultado(tipo, pedidoId, EstadoReserva.Liberada, CodigosResultado.Ok, "Lote rechazado cerrado como liberado.");
            case EstadoReserva.Liberada:
                return Resultado(tipo, pedidoId, EstadoReserva.Liberada, CodigosResultado.Ok, "El lote ya estaba liberado.");
            default:
                return Resultado(tipo, pedidoId, lote.Estado, CodigosResultado.EstadoIncompatible,
                    $"No se puede liberar un lote {TextoEstadoReserva.ATexto(lote.Estado)}.");
        }
    }
}
