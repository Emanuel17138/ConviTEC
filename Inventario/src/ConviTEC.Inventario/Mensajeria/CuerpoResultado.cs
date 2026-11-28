using System;
using System.IO;
using System.Text;
using System.Text.Json;
using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Mensajeria;

/// Cuerpo de <c>stock.resultado</c>: <c>{pedidoId,operacion,estadoReserva,codigo,mensaje}</c>.
/// Lo escribe la proveedora y lo lee el cliente de Pedidos, así el formato vive en un solo lugar.
internal static class CuerpoResultado
{
    public static string Escribir(ResultadoStock resultado)
    {
        using MemoryStream salida = new MemoryStream();
        using (Utf8JsonWriter escritor = new Utf8JsonWriter(salida))
        {
            escritor.WriteStartObject();
            escritor.WriteString("pedidoId", resultado.PedidoId);
            escritor.WriteString("operacion", resultado.Operacion);
            escritor.WriteString("estadoReserva", TextoEstadoReserva.ATexto(resultado.EstadoReserva));
            escritor.WriteString("codigo", resultado.Codigo);
            escritor.WriteString("mensaje", resultado.Mensaje);
            escritor.WriteEndObject();
        }
        return Encoding.UTF8.GetString(salida.ToArray());
    }

    public static bool TryLeer(JsonElement cuerpo, out ResultadoStock? resultado, out string motivo)
    {
        resultado = null;
        if (cuerpo.ValueKind != JsonValueKind.Object)
        {
            motivo = "El cuerpo de stock.resultado debe ser un objeto.";
            return false;
        }

        if (!TryTexto(cuerpo, "pedidoId", permitirVacio: true, out string? pedidoId, out motivo)
            || !TryTexto(cuerpo, "operacion", permitirVacio: false, out string? operacion, out motivo)
            || !TryTexto(cuerpo, "estadoReserva", permitirVacio: false, out string? estadoTexto, out motivo)
            || !TryTexto(cuerpo, "codigo", permitirVacio: false, out string? codigo, out motivo)
            || !TryTexto(cuerpo, "mensaje", permitirVacio: true, out string? mensaje, out motivo))
        {
            return false;
        }

        if (!EsOperacionDeStock(operacion!))
        {
            motivo = $"Operación '{operacion}' desconocida.";
            return false;
        }
        if (!TextoEstadoReserva.TryParse(estadoTexto, out EstadoReserva? estado))
        {
            motivo = $"Estado de reserva '{estadoTexto}' desconocido.";
            return false;
        }
        if (!EsCodigoConocido(codigo!))
        {
            motivo = $"Código '{codigo}' desconocido.";
            return false;
        }

        resultado = new ResultadoStock(pedidoId!, operacion!, estado.Value, codigo!, mensaje!);
        motivo = string.Empty;
        return true;
    }

    private static bool TryTexto(JsonElement cuerpo, string campo, bool permitirVacio, out string? valor, out string motivo)
    {
        valor = null;
        if (!cuerpo.TryGetProperty(campo, out JsonElement elemento) || elemento.ValueKind != JsonValueKind.String)
        {
            motivo = $"Falta '{campo}' o no es texto.";
            return false;
        }

        string texto = elemento.GetString() ?? string.Empty;
        if (!permitirVacio && string.IsNullOrWhiteSpace(texto))
        {
            motivo = $"'{campo}' está vacío.";
            return false;
        }

        valor = texto;
        motivo = string.Empty;
        return true;
    }

    private static bool EsOperacionDeStock(string operacion) =>
        string.Equals(operacion, TiposComandoStock.Reservar, StringComparison.Ordinal)
        || string.Equals(operacion, TiposComandoStock.Confirmar, StringComparison.Ordinal)
        || string.Equals(operacion, TiposComandoStock.Liberar, StringComparison.Ordinal);

    private static bool EsCodigoConocido(string codigo) =>
        string.Equals(codigo, CodigosResultado.Ok, StringComparison.Ordinal)
        || string.Equals(codigo, CodigosResultado.StockInsuficiente, StringComparison.Ordinal)
        || string.Equals(codigo, CodigosResultado.ProductoDesconocido, StringComparison.Ordinal)
        || string.Equals(codigo, CodigosResultado.PrecioDistinto, StringComparison.Ordinal)
        || string.Equals(codigo, CodigosResultado.DatosInvalidos, StringComparison.Ordinal)
        || string.Equals(codigo, CodigosResultado.ClaveReutilizada, StringComparison.Ordinal)
        || string.Equals(codigo, CodigosResultado.EstadoIncompatible, StringComparison.Ordinal);
}
