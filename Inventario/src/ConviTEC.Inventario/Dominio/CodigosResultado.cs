namespace ConviTEC.Inventario.Dominio;

/// Códigos del campo <c>codigo</c> de <c>stock.resultado</c> (contrato v1).
public static class CodigosResultado
{
    public const string Ok = "OK";
    public const string StockInsuficiente = "STOCK_INSUFICIENTE";
    public const string ProductoDesconocido = "PRODUCTO_DESCONOCIDO";
    public const string PrecioDistinto = "PRECIO_DISTINTO";
    public const string DatosInvalidos = "DATOS_INVALIDOS";
    public const string ClaveReutilizada = "CLAVE_REUTILIZADA";
    public const string EstadoIncompatible = "ESTADO_INCOMPATIBLE";
}
