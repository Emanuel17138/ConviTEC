using System;

namespace ConviTEC.Inventario.Persistencia;

/// Un archivo de inventario no se pudo leer, escribir o contiene datos inválidos.
/// El arranque se aborta sin instalar un estado parcial (RNF11, RNF18).
public sealed class DatosInventarioInvalidosException : Exception
{
    public string Archivo { get; }
    public string Detalle { get; }

    public DatosInventarioInvalidosException(string archivo, string detalle, Exception? causa = null)
        : base($"{archivo}: {detalle}", causa)
    {
        Archivo = archivo;
        Detalle = detalle;
    }
}
