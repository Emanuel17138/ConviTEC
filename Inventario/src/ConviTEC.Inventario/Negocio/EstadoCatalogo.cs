using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;

namespace ConviTEC.Inventario.Negocio;

/// Lo que se persiste de la réplica, cada parte en su AVL propio con clave ordinal (sensible a mayúsculas).
/// Solo <see cref="CatalogoReplicado"/> lo modifica, bajo su exclusión mutua.
internal sealed class EstadoCatalogo
{
    /// productoId + sedeId (ver <see cref="OfertaReplicada.ClaveDe"/>) → última oferta recibida.
    public ArbolAvl<string, OfertaReplicada> Ofertas { get; } = EstadoInventario.NuevoIndice<OfertaReplicada>();

    /// sedeId → sedeId, para las sedes cuya primera instantánea ya llegó.
    public ArbolAvl<string, string> Sincronizadas { get; } = EstadoInventario.NuevoIndice<string>();
}
