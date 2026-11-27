using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ConviTEC.Inventario.Persistencia;

/// Lee y escribe los archivos JSON (UTF-8, camelCase) de una sede. No valida reglas de negocio:
/// solo entrega DTO o falla con <see cref="DatosInventarioInvalidosException"/>.
public sealed class RepositorioInventario
{
    private static readonly JsonSerializerOptions Opciones = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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

    public MaestroProductosDto LeerMaestro() => Leer<MaestroProductosDto>(_rutas.MaestroProductos);

    public ExistenciasInicialesDto LeerExistenciasIniciales() => Leer<ExistenciasInicialesDto>(_rutas.ExistenciasIniciales);

    public EstadoInventarioDto LeerEstado() => Leer<EstadoInventarioDto>(_rutas.EstadoInventario);

    /// Guarda el snapshot completo de forma atómica: escribe un archivo temporal, lo fuerza a disco
    /// y lo renombra sobre el definitivo. Un corte antes del reemplazo deja el snapshot anterior completo;
    /// un corte después deja el nuevo completo. Nunca queda un archivo a medias.
    public void GuardarEstado(EstadoInventarioDto estado)
    {
        ArgumentNullException.ThrowIfNull(estado, nameof(estado));

        byte[] contenido = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(estado, Opciones));
        string destino = _rutas.EstadoInventario;
        string temporal = destino + ".tmp";

        try
        {
            string? directorio = Path.GetDirectoryName(destino);
            if (!string.IsNullOrEmpty(directorio))
            {
                Directory.CreateDirectory(directorio);
            }

            using (FileStream archivo = new FileStream(temporal, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                archivo.Write(contenido, 0, contenido.Length);
                archivo.Flush(true);
            }
            _alCompletarEtapa?.Invoke(EtapaGuardado.TemporalEscrito);
            File.Move(temporal, destino, overwrite: true);
            _alCompletarEtapa?.Invoke(EtapaGuardado.Reemplazado);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            BorrarTemporal(temporal);
            throw new DatosInventarioInvalidosException(destino, "no se pudo guardar el estado: " + e.Message, e);
        }
    }

    private static T Leer<T>(string ruta) where T : class
    {
        string texto;
        try
        {
            texto = File.ReadAllText(ruta, Encoding.UTF8);
        }
        catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
        {
            throw new DatosInventarioInvalidosException(ruta, "el archivo no existe.", e);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            throw new DatosInventarioInvalidosException(ruta, "no se pudo leer el archivo: " + e.Message, e);
        }

        try
        {
            T? dto = JsonSerializer.Deserialize<T>(texto, Opciones);
            return dto ?? throw new DatosInventarioInvalidosException(ruta, "el archivo no contiene un objeto JSON.");
        }
        catch (JsonException e)
        {
            throw new DatosInventarioInvalidosException(ruta, "JSON inválido: " + e.Message, e);
        }
    }

    private static void BorrarTemporal(string temporal)
    {
        try
        {
            File.Delete(temporal);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            // Mejor esfuerzo: el temporal sobrante se sobrescribe en el próximo guardado.
        }
    }
}
