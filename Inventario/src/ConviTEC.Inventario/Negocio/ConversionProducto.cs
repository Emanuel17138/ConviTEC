using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Negocio;

/// Conversión validada de <see cref="ProductoDto"/> a <see cref="Producto"/>, común a todos los archivos de la sede.
internal static class ConversionProducto
{
    public static Producto Convertir(ProductoDto? dto, string archivo, string posicion)
    {
        if (dto == null)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion} es nulo.");
        }
        if (dto.PrecioUnitarioCentimos == null)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: falta 'precioUnitarioCentimos'.");
        }

        try
        {
            return new Producto(
                dto.ProductoId!, dto.Nombre!, dto.Descripcion!, dto.Categoria!, dto.PrecioUnitarioCentimos.Value);
        }
        catch (ArgumentException e)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: {e.Message}", e);
        }
    }
}
