using System;

namespace ConviTEC.Inventario.Mensajeria;

/// No se pudo entregar un mensaje: broker sin conexión, sin confirmación o sin cola destinataria.
public sealed class EnvioFallidoException : Exception
{
    public EnvioFallidoException(string mensaje) : base(mensaje)
    {
    }

    public EnvioFallidoException(string mensaje, Exception causa) : base(mensaje, causa)
    {
    }
}
