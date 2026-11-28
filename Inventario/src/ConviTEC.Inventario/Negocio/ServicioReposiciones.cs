using System;
using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Negocio;

/// Regla de negocio de la reposición (RF02). Valida la solicitud antes de incrementar y aplica el efecto,
/// la clave de repetición y las publicaciones pendientes en un solo cambio de <see cref="InventarioLocal.Aplicar{T}"/>.
public sealed class ServicioReposiciones
{
    private readonly InventarioLocal _inventario;

    public ServicioReposiciones(InventarioLocal inventario)
    {
        ArgumentNullException.ThrowIfNull(inventario, nameof(inventario));
        _inventario = inventario;
    }

    /// <summary>
    /// Repone <paramref name="cantidad"/> unidades de <paramref name="productoId"/> con la clave <paramref name="operacionId"/>.
    /// Misma clave y mismo contenido devuelve el resultado guardado sin cambiar nada. Un fallo de disco sale
    /// como excepción y deja la memoria como estaba.
    /// </summary>
    public ResultadoReposicion Reponer(string productoId, string? operacionId, int? cantidad)
    {
        ArgumentNullException.ThrowIfNull(productoId, nameof(productoId));

        if (operacionId == null || !Guid.TryParseExact(operacionId, "D", out _))
        {
            return ResultadoReposicion.Error(EstadoReposicion.DatosInvalidos, CodigosReposicion.DatosInvalidos,
                "operacionId es obligatorio y debe ser un UUID en texto.");
        }
        if (cantidad == null || cantidad <= 0)
        {
            return ResultadoReposicion.Error(EstadoReposicion.CantidadInvalida, CodigosReposicion.CantidadInvalida,
                "La cantidad debe ser un entero positivo.");
        }

        int unidades = cantidad.Value;
        return _inventario.Aplicar(cambio =>
        {
            if (cambio.BuscarReposicion(operacionId, out ReposicionRegistrada? previa))
            {
                if (!string.Equals(previa.ProductoId, productoId, StringComparison.Ordinal) || previa.Cantidad != unidades)
                {
                    return ResultadoReposicion.Error(EstadoReposicion.ClaveReutilizada, CodigosReposicion.ClaveReutilizada,
                        $"La operación '{operacionId}' ya se usó con otro producto o con otra cantidad.");
                }
                if (!cambio.BuscarStock(previa.ProductoId, out StockLocal? vigente))
                {
                    throw new InvalidOperationException(
                        $"La reposición '{operacionId}' guardada apunta a un producto que ya no existe.");
                }
                return ResultadoReposicion.Exito(EstadoReposicion.Repetida,
                    new StockLocal(vigente.Producto, previa.Existencia, previa.Reservada));
            }

            if (!cambio.BuscarStock(productoId, out StockLocal? actual))
            {
                return ResultadoReposicion.Error(EstadoReposicion.ProductoDesconocido, CodigosReposicion.ProductoDesconocido,
                    $"El producto '{productoId}' no existe en esta sede.");
            }
            if ((long)actual.Existencia + unidades > int.MaxValue)
            {
                return ResultadoReposicion.Error(EstadoReposicion.CantidadInvalida, CodigosReposicion.CantidadInvalida,
                    "La cantidad desborda la existencia máxima admitida.");
            }

            int nuevaExistencia = actual.Existencia + unidades;
            cambio.ModificarStock(productoId, nuevaExistencia, actual.Reservada);
            cambio.RegistrarReposicion(new ReposicionRegistrada(operacionId, productoId, unidades, nuevaExistencia, actual.Reservada));
            return ResultadoReposicion.Exito(EstadoReposicion.Aplicada,
                new StockLocal(actual.Producto, nuevaExistencia, actual.Reservada));
        });
    }
}
