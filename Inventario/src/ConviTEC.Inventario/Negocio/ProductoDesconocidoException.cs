using System;

namespace ConviTEC.Inventario.Negocio;

/// Se intentó modificar un producto que esta sede no maneja.
public sealed class ProductoDesconocidoException : Exception
{
    public string ProductoId { get; }

    public ProductoDesconocidoException(string productoId)
        : base($"El producto '{productoId}' no existe en esta sede.")
    {
        ProductoId = productoId;
    }
}
