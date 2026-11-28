using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;

namespace ConviTEC.Inventario.Negocio;

/// Vista global del catálogo (RF03): combina la oferta propia (de <see cref="InventarioLocal"/>) con las ofertas
/// replicadas de las otras sedes (de <see cref="CatalogoReplicado"/>). Solo lee: no escribe ni envía nada.
///
/// El orden por productoId y luego sedeId sale de un AVL propio con la clave ordinal
/// <see cref="OfertaReplicada.ClaveDe"/>, sin ordenamientos incorporados: insertar es O(log n) por oferta y el
/// recorrido en orden entrega las ofertas de un mismo producto contiguas.
public sealed class ConsultaCatalogo : IConsultaCatalogo
{
    private readonly InventarioLocal _inventario;
    private readonly CatalogoReplicado _catalogo;

    public ConsultaCatalogo(InventarioLocal inventario, CatalogoReplicado catalogo)
    {
        ArgumentNullException.ThrowIfNull(inventario, nameof(inventario));
        ArgumentNullException.ThrowIfNull(catalogo, nameof(catalogo));
        if (!string.Equals(inventario.SedeId, catalogo.SedeId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"El inventario es de la sede '{inventario.SedeId}' y el catálogo de '{catalogo.SedeId}': deben ser la misma.");
        }
        _inventario = inventario;
        _catalogo = catalogo;
    }

    public bool EstaInicializado => _catalogo.EstaInicializado;

    public ProductoGlobal[] ConsultarCatalogo()
    {
        // La indisponibilidad se decide con la lectura de las sedes pendientes: nunca se devuelve una vista parcial.
        string[] pendientes = _catalogo.SedesPendientes();
        if (pendientes.Length > 0)
        {
            throw new CatalogoInicializandoException(pendientes);
        }

        ArbolAvl<string, OfertaReplicada> ofertas = EstadoInventario.NuevoIndice<OfertaReplicada>();
        AgregarOfertaPropia(ofertas);
        AgregarOfertasRemotas(ofertas);
        return Agrupar(ofertas.ValoresEnOrden());
    }

    /// Cada producto local, también el agotado, con su disponible y revisión actuales.
    private void AgregarOfertaPropia(ArbolAvl<string, OfertaReplicada> ofertas)
    {
        EntradaStock[] entradas = _inventario.ConsultarEntradas();
        for (int i = 0; i < entradas.Length; i++)
        {
            OfertaReplicada propia = new OfertaReplicada(
                _inventario.SedeId, entradas[i].Stock.Producto, entradas[i].Stock.Disponible, entradas[i].Revision);
            ofertas.Insertar(propia.Clave, propia);
        }
    }

    private void AgregarOfertasRemotas(ArbolAvl<string, OfertaReplicada> ofertas)
    {
        OfertaReplicada[] remotas = _catalogo.ConsultarOfertas();
        for (int i = 0; i < remotas.Length; i++)
        {
            ofertas.Insertar(remotas[i].Clave, remotas[i]);
        }
    }

    /// Recibe las ofertas ya en orden (productoId, sedeId) y arma un <see cref="ProductoGlobal"/> por producto.
    /// Un producto sin ninguna oferta no aparece, y una sede sin datos del producto tampoco.
    private ProductoGlobal[] Agrupar(OfertaReplicada[] ordenadas)
    {
        int productos = 0;
        for (int i = 0; i < ordenadas.Length; i++)
        {
            if (i == 0 || !MismoProducto(ordenadas[i - 1], ordenadas[i]))
            {
                productos++;
            }
        }

        ProductoGlobal[] vista = new ProductoGlobal[productos];
        int inicio = 0;
        int posicion = 0;
        while (inicio < ordenadas.Length)
        {
            int fin = inicio + 1;
            while (fin < ordenadas.Length && MismoProducto(ordenadas[inicio], ordenadas[fin]))
            {
                fin++;
            }
            vista[posicion++] = ConstruirProducto(ordenadas, inicio, fin);
            inicio = fin;
        }
        return vista;
    }

    private ProductoGlobal ConstruirProducto(OfertaReplicada[] ordenadas, int inicio, int fin)
    {
        Oferta[] ofertas = new Oferta[fin - inicio];
        Producto producto = ordenadas[inicio].Producto;
        for (int i = inicio; i < fin; i++)
        {
            ofertas[i - inicio] = new Oferta(ordenadas[i].SedeId, ordenadas[i].Disponible, ordenadas[i].Revision);
            // El maestro es común: si la sede tiene el producto, su definición es la de referencia.
            if (string.Equals(ordenadas[i].SedeId, _inventario.SedeId, StringComparison.Ordinal))
            {
                producto = ordenadas[i].Producto;
            }
        }
        return new ProductoGlobal(producto, ofertas);
    }

    private static bool MismoProducto(OfertaReplicada a, OfertaReplicada b) =>
        string.Equals(a.Producto.ProductoId, b.Producto.ProductoId, StringComparison.Ordinal);
}
