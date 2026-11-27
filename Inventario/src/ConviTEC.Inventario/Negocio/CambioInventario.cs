using System;
using System.Diagnostics.CodeAnalysis;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;

namespace ConviTEC.Inventario.Negocio;

/// Cambio en preparación dentro de <see cref="InventarioLocal.Aplicar{T}"/>. Nada de lo que se anota aquí
/// toca el estado vivo ni el disco hasta que el snapshot completo quede guardado. Si la operación lanza una
/// excepción, el cambio se descarta entero.
/// Las lecturas ven primero lo preparado y después el estado vivo, así una operación ve sus propias escrituras.
public sealed class CambioInventario
{
    private readonly EstadoInventario _vivo;

    internal ArbolAvl<string, StockLocal> StockModificado { get; } = EstadoInventario.NuevoIndice<StockLocal>();
    internal ArbolAvl<string, LoteReserva> Lotes { get; } = EstadoInventario.NuevoIndice<LoteReserva>();
    internal ArbolAvl<string, ComandoRegistrado> Comandos { get; } = EstadoInventario.NuevoIndice<ComandoRegistrado>();
    internal ArbolAvl<string, ReposicionRegistrada> Reposiciones { get; } = EstadoInventario.NuevoIndice<ReposicionRegistrada>();

    internal CambioInventario(EstadoInventario vivo)
    {
        _vivo = vivo;
    }

    /// Verdadero si la operación no anotó nada: entonces no se guarda ni se sube ninguna revisión.
    internal bool EstaVacio =>
        StockModificado.EstaVacio && Lotes.EstaVacio && Comandos.EstaVacio && Reposiciones.EstaVacio;

    public bool BuscarStock(string productoId, [MaybeNullWhen(false)] out StockLocal stock)
    {
        ArgumentNullException.ThrowIfNull(productoId, nameof(productoId));
        if (StockModificado.Buscar(productoId, out stock))
        {
            return true;
        }
        if (_vivo.Stock.Buscar(productoId, out EntradaStock? entrada))
        {
            stock = entrada.Stock;
            return true;
        }
        stock = null;
        return false;
    }

    /// Fija las cantidades de un producto. Valida 0 ≤ reservada ≤ existencia. Toda llamada cuenta como una
    /// modificación: al guardar la revisión del producto sube una vez, aunque se llame varias veces.
    public void ModificarStock(string productoId, int existencia, int reservada)
    {
        if (!BuscarStock(productoId, out StockLocal? actual))
        {
            throw new ProductoDesconocidoException(productoId);
        }

        StockLocal nuevo = new StockLocal(actual.Producto, existencia, reservada);
        if (!StockModificado.Reemplazar(productoId, nuevo))
        {
            StockModificado.Insertar(productoId, nuevo);
        }
    }

    public bool BuscarLote(string pedidoId, [MaybeNullWhen(false)] out LoteReserva lote)
    {
        ArgumentNullException.ThrowIfNull(pedidoId, nameof(pedidoId));
        return Lotes.Buscar(pedidoId, out lote) || _vivo.Lotes.Buscar(pedidoId, out lote);
    }

    /// Anota un lote nuevo. Lanza <see cref="InvalidOperationException"/> si el pedido ya tiene lote.
    public void CrearLote(LoteReserva lote)
    {
        ArgumentNullException.ThrowIfNull(lote, nameof(lote));
        if (BuscarLote(lote.PedidoId, out _))
        {
            throw new InvalidOperationException($"El pedido '{lote.PedidoId}' ya tiene un lote en esta sede.");
        }
        Lotes.Insertar(lote.PedidoId, lote);
    }

    /// Sustituye un lote existente (por ejemplo, RESERVADA → CONFIRMADA). Lanza si el pedido no tiene lote.
    public void ActualizarLote(LoteReserva lote)
    {
        ArgumentNullException.ThrowIfNull(lote, nameof(lote));
        if (!BuscarLote(lote.PedidoId, out _))
        {
            throw new InvalidOperationException($"El pedido '{lote.PedidoId}' no tiene lote que actualizar.");
        }
        if (!Lotes.Reemplazar(lote.PedidoId, lote))
        {
            Lotes.Insertar(lote.PedidoId, lote);
        }
    }

    public bool BuscarComando(string mensajeId, [MaybeNullWhen(false)] out ComandoRegistrado comando)
    {
        ArgumentNullException.ThrowIfNull(mensajeId, nameof(mensajeId));
        return Comandos.Buscar(mensajeId, out comando) || _vivo.Comandos.Buscar(mensajeId, out comando);
    }

    /// Anota el resultado de un comando. Un mensajeId solo se registra una vez: decidir qué hacer con un
    /// duplicado le corresponde a quien llama, consultando antes con <see cref="BuscarComando"/>.
    public void RegistrarComando(ComandoRegistrado comando)
    {
        ArgumentNullException.ThrowIfNull(comando, nameof(comando));
        if (BuscarComando(comando.MensajeId, out _))
        {
            throw new InvalidOperationException($"El comando '{comando.MensajeId}' ya está registrado.");
        }
        Comandos.Insertar(comando.MensajeId, comando);
    }

    public bool BuscarReposicion(string operacionId, [MaybeNullWhen(false)] out ReposicionRegistrada reposicion)
    {
        ArgumentNullException.ThrowIfNull(operacionId, nameof(operacionId));
        return Reposiciones.Buscar(operacionId, out reposicion) || _vivo.Reposiciones.Buscar(operacionId, out reposicion);
    }

    /// Anota una reposición aplicada. Una operacionId solo se registra una vez (ver <see cref="RegistrarComando"/>).
    public void RegistrarReposicion(ReposicionRegistrada reposicion)
    {
        ArgumentNullException.ThrowIfNull(reposicion, nameof(reposicion));
        if (BuscarReposicion(reposicion.OperacionId, out _))
        {
            throw new InvalidOperationException($"La operación '{reposicion.OperacionId}' ya está registrada.");
        }
        Reposiciones.Insertar(reposicion.OperacionId, reposicion);
    }
}
