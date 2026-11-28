using System;
using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Negocio;

/// La vista global no se puede consultar todavía: falta la primera instantánea de alguna otra sede. HTTP la
/// traduce a 503 <c>CATALOGO_INICIALIZANDO</c>; Pedidos la recibe por el puerto <see cref="IConsultaCatalogo"/>.
public sealed class CatalogoInicializandoException : Exception
{
    private readonly string[] _sedesPendientes;

    public string Codigo => CodigosCatalogo.CatalogoInicializando;

    /// Copia de las sedes cuya primera instantánea falta, en el orden de la configuración.
    public string[] SedesPendientes
    {
        get
        {
            string[] copia = new string[_sedesPendientes.Length];
            Array.Copy(_sedesPendientes, copia, _sedesPendientes.Length);
            return copia;
        }
    }

    public CatalogoInicializandoException(string[] sedesPendientes)
        : base(ConstruirMensaje(sedesPendientes))
    {
        _sedesPendientes = new string[sedesPendientes.Length];
        Array.Copy(sedesPendientes, _sedesPendientes, sedesPendientes.Length);
    }

    private static string ConstruirMensaje(string[] sedesPendientes)
    {
        ArgumentNullException.ThrowIfNull(sedesPendientes, nameof(sedesPendientes));
        return "El catálogo global se está inicializando: falta la primera instantánea de "
            + string.Join(", ", sedesPendientes) + ".";
    }
}
