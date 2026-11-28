using System;
using System.IO;

namespace ConviTEC.Inventario.Persistencia;

/// Lee y escribe los archivos JSON (UTF-8, camelCase) de una sede. No valida reglas de negocio:
/// solo entrega DTO o falla con <see cref="DatosInventarioInvalidosException"/>.
public sealed class RepositorioInventario
{
    private readonly RutasArchivosInventario _rutas;
    private readonly Action<EtapaGuardado>? _alCompletarEtapa;

    public RepositorioInventario(RutasArchivosInventario rutas) : this(rutas, null)
    {
    }

    /// Solo para pruebas: <paramref name="alCompletarEtapa"/> se invoca al terminar cada etapa del guardado y,
    /// si lanza, simula un corte del proceso en ese punto (el archivo temporal no se limpia).
    internal RepositorioInventario(RutasArchivosInventario rutas, Action<EtapaGuardado>? alCompletarEtapa)
    {
        ArgumentNullException.ThrowIfNull(rutas, nameof(rutas));
        _rutas = rutas;
        _alCompletarEtapa = alCompletarEtapa;
    }

    public bool ExisteEstadoGuardado() => File.Exists(_rutas.EstadoInventario);

    public MaestroProductosDto LeerMaestro() => ArchivoJson.Leer<MaestroProductosDto>(_rutas.MaestroProductos);

    public ExistenciasInicialesDto LeerExistenciasIniciales() => ArchivoJson.Leer<ExistenciasInicialesDto>(_rutas.ExistenciasIniciales);

    public EstadoInventarioDto LeerEstado() => ArchivoJson.Leer<EstadoInventarioDto>(_rutas.EstadoInventario);

    /// Guarda el snapshot completo de forma atómica (ver <see cref="ArchivoJson.Guardar{T}"/>).
    public void GuardarEstado(EstadoInventarioDto estado)
    {
        ArgumentNullException.ThrowIfNull(estado, nameof(estado));
        ArchivoJson.Guardar(_rutas.EstadoInventario, estado, _alCompletarEtapa);
    }
}
