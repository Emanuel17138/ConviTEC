using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Mensajeria;

/// Valores del campo <c>tipo</c> del sobre v1 para la réplica del catálogo (RF03). Los tres viajan por la cola
/// <c>inventario.&lt;sedeId&gt;</c> del destinatario.
public static class TiposMensajeCatalogo
{
    /// Cambio absoluto de una oferta: cuerpo <c>{producto,disponible,revision}</c>.
    public const string Actualizado = SalidaPendiente.TipoMensaje;

    /// Petición de la copia completa de la sede destino: cuerpo <c>{}</c>.
    public const string Solicitar = "catalogo.solicitar";

    /// Copia completa de las ofertas de la sede emisora: cuerpo <c>{entradas:[{producto,disponible,revision}]}</c>.
    public const string Instantanea = "catalogo.instantanea";
}
