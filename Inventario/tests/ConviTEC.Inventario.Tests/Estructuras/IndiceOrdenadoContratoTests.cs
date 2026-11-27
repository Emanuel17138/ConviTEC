using System;
using ConviTEC.Inventario.Estructuras;

namespace ConviTEC.Inventario.Tests.Estructuras;

/// <summary>Comportamiento que debe cumplir cualquier índice ordenado. Lo heredan las pruebas de cada árbol.</summary>
public abstract class IndiceOrdenadoContratoTests
{
    protected abstract IIndiceOrdenado<string, int> CrearIndice();

    protected static void InsertarTodos(IIndiceOrdenado<string, int> indice, params string[] claves)
    {
        for (int i = 0; i < claves.Length; i++)
        {
            indice.Insertar(claves[i], i);
        }
    }

    [Theory]
    [InlineData("CAF", "ARR", "SAL")]
    [InlineData("SAL", "CAF", "ARR")]
    [InlineData("ARR", "CAF", "SAL")]
    [InlineData("SAL", "ARR", "CAF")]
    public void Recorrido_EnOrden_ProduceArrCafSalSinImportarElOrdenDeInsercion(string a, string b, string c)
    {
        IIndiceOrdenado<string, int> indice = CrearIndice();

        InsertarTodos(indice, a, b, c);

        Assert.Equal(new[] { "ARR", "CAF", "SAL" }, indice.ClavesEnOrden());
        Assert.Equal(3, indice.Cantidad);
    }

    [Fact]
    public void Buscar_DistingueMayusculasYReportaAusentes()
    {
        IIndiceOrdenado<string, int> indice = CrearIndice();
        InsertarTodos(indice, "CAF", "ARR", "SAL");

        Assert.True(indice.Buscar("CAF", out int valorCaf));
        Assert.Equal(0, valorCaf);
        Assert.False(indice.Buscar("caf", out _));
        Assert.False(indice.Buscar("XYZ", out _));
        Assert.True(indice.Contiene("SAL"));
        Assert.False(indice.Contiene("Sal"));
    }

    [Fact]
    public void Insertar_ClaveRepetida_DevuelveFalseYConservaUnaSolaEntradaOriginal()
    {
        IIndiceOrdenado<string, int> indice = CrearIndice();
        InsertarTodos(indice, "CAF", "ARR", "SAL");

        bool reinsertado = indice.Insertar("CAF", 99);

        Assert.False(reinsertado);
        Assert.Equal(3, indice.Cantidad);
        Assert.Equal(new[] { "ARR", "CAF", "SAL" }, indice.ClavesEnOrden());
        Assert.True(indice.Buscar("CAF", out int valor));
        Assert.Equal(0, valor); // Se conserva el valor original, no se reemplaza.
    }

    [Fact]
    public void IndiceVacio_NoFallaYDevuelveColeccionesVacias()
    {
        IIndiceOrdenado<string, int> indice = CrearIndice();

        Assert.True(indice.EstaVacio);
        Assert.Equal(0, indice.Cantidad);
        Assert.Equal(0, indice.Altura);
        Assert.False(indice.Buscar("CAF", out _));
        Assert.False(indice.Contiene("CAF"));
        Assert.Empty(indice.ClavesEnOrden());
        Assert.Empty(indice.ValoresEnOrden());
    }

    [Fact]
    public void ClaveNula_LanzaArgumentNullException()
    {
        IIndiceOrdenado<string, int> indice = CrearIndice();

        Assert.Throws<ArgumentNullException>(() => indice.Insertar(null!, 1));
        Assert.Throws<ArgumentNullException>(() => indice.Buscar(null!, out _));
        Assert.Throws<ArgumentNullException>(() => indice.Contiene(null!));
    }

    [Fact]
    public void ValoresEnOrden_SigueElOrdenDeLasClaves()
    {
        IIndiceOrdenado<string, int> indice = CrearIndice();
        indice.Insertar("SAL", 30);
        indice.Insertar("ARR", 10);
        indice.Insertar("CAF", 20);

        Assert.Equal(new[] { 10, 20, 30 }, indice.ValoresEnOrden());
    }

    [Fact]
    public void Reemplazar_ClaveExistente_CambiaElValorSinAlterarClavesNiCantidad()
    {
        IIndiceOrdenado<string, int> indice = CrearIndice();
        InsertarTodos(indice, "CAF", "ARR", "SAL");
        int alturaAntes = indice.Altura;

        bool reemplazado = indice.Reemplazar("CAF", 99);

        Assert.True(reemplazado);
        Assert.True(indice.Buscar("CAF", out int valor));
        Assert.Equal(99, valor);
        Assert.Equal(3, indice.Cantidad);
        Assert.Equal(alturaAntes, indice.Altura);
        Assert.Equal(new[] { "ARR", "CAF", "SAL" }, indice.ClavesEnOrden());
    }

    [Fact]
    public void Reemplazar_ClaveInexistenteOIndiceVacio_DevuelveFalseYNoInserta()
    {
        IIndiceOrdenado<string, int> indice = CrearIndice();

        Assert.False(indice.Reemplazar("CAF", 1));
        Assert.True(indice.EstaVacio);

        InsertarTodos(indice, "CAF", "ARR");
        Assert.False(indice.Reemplazar("caf", 1)); // distingue mayúsculas
        Assert.False(indice.Reemplazar("XYZ", 1));
        Assert.Equal(2, indice.Cantidad);
        Assert.False(indice.Contiene("XYZ"));
    }

    [Fact]
    public void Reemplazar_ClaveNula_Lanza()
    {
        IIndiceOrdenado<string, int> indice = CrearIndice();

        Assert.Throws<ArgumentNullException>(() => indice.Reemplazar(null!, 1));
    }

    [Fact]
    public void ArbolUnaSolaClave_TieneAlturaUno()
    {
        IIndiceOrdenado<string, int> indice = CrearIndice();

        indice.Insertar("CAF", 1);

        Assert.False(indice.EstaVacio);
        Assert.Equal(1, indice.Altura);
        Assert.Equal(new[] { "CAF" }, indice.ClavesEnOrden());
    }

    [Fact]
    public void ClavesAleatorias_QuedanOrdenadasYSinDuplicados()
    {
        IIndiceOrdenado<string, int> indice = CrearIndice();
        string[] claves = GeneradorClaves.Barajadas(1000, semilla: 42);

        for (int i = 0; i < claves.Length; i++)
        {
            Assert.True(indice.Insertar(claves[i], i));
        }

        Assert.Equal(1000, indice.Cantidad);
        Assert.Equal(GeneradorClaves.EnOrden(1000), indice.ClavesEnOrden());
        for (int i = 0; i < claves.Length; i++)
        {
            Assert.True(indice.Contiene(claves[i]));
        }
    }
}
