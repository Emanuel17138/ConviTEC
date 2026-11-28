using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Negocio;

/// Puerto <c>consultarCatalogo() → ProductoGlobal[]</c> que Inventario ofrece a Pedidos (Integrante 2). Es la misma
/// vista que sirve <c>GET /api/v1/catalogo</c> a Web.
public interface IConsultaCatalogo
{
    /// Verdadero cuando ya llegó la primera instantánea de cada otra sede configurada.
    bool EstaInicializado { get; }

    /// Vista global por productoId ascendente y, dentro de cada producto, por sedeId ascendente. Arreglo vacío si
    /// la vista está inicializada y no tiene productos.
    /// <exception cref="CatalogoInicializandoException">Falta la primera instantánea de alguna sede.</exception>
    ProductoGlobal[] ConsultarCatalogo();
}
