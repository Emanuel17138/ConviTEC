namespace ConviTEC.Inventario.Persistencia;

/// Puntos del guardado atómico donde las pruebas pueden simular un corte del proceso.
internal enum EtapaGuardado
{
    /// El archivo temporal ya está completo y forzado a disco; el definitivo aún es el snapshot anterior.
    TemporalEscrito,

    /// El temporal ya reemplazó al definitivo: el snapshot nuevo es el vigente.
    Reemplazado,
}
