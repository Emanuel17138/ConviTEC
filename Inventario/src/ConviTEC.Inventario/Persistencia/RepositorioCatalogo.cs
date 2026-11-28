using System;
using System.IO;

namespace ConviTEC.Inventario.Persistencia;

/// Lee y escribe <c>catalogo-replicado.json</c> de la sede, con la misma escritura atómica que el inventario.
/// No valida reglas de negocio.
public sealed class RepositorioCatalogo
{
    private readonly RutasArchivosInventario _rutas;
    private readonly Action<EtapaGuardado>? _alCompletarEtapa;

    public RepositorioCatalogo(RutasArchivosInventario rutas) : this(rutas, null)
    {
    }

    /// Solo para pruebas: ver <see cref="RepositorioInventario"/>.
    internal RepositorioCatalogo(RutasArchivosInventario rutas, Action<EtapaGuardado>? alCompletarEtapa)
    {
        ArgumentNullException.ThrowIfNull(rutas, nameof(rutas));
        _rutas = rutas;
        _alCompletarEtapa = alCompletarEtapa;
    }

    public bool ExisteCatalogoGuardado() => File.Exists(_rutas.CatalogoReplicado);

    public CatalogoReplicadoDto LeerCatalogo() => ArchivoJson.Leer<CatalogoReplicadoDto>(_rutas.CatalogoReplicado);

    public void GuardarCatalogo(CatalogoReplicadoDto catalogo)
    {
        ArgumentNullException.ThrowIfNull(catalogo, nameof(catalogo));
        ArchivoJson.Guardar(_rutas.CatalogoReplicado, catalogo, _alCompletarEtapa);
    }
}
