using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ConviTEC.Inventario.Estructuras;

/// Árbol binario de búsqueda sin balanceo. Solo usado para comapración con el árbol AVL.
public sealed class ArbolBinarioBusqueda<TClave, TValor> : IIndiceOrdenado<TClave, TValor>
{
    private sealed class Nodo
    {
        public TClave Clave;
        public TValor Valor;
        public Nodo? Izquierdo;
        public Nodo? Derecho;

        public Nodo(TClave clave, TValor valor)
        {
            Clave = clave;
            Valor = valor;
        }
    }

    private readonly IComparer<TClave> _comparador;
    private Nodo? _raiz;

    public ArbolBinarioBusqueda(IComparer<TClave> comparador)
    {
        ArgumentNullException.ThrowIfNull(comparador, nameof(comparador));
        _comparador = comparador;
    }

    public int Cantidad { get; private set; }

    public bool EstaVacio => _raiz == null;

    ///Calcula la altura recorriendo por niveles con una cola propia sobre arreglo: O(n).
    public int Altura
    {
        get
        {
            if (_raiz == null)
            {
                return 0;
            }

            Nodo[] cola = new Nodo[Cantidad];
            int inicio = 0;
            int fin = 0;
            cola[fin++] = _raiz;
            int niveles = 0;
            while (inicio < fin)
            {
                int enNivel = fin - inicio;
                for (int i = 0; i < enNivel; i++)
                {
                    Nodo nodo = cola[inicio++];
                    if (nodo.Izquierdo != null)
                    {
                        cola[fin++] = nodo.Izquierdo;
                    }
                    if (nodo.Derecho != null)
                    {
                        cola[fin++] = nodo.Derecho;
                    }
                }
                niveles++;
            }
            return niveles;
        }
    }

    public bool Insertar(TClave clave, TValor valor)
    {
        ArgumentNullException.ThrowIfNull(clave, nameof(clave));

        if (_raiz == null)
        {
            _raiz = new Nodo(clave, valor);
            Cantidad++;
            return true;
        }

        Nodo actual = _raiz;
        while (true)
        {
            int comparacion = _comparador.Compare(clave, actual.Clave);
            if (comparacion == 0)
            {
                return false;
            }

            if (comparacion < 0)
            {
                if (actual.Izquierdo == null)
                {
                    actual.Izquierdo = new Nodo(clave, valor);
                    break;
                }
                actual = actual.Izquierdo;
            }
            else
            {
                if (actual.Derecho == null)
                {
                    actual.Derecho = new Nodo(clave, valor);
                    break;
                }
                actual = actual.Derecho;
            }
        }

        Cantidad++;
        return true;
    }

    public bool Buscar(TClave clave, [MaybeNullWhen(false)] out TValor valor)
    {
        ArgumentNullException.ThrowIfNull(clave, nameof(clave));

        Nodo? actual = _raiz;
        while (actual != null)
        {
            int comparacion = _comparador.Compare(clave, actual.Clave);
            if (comparacion == 0)
            {
                valor = actual.Valor;
                return true;
            }
            actual = comparacion < 0 ? actual.Izquierdo : actual.Derecho;
        }

        valor = default;
        return false;
    }

    public bool Contiene(TClave clave) => Buscar(clave, out _);

    public bool Reemplazar(TClave clave, TValor valor)
    {
        ArgumentNullException.ThrowIfNull(clave, nameof(clave));

        Nodo? actual = _raiz;
        while (actual != null)
        {
            int comparacion = _comparador.Compare(clave, actual.Clave);
            if (comparacion == 0)
            {
                actual.Valor = valor;
                return true;
            }
            actual = comparacion < 0 ? actual.Izquierdo : actual.Derecho;
        }
        return false;
    }

    public TClave[] ClavesEnOrden()
    {
        Nodo[] nodos = NodosEnOrden();
        TClave[] claves = new TClave[nodos.Length];
        for (int i = 0; i < nodos.Length; i++)
        {
            claves[i] = nodos[i].Clave;
        }
        return claves;
    }

    public TValor[] ValoresEnOrden()
    {
        Nodo[] nodos = NodosEnOrden();
        TValor[] valores = new TValor[nodos.Length];
        for (int i = 0; i < nodos.Length; i++)
        {
            valores[i] = nodos[i].Valor;
        }
        return valores;
    }

    ///Recorrido en orden iterativo con una pila propia sobre arreglo (tamaño máximo: n nodos).
    private Nodo[] NodosEnOrden()
    {
        Nodo[] resultado = new Nodo[Cantidad];
        Nodo[] pila = new Nodo[Cantidad];
        int tope = 0;
        int posicion = 0;
        Nodo? actual = _raiz;

        while (actual != null || tope > 0)
        {
            while (actual != null)
            {
                pila[tope++] = actual;
                actual = actual.Izquierdo;
            }
            actual = pila[--tope];
            resultado[posicion++] = actual;
            actual = actual.Derecho;
        }
        return resultado;
    }
}
