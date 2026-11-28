using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ConviTEC.Inventario.Api.Http;

/// Adaptador REST del inventario local. No contiene reglas de negocio: llama a <see cref="InventarioLocal"/>
/// y proyecta el resultado: consulta del inventario y reposiciones sobre la misma instancia.
public static class RutasInventario
{
    public const string PrefijoV1 = "/api/v1";
    public const string RutaInventario = PrefijoV1 + "/inventario";
    public const string RutaReposiciones = RutaInventario + "/{productoId}/reposiciones";

    public static IEndpointRouteBuilder MapearInventario(this IEndpointRouteBuilder rutas, InventarioLocal inventario)
    {
        ArgumentNullException.ThrowIfNull(rutas, nameof(rutas));
        ArgumentNullException.ThrowIfNull(inventario, nameof(inventario));

        // 200 con el stock de la sede por productoId ascendente, agotados incluidos. Sin productos: 200 y [].
        rutas.MapGet(RutaInventario, () => Results.Ok(StockLocalRespuesta.Desde(inventario.ConsultarInventario())));

        // 200 StockLocal resultante. 400 DATOS_INVALIDOS / CANTIDAD_INVALIDA, 404 PRODUCTO_DESCONOCIDO, 409 CLAVE_REUTILIZADA.
        ServicioReposiciones reposiciones = new ServicioReposiciones(inventario);
        rutas.MapPost(RutaReposiciones, async (string productoId, HttpRequest peticion) =>
        {
            ReposicionSolicitud? solicitud = await ReposicionSolicitud.LeerAsync(peticion.Body, peticion.HttpContext.RequestAborted);
            if (solicitud == null)
            {
                return Error(StatusCodes.Status400BadRequest, CodigosReposicion.DatosInvalidos,
                    "El cuerpo debe ser un objeto JSON {operacionId,cantidad}.");
            }

            ResultadoReposicion resultado = reposiciones.Reponer(productoId, solicitud.OperacionId, solicitud.Cantidad);
            return resultado.Estado switch
            {
                EstadoReposicion.Aplicada or EstadoReposicion.Repetida => Results.Ok(StockLocalRespuesta.Desde(resultado.Stock!)),
                EstadoReposicion.ProductoDesconocido => Error(StatusCodes.Status404NotFound, resultado.Codigo!, resultado.Mensaje),
                EstadoReposicion.ClaveReutilizada => Error(StatusCodes.Status409Conflict, resultado.Codigo!, resultado.Mensaje),
                _ => Error(StatusCodes.Status400BadRequest, resultado.Codigo!, resultado.Mensaje),
            };
        });
        return rutas;
    }

    private static IResult Error(int estado, string codigo, string mensaje) =>
        Results.Json(new ErrorRespuesta(codigo, mensaje), statusCode: estado);
}
