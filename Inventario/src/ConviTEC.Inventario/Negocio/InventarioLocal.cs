using System;
using System.Diagnostics.CodeAnalysis;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Negocio;

/// Estado de inventario de una sede y su único escritor. Todo cambio entra por <see cref="Aplicar{T}"/>:
/// bajo una exclusión mutua local se prepara el cambio completo, se guarda el snapshot entero de forma atómica
/// y solo después se instala en memoria. Así memoria y disco nunca quedan a medias (RNF15, RNF16).
/// Solo lo crea <see cref="CargadorInventario"/> con datos ya validados.
public sealed class InventarioLocal
{
    private readonly object _cerrojo = new object();
    private readonly EstadoInventario _estado;
    private readonly RepositorioInventario _repositorio;
    private readonly string[] _sedesRemotas;

    public string SedeId { get; }

    internal InventarioLocal(string sedeId, EstadoInventario estado, RepositorioInventario repositorio, string[] sedesRemotas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(estado, nameof(estado));
        ArgumentNullException.ThrowIfNull(repositorio, nameof(repositorio));
        SedeId = sedeId;
        _estado = estado;
        _repositorio = repositorio;
        _sedesRemotas = SedesRemotas.Validar(sedeId, sedesRemotas);
    }

    public int Cantidad
    {
        get
        {
            lock (_cerrojo)
            {
                return _estado.Stock.Cantidad;
            }
        }
    }

    /// Todo el stock de la sede por productoId ascendente, agotados incluidos. Arreglo vacío si no hay productos.
    public StockLocal[] ConsultarInventario()
    {
        lock (_cerrojo)
        {
            EntradaStock[] entradas = _estado.Stock.ValoresEnOrden();
            StockLocal[] stock = new StockLocal[entradas.Length];
            for (int i = 0; i < entradas.Length; i++)
            {
                stock[i] = entradas[i].Stock;
            }
            return stock;
        }
    }

    /// Búsqueda exacta (distingue mayúsculas). Devuelve false si el producto no es de esta sede.
    public bool BuscarStock(string productoId, [MaybeNullWhen(false)] out StockLocal stock)
    {
        ArgumentNullException.ThrowIfNull(productoId, nameof(productoId));
        lock (_cerrojo)
        {
            if (_estado.Stock.Buscar(productoId, out EntradaStock? entrada))
            {
                stock = entrada.Stock;
                return true;
            }
            stock = null;
            return false;
        }
    }

    public bool Contiene(string productoId)
    {
        ArgumentNullException.ThrowIfNull(productoId, nameof(productoId));
        lock (_cerrojo)
        {
            return _estado.Stock.Contiene(productoId);
        }
    }

    /// Revisión actual del producto (0 si nunca cambió desde el arranque inicial).
    public bool BuscarRevision(string productoId, out long revision)
    {
        ArgumentNullException.ThrowIfNull(productoId, nameof(productoId));
        lock (_cerrojo)
        {
            if (_estado.Stock.Buscar(productoId, out EntradaStock? entrada))
            {
                revision = entrada.Revision;
                return true;
            }
            revision = 0;
            return false;
        }
    }

    public bool BuscarLote(string pedidoId, [MaybeNullWhen(false)] out LoteReserva lote)
    {
        ArgumentNullException.ThrowIfNull(pedidoId, nameof(pedidoId));
        lock (_cerrojo)
        {
            return _estado.Lotes.Buscar(pedidoId, out lote);
        }
    }

    public bool BuscarComando(string mensajeId, [MaybeNullWhen(false)] out ComandoRegistrado comando)
    {
        ArgumentNullException.ThrowIfNull(mensajeId, nameof(mensajeId));
        lock (_cerrojo)
        {
            return _estado.Comandos.Buscar(mensajeId, out comando);
        }
    }

    public bool BuscarReposicion(string operacionId, [MaybeNullWhen(false)] out ReposicionRegistrada reposicion)
    {
        ArgumentNullException.ThrowIfNull(operacionId, nameof(operacionId));
        lock (_cerrojo)
        {
            return _estado.Reposiciones.Buscar(operacionId, out reposicion);
        }
    }

    /// Publicaciones de <c>catalogo.actualizado</c> aún no enviadas, ordenadas por destino y producto.
    public SalidaPendiente[] ConsultarSalidasPendientes()
    {
        lock (_cerrojo)
        {
            return _estado.Salidas.ValoresEnOrden();
        }
    }

