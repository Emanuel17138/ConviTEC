namespace ConviTEC.Inventario.Negocio;

/// Desenlace de una solicitud de reposición.
public enum EstadoReposicion
{
    /// Se incrementó la existencia y quedó registrada la clave.
    Aplicada,

    /// La misma clave con el mismo contenido: se devuelve el resultado guardado sin modificar nada.
    Repetida,

    /// La solicitud no es interpretable (por ejemplo, operacionId ausente o que no es UUID).
    DatosInvalidos,

    /// La cantidad no es un entero positivo (o sumarla desbordaría la existencia).
    CantidadInvalida,

    /// El producto no pertenece a esta sede.
    ProductoDesconocido,

    /// La clave ya se usó con otra cantidad u otro producto.
    ClaveReutilizada,
}
