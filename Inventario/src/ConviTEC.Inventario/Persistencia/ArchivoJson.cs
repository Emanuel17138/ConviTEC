using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ConviTEC.Inventario.Persistencia;

/// Lectura y escritura atómica de archivos JSON (UTF-8, camelCase) compartida por los repositorios de la sede.
/// No valida reglas de negocio: solo entrega DTO o falla con <see cref="DatosInventarioInvalidosException"/>.
internal static class ArchivoJson
{
    private static readonly JsonSerializerOptions Opciones = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T Leer<T>(string ruta) where T : class
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

    /// Guarda de forma atómica: escribe un archivo temporal, lo fuerza a disco y lo renombra sobre el definitivo.
    /// Un corte antes del reemplazo deja el archivo anterior completo; un corte después deja el nuevo completo.
    /// <paramref name="alCompletarEtapa"/> solo lo usan las pruebas: si lanza, simula un corte del proceso en ese
    /// punto (el temporal no se limpia).
    public static void Guardar<T>(string destino, T contenidoDto, Action<EtapaGuardado>? alCompletarEtapa) where T : class
    {
        byte[] contenido = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(contenidoDto, Opciones));
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
            alCompletarEtapa?.Invoke(EtapaGuardado.TemporalEscrito);
            File.Move(temporal, destino, overwrite: true);
            alCompletarEtapa?.Invoke(EtapaGuardado.Reemplazado);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            BorrarTemporal(temporal);
            throw new DatosInventarioInvalidosException(destino, "no se pudo guardar el estado: " + e.Message, e);
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
