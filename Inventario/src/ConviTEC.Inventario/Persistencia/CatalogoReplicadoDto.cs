namespace ConviTEC.Inventario.Persistencia;

// DTO de catalogo-replicado.json. Todos los campos admiten null para detectar campos faltantes. Solo arreglos nativos (RNF06).

public sealed class OfertaReplicadaGuardadaDto
{
    public string? SedeId { get; set; }
    public ProductoDto? Producto { get; set; }
    public int? Disponible { get; set; }
    public long? Revision { get; set; }
}

/// catalogo-replicado.json: última oferta recibida por pareja sede–producto y sedes cuya primera instantánea ya llegó.
public sealed class CatalogoReplicadoDto
{
    public const int VersionActual = 1;

    public int? Version { get; set; }
    public string? SedeId { get; set; }
    public string?[]? SedesSincronizadas { get; set; }
    public OfertaReplicadaGuardadaDto?[]? Ofertas { get; set; }
}
