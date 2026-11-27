namespace ConviTEC.Inventario.Persistencia;

// DTO de los archivos JSON. Todos los campos admiten null para poder detectar campos faltantes
// y rechazarlos con un mensaje claro. Solo usan arreglos nativos (RNF06).

public sealed class ProductoDto
{
    public string? ProductoId { get; set; }
    public string? Nombre { get; set; }
    public string? Descripcion { get; set; }
    public string? Categoria { get; set; }
    public long? PrecioUnitarioCentimos { get; set; }
}

/// maestro-productos.json: catálogo común. Solo se lee en el arranque inicial.
public sealed class MaestroProductosDto
{
    public ProductoDto?[]? Productos { get; set; }
}

public sealed class ExistenciaInicialDto
{
    public string? ProductoId { get; set; }
    public int? Existencia { get; set; }
}

/// existencias-iniciales.json: productos que maneja esta sede. Solo se lee en el arranque inicial.
public sealed class ExistenciasInicialesDto
{
    public string? SedeId { get; set; }
    public ExistenciaInicialDto?[]? Existencias { get; set; }
}

public sealed class StockGuardadoDto
{
    public ProductoDto? Producto { get; set; }
    public int? Existencia { get; set; }
    public int? Reservada { get; set; }
}

/// estado-inventario.json: snapshot del estado de la sede. Su presencia marca la recuperación.
public sealed class EstadoInventarioDto
{
    public const int VersionActual = 1;

    public int? Version { get; set; }
    public string? SedeId { get; set; }
    public StockGuardadoDto?[]? Stock { get; set; }
}
