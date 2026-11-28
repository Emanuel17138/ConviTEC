using System;
using ConviTEC.Inventario.Api.Http;
using ConviTEC.Inventario.Negocio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConviTEC.Inventario.Api;

/// Arma el backend HTTP de una sede a partir de su inventario ya cargado y validado.
public static class ServidorSede
{
    /// <param name="mensajeria">Si se indica, la sede también consume su cola de inventario en RabbitMQ.</param>
    /// <param name="catalogo">Si se indica, la sede expone <c>GET /api/v1/catalogo</c> y registra el puerto
    /// <see cref="IConsultaCatalogo"/> para Pedidos.</param>
    public static WebApplication Crear(
        InventarioLocal inventario, string url, OpcionesMensajeria? mensajeria = null, CatalogoReplicado? catalogo = null)
    {
        ArgumentNullException.ThrowIfNull(inventario, nameof(inventario));
        ArgumentException.ThrowIfNullOrWhiteSpace(url, nameof(url));

        // Falla antes de abrir nada si el inventario y la réplica no son de la misma sede.
        ConsultaCatalogo? consulta = catalogo == null ? null : new ConsultaCatalogo(inventario, catalogo);

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(url);
        if (consulta != null)
        {
            builder.Services.AddSingleton<IConsultaCatalogo>(consulta);
        }
        if (mensajeria != null)
        {
            builder.Services.AddHostedService(proveedor =>
                new ServicioMensajeria(mensajeria, proveedor.GetRequiredService<ILogger<ServicioMensajeria>>()));
        }
        WebApplication app = builder.Build();

        // Un fallo inesperado nunca filtra detalles internos: se responde con el cuerpo común {codigo, mensaje}.
        app.UseExceptionHandler(manejador => manejador.Run(async contexto =>
        {
            Exception? error = contexto.Features.Get<IExceptionHandlerFeature>()?.Error;
            app.Logger.LogError(error, "Error no controlado en {Metodo} {Ruta}", contexto.Request.Method, contexto.Request.Path);
            contexto.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await contexto.Response.WriteAsJsonAsync(
                new ErrorRespuesta("ERROR_INTERNO", "Error interno al procesar la solicitud."));
        }));

        // Registro de la operación en el arranque de la sede: el endpoint existe solo con el estado ya instalado.
        app.MapearInventario(inventario);
        if (consulta != null)
        {
            app.MapearCatalogo(consulta);
        }
        return app;
    }
}
