using System;
using ConviTEC.Inventario.Estructuras;

namespace ConviTEC.Inventario.Tests.Estructuras;

public class ArbolAvlTests : IndiceOrdenadoContratoTests
{
    protected override IIndiceOrdenado<string, int> CrearIndice() => CrearArbol();

    private static ArbolAvl<string, int> CrearArbol() => new ArbolAvl<string, int>(StringComparer.Ordinal);

    [Theory]
    [InlineData("C", "B", "A")] // Rotación simple a la derecha
    [InlineData("A", "B", "C")] // Rotación simple a la izquierda
    [InlineData("C", "A", "B")] // Rotación izquierda-derecha
    [InlineData("A", "C", "B")] // Rotación derecha-izquierda
    public void TresClaves_EnCualquierOrden_DejanBComoRaizYArbolBalanceado(string primera, string segunda, string tercera)
    {
        ArbolAvl<string, int> arbol = CrearArbol();

        arbol.Insertar(primera, 1);
        arbol.Insertar(segunda, 2);
        arbol.Insertar(tercera, 3);

        Assert.Equal("B", arbol.ClaveRaiz);
        Assert.Equal(new[] { "A", "B", "C" }, arbol.ClavesEnOrden());
        Assert.Equal(2, arbol.Altura);
        Assert.True(arbol.VerificarInvariantes());
    }

    [Fact]
    public void ClaveRaiz_EnArbolVacio_Lanza()
    {
        ArbolAvl<string, int> arbol = CrearArbol();

        Assert.Throws<InvalidOperationException>(() => arbol.ClaveRaiz);
    }

    [Fact]
    public void ClavesOrdenadas_MantienenElBalanceYAlturaLogaritmica()
    {
        ArbolAvl<string, int> arbol = CrearArbol();
        string[] claves = GeneradorClaves.EnOrden(1000);

        for (int i = 0; i < claves.Length; i++)
        {
            arbol.Insertar(claves[i], i);
            if (i % 100 == 0)
            {
                Assert.True(arbol.VerificarInvariantes());
            }
        }

        Assert.True(arbol.VerificarInvariantes());
        Assert.InRange(arbol.Altura, 10, 14); // log2(1000) ≈ 10 ≤ h ≤ 1.44·log2(1002) ≈ 14
    }

    [Fact]
    public void ClavesAleatorias_MantienenInvariantes()
    {
        ArbolAvl<string, int> arbol = CrearArbol();
        string[] claves = GeneradorClaves.Barajadas(1000, semilla: 7);

        for (int i = 0; i < claves.Length; i++)
        {
            arbol.Insertar(claves[i], i);
        }

        Assert.True(arbol.VerificarInvariantes());
    }

    [Fact]
    public void ClavesRepetidas_NoRompenElBalance()
    {
        ArbolAvl<string, int> arbol = CrearArbol();
        string[] claves = GeneradorClaves.Barajadas(50, semilla: 3);

        for (int vuelta = 0; vuelta < 3; vuelta++)
        {
            for (int i = 0; i < claves.Length; i++)
            {
                arbol.Insertar(claves[i], i);
            }
        }

        Assert.Equal(50, arbol.Cantidad);
        Assert.True(arbol.VerificarInvariantes());
    }

    [Fact]
    public void ComparadorOrdinal_DistingueMayusculasYMinusculas()
    {
        ArbolAvl<string, int> arbol = CrearArbol();

        arbol.Insertar("caf", 1);
        arbol.Insertar("CAF", 2);

        Assert.Equal(2, arbol.Cantidad);
        Assert.Equal(new[] { "CAF", "caf" }, arbol.ClavesEnOrden()); // Orden ordinal: mayúsculas primero.
    }

    [Fact]
    public void ComparadorNulo_LanzaArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ArbolAvl<string, int>(null!));
    }
}