    /// <summary>
    /// Ejecuta <paramref name="operacion"/> sobre un cambio en preparación y lo hace durable.
    /// <list type="number">
    /// <item>La operación lee y anota en el <see cref="CambioInventario"/>; si lanza, no se toca nada.</item>
    /// <item>Si anotó algo: sube una vez la revisión de cada producto modificado y reemplaza la publicación pendiente
    /// de cada otra sede por una nueva con el disponible y la revisión resultantes.</item>
    /// <item>Guarda el snapshot completo de forma atómica. Si falla, la excepción sale y la memoria queda como estaba.</item>
    /// <item>Solo con el snapshot guardado instala el cambio en memoria.</item>
    /// </list>
    /// El valor devuelto por la operación se entrega después de que todo quedó durable.
    /// </summary>
    public T Aplicar<T>(Func<CambioInventario, T> operacion)
    {
        ArgumentNullException.ThrowIfNull(operacion, nameof(operacion));

        lock (_cerrojo)
        {
            CambioInventario cambio = new CambioInventario(_estado);
            T resultado = operacion(cambio);
            if (cambio.EstaVacio)
            {
                return resultado;
            }

            ArbolAvl<string, EntradaStock> stockNuevo = PrepararStock(cambio);
            ArbolAvl<string, SalidaPendiente> salidasNuevas = PrepararSalidas(stockNuevo);

            _repositorio.GuardarEstado(ConversionEstado.Construir(
                SedeId,
                FusionIndices.Fusionar(_estado.Stock, stockNuevo),
                FusionIndices.Fusionar(_estado.Lotes, cambio.Lotes),
                FusionIndices.Fusionar(_estado.Comandos, cambio.Comandos),
                FusionIndices.Fusionar(_estado.Reposiciones, cambio.Reposiciones),
                FusionIndices.Fusionar(_estado.Salidas, salidasNuevas)));

            FusionIndices.Instalar(_estado.Stock, stockNuevo);
            FusionIndices.Instalar(_estado.Lotes, cambio.Lotes);
            FusionIndices.Instalar(_estado.Comandos, cambio.Comandos);
            FusionIndices.Instalar(_estado.Reposiciones, cambio.Reposiciones);
            FusionIndices.Instalar(_estado.Salidas, salidasNuevas);
            return resultado;
        }
    }

    /// Igual que <see cref="Aplicar{T}"/> para operaciones que no devuelven valor.
    public void Aplicar(Action<CambioInventario> operacion)
    {
        ArgumentNullException.ThrowIfNull(operacion, nameof(operacion));
        Aplicar<object?>(cambio =>
        {
            operacion(cambio);
            return null;
        });
    }

    /// Una entrada nueva por producto modificado, con la revisión viva + 1 (una sola vez por cambio).
    private ArbolAvl<string, EntradaStock> PrepararStock(CambioInventario cambio)
    {
        ArbolAvl<string, EntradaStock> nuevo = EstadoInventario.NuevoIndice<EntradaStock>();
        string[] productos = cambio.StockModificado.ClavesEnOrden();
        StockLocal[] stocks = cambio.StockModificado.ValoresEnOrden();
        for (int i = 0; i < productos.Length; i++)
        {
            _estado.Stock.Buscar(productos[i], out EntradaStock? vigente);
            nuevo.Insertar(productos[i], new EntradaStock(stocks[i], vigente!.Revision + 1));
        }
        return nuevo;
    }

    /// Por cada producto modificado y cada otra sede, una salida nueva. Sustituye a la pendiente de la misma pareja.
    private ArbolAvl<string, SalidaPendiente> PrepararSalidas(ArbolAvl<string, EntradaStock> stockNuevo)
    {
        ArbolAvl<string, SalidaPendiente> salidas = EstadoInventario.NuevoIndice<SalidaPendiente>();
        EntradaStock[] entradas = stockNuevo.ValoresEnOrden();
        for (int i = 0; i < entradas.Length; i++)
        {
            for (int j = 0; j < _sedesRemotas.Length; j++)
            {
                SalidaPendiente salida = new SalidaPendiente(
                    Guid.NewGuid().ToString(), _sedesRemotas[j],
                    entradas[i].Stock.Producto.ProductoId, entradas[i].Stock.Disponible, entradas[i].Revision);
                salidas.Insertar(salida.Clave, salida);
            }
        }
        return salidas;
    }
}
