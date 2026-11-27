using System;
using ConviTEC.Inventario.Negocio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ConviTEC.Inventario.Api.Http;

/// Adaptador REST del inventario local. No contiene reglas de negocio: llama a <see cref="InventarioLocal"/>
/// y proyecta el resultado. RF02 agregará aquí las reposiciones sobre la misma instancia.
public static class RutasInventario
{
    public const string PrefijoV1 = "/api/v1";
    public const string RutaInventario = PrefijoV1 + "/inventario";

    public static IEndpointRouteBuilder MapearInventario(this IEndpointRouteBuilder rutas, InventarioLocal inventario)
    {
        ArgumentNullException.ThrowIfNull(rutas, nameof(rutas));
        ArgumentNullException.ThrowIfNull(inventario, nameof(inventario));

        // 200 con el stock de la sede por productoId ascendente, agotados incluidos. Sin productos: 200 y [].
        rutas.MapGet(RutaInventario, () => Results.Ok(StockLocalRespuesta.Desde(inventario.ConsultarInventario())));
        return rutas;
    }
}
