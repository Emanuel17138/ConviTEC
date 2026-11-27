using System;
using ConviTEC.Inventario.Estructuras;

namespace ConviTEC.Inventario.Tests.Estructuras;

/// <summary>Respalda con mediciones la justificación del AVL (ver docs/indice-productos.md).</summary>
public class ComparacionIndicesTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    public void InsercionesOrdenadas_ElAvlMantieneAlturaLogaritmicaYElBstSeConvierteEnLista(int cantidad)
    {
        string[] claves = GeneradorClaves.EnOrden(cantidad);
        ArbolAvl<string, int> avl = new ArbolAvl<string, int>(StringComparer.Ordinal);
        ArbolBinarioBusqueda<string, int> bst = new ArbolBinarioBusqueda<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < claves.Length; i++)
        {
            avl.Insertar(claves[i], i);
            bst.Insertar(claves[i], i);
        }

        int cotaAvl = (int)Math.Floor(1.4405 * Math.Log2(cantidad + 2));
        Assert.Equal(cantidad, bst.Altura);
        Assert.True(avl.Altura <= cotaAvl, $"Altura AVL {avl.Altura} supera la cota {cotaAvl}.");
        Assert.True(avl.Altura < bst.Altura);
    }

    [Fact]
    public void AmbosIndices_ProducenElMismoRecorridoYLosMismosValores()
    {
        string[] claves = GeneradorClaves.Barajadas(500, semilla: 11);
        ArbolAvl<string, int> avl = new ArbolAvl<string, int>(StringComparer.Ordinal);
        ArbolBinarioBusqueda<string, int> bst = new ArbolBinarioBusqueda<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < claves.Length; i++)
        {
            avl.Insertar(claves[i], i);
            bst.Insertar(claves[i], i);
        }

        Assert.Equal(bst.ClavesEnOrden(), avl.ClavesEnOrden());
        Assert.Equal(bst.ValoresEnOrden(), avl.ValoresEnOrden());
    }
}
