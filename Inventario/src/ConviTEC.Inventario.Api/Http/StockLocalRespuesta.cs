using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Api.Http;

/// Producto tal como lo expone el contrato REST v1.
public sealed record ProductoRespuesta(
    string ProductoId, string Nombre, string Descripcion, string Categoria, long PrecioUnitarioCentimos)
{
    public static ProductoRespuesta Desde(Producto producto) =>
        new ProductoRespuesta(
            producto.ProductoId, producto.Nombre, producto.Descripcion, producto.Categoria,
            producto.PrecioUnitarioCentimos);
}

/// StockLocal tal como lo expone el contrato REST v1: {producto, existencia, reservada, disponible}.
/// Proyectar a un tipo propio fija los campos exactos y desacopla el JSON del modelo de dominio.
public sealed record StockLocalRespuesta(ProductoRespuesta Producto, int Existencia, int Reservada, int Disponible)
{
    public static StockLocalRespuesta Desde(StockLocal stock)
    {
        return new StockLocalRespuesta(
            ProductoRespuesta.Desde(stock.Producto),
            stock.Existencia,
            stock.Reservada,
            stock.Disponible);
    }

    /// Proyecta el arreglo completo conservando el orden recibido (productoId ascendente).
    public static StockLocalRespuesta[] Desde(StockLocal[] stocks)
    {
        StockLocalRespuesta[] respuesta = new StockLocalRespuesta[stocks.Length];
        for (int i = 0; i < stocks.Length; i++)
        {
            respuesta[i] = Desde(stocks[i]);
        }
        return respuesta;
    }
}
