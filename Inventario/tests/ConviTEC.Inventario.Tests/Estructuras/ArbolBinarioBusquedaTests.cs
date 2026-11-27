using System;
using ConviTEC.Inventario.Estructuras;

namespace ConviTEC.Inventario.Tests.Estructuras;

public class ArbolBinarioBusquedaTests : IndiceOrdenadoContratoTests
{
    protected override IIndiceOrdenado<string, int> CrearIndice() =>
        new ArbolBinarioBusqueda<string, int>(StringComparer.Ordinal);

    [Fact]
    public void SinBalance_CBA_QuedaComoCadenaDeAlturaTres()
    {
        IIndiceOrdenado<string, int> arbol = CrearIndice();

        InsertarTodos(arbol, "C", "B", "A");

        Assert.Equal(3, arbol.Altura);
        Assert.Equal(new[] { "A", "B", "C" }, arbol.ClavesEnOrden());
    }

    [Fact]
    public void ClavesOrdenadas_DegeneranEnListaSinDesbordarLaPila()
    {
        IIndiceOrdenado<string, int> arbol = CrearIndice();
        string[] claves = GeneradorClaves.EnOrden(20000);

        for (int i = 0; i < claves.Length; i++)
        {
            arbol.Insertar(claves[i], i);
        }

        Assert.Equal(20000, arbol.Altura);
        Assert.Equal(claves, arbol.ClavesEnOrden());
    }

    [Fact]
    public void ComparadorNulo_LanzaArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ArbolBinarioBusqueda<string, int>(null!));
    }
}
