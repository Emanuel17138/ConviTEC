using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Negocio;

/// Lectura de campos de los DTO: un campo faltante se reporta con archivo y posición.
internal static class CamposObligatorios
{
    public static T Valor<T>(T? valor, string campo, string archivo, string posicion) where T : struct
    {
        if (valor == null)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: falta '{campo}'.");
        }
        return valor.Value;
    }

    public static string Texto(string? valor, string campo, string archivo, string posicion)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: falta '{campo}'.");
        }
        return valor;
    }

    /// Texto que puede ser vacío pero no ausente.
    public static string TextoPresente(string? valor, string campo, string archivo, string posicion)
    {
        if (valor == null)
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: falta '{campo}'.");
        }
        return valor;
    }
}
