using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;

namespace ConviTEC.Inventario.Tests.Negocio;

/// Arma una <see cref="CatalogoReplicado"/> sobre un directorio de prueba, igual que lo haría el arranque.
internal static class FabricaCatalogo
{
    public static EntradaCatalogo Entrada(string productoId, int disponible, long revision, long precio = 150000) =>
        new EntradaCatalogo(new Producto(productoId, "Nombre " + productoId, "Descripción", "Categoría", precio), disponible, revision);

    /// <param name="repositorio">Repositorio con puntos de corte; si es null se usa el normal.</param>
    public static CatalogoReplicado Crear(
        DirectorioTemporal dir, string sedeId, string[] sedesRemotas, Func<RutasArchivosInventario, RepositorioCatalogo>? repositorio = null)
    {
        RutasArchivosInventario rutas = dir.Rutas(sedeId);
        return new CargadorCatalogo().Cargar(rutas, sedesRemotas, repositorio?.Invoke(rutas) ?? new RepositorioCatalogo(rutas));
    }

    /// Recarga desde disco como lo haría un reinicio del proceso.
    public static CatalogoReplicado Recargar(DirectorioTemporal dir, string sedeId, params string[] sedesRemotas) =>
        new CargadorCatalogo().Cargar(dir.Rutas(sedeId), sedesRemotas);
}
