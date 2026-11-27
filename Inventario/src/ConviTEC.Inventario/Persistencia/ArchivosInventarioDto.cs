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

    /// Opcional: un snapshot sin revisión (anterior a RF02) se lee como revisión 0.
    public long? Revision { get; set; }
}

public sealed class LineaReservaDto
{
    public string? ProductoId { get; set; }
    public int? Cantidad { get; set; }
    public long? PrecioUnitarioCentimos { get; set; }
}

public sealed class LoteGuardadoDto
{
    public string? PedidoId { get; set; }
    public string? CoordinadoraSedeId { get; set; }
    public string? Estado { get; set; }
    public LineaReservaDto?[]? Lineas { get; set; }
}

public sealed class ResultadoStockDto
{
    public string? PedidoId { get; set; }
    public string? Operacion { get; set; }
    public string? EstadoReserva { get; set; }
    public string? Codigo { get; set; }
    public string? Mensaje { get; set; }
}

public sealed class ComandoGuardadoDto
{
    public string? MensajeId { get; set; }
    public string? Tipo { get; set; }
    public string? OrigenSedeId { get; set; }
    public string? Contenido { get; set; }
    public ResultadoStockDto? Resultado { get; set; }
}

public sealed class ReposicionGuardadaDto
{
    public string? OperacionId { get; set; }
    public string? ProductoId { get; set; }
    public int? Cantidad { get; set; }
    public int? Existencia { get; set; }
    public int? Reservada { get; set; }
}

public sealed class SalidaPendienteGuardadaDto
{
    public string? MensajeId { get; set; }
    public string? DestinoSedeId { get; set; }
    public string? ProductoId { get; set; }
    public int? Disponible { get; set; }
    public long? Revision { get; set; }
}

/// estado-inventario.json: snapshot del estado de la sede. Su presencia marca la recuperación.
/// Las secciones lotes, comandos, reposiciones y salidasPendientes son opcionales (v1 solo agrega campos opcionales):
/// si faltan se leen como vacías.
public sealed class EstadoInventarioDto
{
    public const int VersionActual = 1;

    public int? Version { get; set; }
    public string? SedeId { get; set; }
    public StockGuardadoDto?[]? Stock { get; set; }
    public LoteGuardadoDto?[]? Lotes { get; set; }
    public ComandoGuardadoDto?[]? Comandos { get; set; }
    public ReposicionGuardadaDto?[]? Reposiciones { get; set; }
    public SalidaPendienteGuardadaDto?[]? SalidasPendientes { get; set; }
}
