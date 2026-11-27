using System;
using ConviTEC.Inventario.Api;
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
try
{
    // Valida todo antes de instalar el estado: si algo falla la sede no abre el puerto.
    arranque = new CargadorInventario().Cargar(
        RutasArchivosInventario.EnDirectorio(configuracion.DirectorioDatos, configuracion.SedeId),
        configuracion.SedesRemotas);
}
catch (DatosInventarioInvalidosException e)
{
    Console.Error.WriteLine("No se pudo arrancar la sede " + configuracion.SedeId + ": " + e.Message);
    return 1;
}

Console.WriteLine(
    $"Sede {configuracion.SedeId}: arranque {arranque.Modo}, {arranque.Inventario.Cantidad} producto(s).");

ServidorSede.Crear(arranque.Inventario, configuracion.Url).Run();
return 0;
