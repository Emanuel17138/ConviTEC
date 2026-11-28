using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Api.Http;

/// Oferta tal como la expone el contrato REST v1: {sedeId, disponible, revision}.
public sealed record OfertaRespuesta(string SedeId, int Disponible, long Revision)
{
    public static OfertaRespuesta Desde(Oferta oferta) => new OfertaRespuesta(oferta.SedeId, oferta.Disponible, oferta.Revision);
}

/// ProductoGlobal tal como lo expone el contrato REST v1: {producto, ofertas}.
public sealed record ProductoGlobalRespuesta(ProductoRespuesta Producto, OfertaRespuesta[] Ofertas)
{
    public static ProductoGlobalRespuesta Desde(ProductoGlobal global)
    {
        Oferta[] ofertas = global.Ofertas;
        OfertaRespuesta[] respuesta = new OfertaRespuesta[ofertas.Length];
        for (int i = 0; i < ofertas.Length; i++)
        {
            respuesta[i] = OfertaRespuesta.Desde(ofertas[i]);
        }
        return new ProductoGlobalRespuesta(ProductoRespuesta.Desde(global.Producto), respuesta);
    }

    /// Proyecta la vista completa conservando el orden recibido (productoId y luego sedeId ascendentes).
    public static ProductoGlobalRespuesta[] Desde(ProductoGlobal[] vista)
    {
        ProductoGlobalRespuesta[] respuesta = new ProductoGlobalRespuesta[vista.Length];
        for (int i = 0; i < vista.Length; i++)
        {
            respuesta[i] = Desde(vista[i]);
        }
        return respuesta;
    }
}
