using System;
using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Negocio;

/// Resultado de <see cref="ServicioReposiciones.Reponer"/>. Un rechazo esperado es un resultado, no una excepción.
public sealed class ResultadoReposicion
{
    public EstadoReposicion Estado { get; }

    /// StockLocal resultante de la operación. Solo existe si <see cref="EsExito"/>.
    public StockLocal? Stock { get; }

    /// Código del error REST; null si fue exitosa.
    public string? Codigo { get; }

    public string Mensaje { get; }

    public bool EsExito => Estado == EstadoReposicion.Aplicada || Estado == EstadoReposicion.Repetida;

    private ResultadoReposicion(EstadoReposicion estado, StockLocal? stock, string? codigo, string mensaje)
    {
        Estado = estado;
        Stock = stock;
        Codigo = codigo;
        Mensaje = mensaje;
    }

    internal static ResultadoReposicion Exito(EstadoReposicion estado, StockLocal stock)
    {
        ArgumentNullException.ThrowIfNull(stock, nameof(stock));
        return new ResultadoReposicion(estado, stock, null, "OK");
    }

    internal static ResultadoReposicion Error(EstadoReposicion estado, string codigo, string mensaje) =>
        new ResultadoReposicion(estado, null, codigo, mensaje);
}
