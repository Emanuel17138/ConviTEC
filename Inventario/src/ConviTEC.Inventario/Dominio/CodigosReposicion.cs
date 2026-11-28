namespace ConviTEC.Inventario.Dominio;

/// Códigos del campo <c>codigo</c> de los errores REST de la reposición (contrato v1).
/// Los que coinciden con <see cref="CodigosResultado"/> reutilizan su valor.
public static class CodigosReposicion
{
    public const string CantidadInvalida = "CANTIDAD_INVALIDA";
    public const string ProductoDesconocido = CodigosResultado.ProductoDesconocido;
    public const string ClaveReutilizada = CodigosResultado.ClaveReutilizada;
    public const string DatosInvalidos = CodigosResultado.DatosInvalidos;
}
