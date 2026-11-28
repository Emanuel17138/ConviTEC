namespace ConviTEC.Inventario.Negocio;

/// Línea de un lote tal como llegó en el comando. Los campos admiten null para poder representar
/// datos inválidos y responder DATOS_INVALIDOS sin lanzar excepciones.
public sealed class LineaSolicitada
{
    public string? ProductoId { get; }
    public int? Cantidad { get; }
    public long? PrecioUnitarioCentimos { get; }

    public LineaSolicitada(string? productoId, int? cantidad, long? precioUnitarioCentimos)
    {
        ProductoId = productoId;
        Cantidad = cantidad;
        PrecioUnitarioCentimos = precioUnitarioCentimos;
    }
}
