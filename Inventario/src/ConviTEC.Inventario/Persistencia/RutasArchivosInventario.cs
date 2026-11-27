using System;
using System.IO;

namespace ConviTEC.Inventario.Persistencia;

/// Ubicación de los archivos de una sede. Cada sede usa su propio directorio (RNF14, RNF15).
public sealed class RutasArchivosInventario
{
    public const string NombreMaestro = "maestro-productos.json";
    public const string NombreExistenciasIniciales = "existencias-iniciales.json";
    public const string NombreEstado = "estado-inventario.json";

    public string SedeId { get; }
    public string MaestroProductos { get; }
    public string ExistenciasIniciales { get; }
    public string EstadoInventario { get; }

    public RutasArchivosInventario(string sedeId, string maestroProductos, string existenciasIniciales, string estadoInventario)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(maestroProductos, nameof(maestroProductos));
        ArgumentException.ThrowIfNullOrWhiteSpace(existenciasIniciales, nameof(existenciasIniciales));
        ArgumentException.ThrowIfNullOrWhiteSpace(estadoInventario, nameof(estadoInventario));

        SedeId = sedeId;
        MaestroProductos = maestroProductos;
        ExistenciasIniciales = existenciasIniciales;
        EstadoInventario = estadoInventario;
    }

    /// Rutas con los nombres estándar dentro del directorio propio de la sede.
    public static RutasArchivosInventario EnDirectorio(string directorioSede, string sedeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directorioSede, nameof(directorioSede));
        return new RutasArchivosInventario(
            sedeId,
            Path.Combine(directorioSede, NombreMaestro),
            Path.Combine(directorioSede, NombreExistenciasIniciales),
            Path.Combine(directorioSede, NombreEstado));
    }
}
