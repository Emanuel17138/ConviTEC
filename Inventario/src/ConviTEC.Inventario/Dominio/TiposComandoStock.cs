namespace ConviTEC.Inventario.Dominio;

/// Valores del campo <c>tipo</c> del sobre v1 para los comandos de stock y su respuesta.
public static class TiposComandoStock
{
    public const string Reservar = "stock.reservar";
    public const string Confirmar = "stock.confirmar";
    public const string Liberar = "stock.liberar";
    public const string Resultado = "stock.resultado";
}
