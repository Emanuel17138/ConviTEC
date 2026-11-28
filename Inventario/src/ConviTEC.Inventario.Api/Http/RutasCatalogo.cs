using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ConviTEC.Inventario.Api.Http;

/// Adaptador REST del catálogo global. Sin reglas de negocio: proyecta la vista de <see cref="IConsultaCatalogo"/>,
/// la misma que usa Pedidos por el puerto local.
public static class RutasCatalogo
{
    public const string RutaCatalogo = RutasInventario.PrefijoV1 + "/catalogo";

    public static IEndpointRouteBuilder MapearCatalogo(this IEndpointRouteBuilder rutas, IConsultaCatalogo consulta)
    {
        ArgumentNullException.ThrowIfNull(rutas, nameof(rutas));
        ArgumentNullException.ThrowIfNull(consulta, nameof(consulta));

        // 200 ProductoGlobal[] por productoId y luego sedeId ([] si la vista inicializada no tiene productos).
        // 503 CATALOGO_INICIALIZANDO mientras falte la primera instantánea de alguna sede.
        rutas.MapGet(RutaCatalogo, () =>
        {
            try
            {
                return Results.Ok(ProductoGlobalRespuesta.Desde(consulta.ConsultarCatalogo()));
            }
            catch (CatalogoInicializandoException e)
            {
                return Results.Json(new ErrorRespuesta(CodigosCatalogo.CatalogoInicializando, e.Message),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        return rutas;
    }
}
