using System;
using System.IO;
using System.Text;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Tests.Persistencia;

/// Directorio de sede aislado por prueba. Se borra al terminar.
public sealed class DirectorioTemporal : IDisposable
{
    public string Ruta { get; }

    public DirectorioTemporal()
    {
        Ruta = Path.Combine(Path.GetTempPath(), "convitec-inventario-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Ruta);
    }

    public string Escribir(string nombreArchivo, string contenido)
    {
        string ruta = Path.Combine(Ruta, nombreArchivo);
        File.WriteAllText(ruta, contenido, new UTF8Encoding(false));
        return ruta;
    }

    public string Leer(string nombreArchivo) => File.ReadAllText(Path.Combine(Ruta, nombreArchivo), Encoding.UTF8);

    public bool Existe(string nombreArchivo) => File.Exists(Path.Combine(Ruta, nombreArchivo));

    public RutasArchivosInventario Rutas(string sedeId) => RutasArchivosInventario.EnDirectorio(Ruta, sedeId);

    public void Dispose()
    {
        if (Directory.Exists(Ruta))
        {
            Directory.Delete(Ruta, recursive: true);
        }
    }
}
