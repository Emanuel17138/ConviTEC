using System;
using ConviTEC.Inventario.Api;
using ConviTEC.Inventario.Mensajeria;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;

ConfiguracionSede configuracion;
try
{
    configuracion = ConfiguracionSede.Leer(args);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

ResultadoArranque arranque;
CatalogoReplicado catalogo;
try
{
    // Valida todo antes de instalar el estado: si algo falla la sede no abre el puerto.
    RutasArchivosInventario rutas = RutasArchivosInventario.EnDirectorio(configuracion.DirectorioDatos, configuracion.SedeId);
    arranque = new CargadorInventario().Cargar(rutas, configuracion.SedesRemotas);
    catalogo = new CargadorCatalogo().Cargar(rutas, configuracion.SedesRemotas);
}
catch (DatosInventarioInvalidosException e)
{
    Console.Error.WriteLine("No se pudo arrancar la sede " + configuracion.SedeId + ": " + e.Message);
    return 1;
}

Console.WriteLine(
    $"Sede {configuracion.SedeId}: arranque {arranque.Modo}, {arranque.Inventario.Cantidad} producto(s).");
Console.WriteLine(
    $"Catálogo replicado: {catalogo.ConsultarOfertas().Length} oferta(s), "
    + $"{catalogo.SedesPendientes().Length} sede(s) sin primera instantánea.");

// Lo que llega por RabbitMQ a inventario.<sede>: comandos de stock (RF02) y mensajes de catálogo (RF03).
DespachadorInventario despachador = new DespachadorInventario(configuracion.SedeId);
despachador.Registrar(new ManejadorComandosStock(configuracion.SedeId, new ProveedorStock(arranque.Inventario)));
despachador.Registrar(new ManejadorMensajesCatalogo(configuracion.SedeId, arranque.Inventario, catalogo));
OpcionesMensajeria mensajeria = new OpcionesMensajeria(
    configuracion.SedeId, configuracion.SedesRemotas, configuracion.RabbitUri, despachador,
    publicador => new SincronizadorCatalogo(
        configuracion.SedeId, configuracion.SedesRemotas, arranque.Inventario, publicador, Console.WriteLine));

ServidorSede.Crear(arranque.Inventario, configuracion.Url, mensajeria, catalogo).Run();
return 0;
