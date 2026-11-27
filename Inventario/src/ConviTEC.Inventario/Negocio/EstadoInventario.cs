using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Negocio;

/// Todo lo que se persiste de una sede, cada parte en su AVL propio con clave ordinal (sensible a mayúsculas).
/// Solo <see cref="InventarioLocal"/> lo modifica, bajo su exclusión mutua.
internal sealed class EstadoInventario
{
    /// productoId → stock y revisión.
    public ArbolAvl<string, EntradaStock> Stock { get; } = NuevoIndice<EntradaStock>();

    /// pedidoId → lote de reserva.
    public ArbolAvl<string, LoteReserva> Lotes { get; } = NuevoIndice<LoteReserva>();

    /// mensajeId → comando procesado y su resultado.
    public ArbolAvl<string, ComandoRegistrado> Comandos { get; } = NuevoIndice<ComandoRegistrado>();

    /// operacionId → reposición aplicada.
    public ArbolAvl<string, ReposicionRegistrada> Reposiciones { get; } = NuevoIndice<ReposicionRegistrada>();

    /// destino + producto → última publicación pendiente (ver <see cref="SalidaPendiente.Clave"/>).
    public ArbolAvl<string, SalidaPendiente> Salidas { get; } = NuevoIndice<SalidaPendiente>();

    /// Índice vacío con comparación ordinal: sensible a mayúsculas.
    public static ArbolAvl<string, T> NuevoIndice<T>() => new ArbolAvl<string, T>(StringComparer.Ordinal);

    /// Snapshot completo del estado actual, listo para guardar.
    public EstadoInventarioDto ADto(string sedeId) => ConversionEstado.Construir(
        sedeId,
        Stock.ValoresEnOrden(),
        Lotes.ValoresEnOrden(),
        Comandos.ValoresEnOrden(),
        Reposiciones.ValoresEnOrden(),
        Salidas.ValoresEnOrden());
}
