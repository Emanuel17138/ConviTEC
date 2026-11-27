using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Negocio;

/// Arma un <see cref="InventarioLocal"/> con stock dado (revisión 0), con su snapshot inicial ya guardado en el
/// directorio de la prueba, igual que lo dejaría el arranque inicial.
internal static class FabricaInventario
{
    public static StockLocal Stock(string id, int existencia, int reservada, long precio = 150000) =>
        new StockLocal(new Producto(id, "Nombre " + id, "Descripción", "Categoría", precio), existencia, reservada);

    public static InventarioLocal Crear(DirectorioTemporal dir, string sedeId, params StockLocal[] stocks) =>
        CrearCon(dir, sedeId, Array.Empty<string>(), null, stocks);

    /// <param name="repositorio">Repositorio con puntos de corte; si es null se usa el normal.</param>
    public static InventarioLocal CrearCon(
        DirectorioTemporal dir, string sedeId, string[] sedesRemotas, RepositorioInventario? repositorio, StockLocal[] stocks)
    {
        RutasArchivosInventario rutas = dir.Rutas(sedeId);
        EstadoInventario estado = new EstadoInventario();
        for (int i = 0; i < stocks.Length; i++)
        {
            estado.Stock.Insertar(stocks[i].Producto.ProductoId, new EntradaStock(stocks[i], 0));
        }

        // El snapshot inicial siempre con el repositorio normal: los cortes simulados son para los cambios.
        new RepositorioInventario(rutas).GuardarEstado(estado.ADto(sedeId));
        return new InventarioLocal(sedeId, estado, repositorio ?? new RepositorioInventario(rutas), sedesRemotas);
    }

    /// Recarga desde disco como lo haría un reinicio del proceso.
    public static InventarioLocal Recargar(DirectorioTemporal dir, string sedeId, params string[] sedesRemotas) =>
        new CargadorInventario().Cargar(dir.Rutas(sedeId), sedesRemotas).Inventario;
}
