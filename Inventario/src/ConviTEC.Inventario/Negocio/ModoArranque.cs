namespace ConviTEC.Inventario.Negocio;

public enum ModoArranque
{
    /// No había estado guardado: se leyeron el maestro y las existencias iniciales.
    Inicial,

    /// Había estado guardado: se recuperaron las cantidades y se ignoraron las semillas.
    Recuperacion,
}
