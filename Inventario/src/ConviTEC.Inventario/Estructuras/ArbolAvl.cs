using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ConviTEC.Inventario.Estructuras;

/// Árbol AVL propiamente implementado. 
/// Garantiza inserción y búsqueda en O(log n) y recorrido en orden en O(n).
public sealed class ArbolAvl<TClave, TValor> : IIndiceOrdenado<TClave, TValor>
{
    private sealed class Nodo
    {
        public TClave Clave;
        public TValor Valor;
        public Nodo? Izquierdo;
        public Nodo? Derecho;
        public int Altura = 1;

        public Nodo(TClave clave, TValor valor)
        {
            Clave = clave;
            Valor = valor;
        }
    }

    private readonly IComparer<TClave> _comparador;
    private Nodo? _raiz;

    public ArbolAvl(IComparer<TClave> comparador)
    {
        ArgumentNullException.ThrowIfNull(comparador, nameof(comparador));
        _comparador = comparador;
    }

    public int Cantidad { get; private set; }

    public bool EstaVacio => _raiz == null;

    public int Altura => AlturaDe(_raiz);

    public bool Insertar(TClave clave, TValor valor)
    {
        ArgumentNullException.ThrowIfNull(clave, nameof(clave));

        bool insertado = false;
        _raiz = Insertar(_raiz, clave, valor, ref insertado);
        if (insertado)
        {
            Cantidad++;
        }
        return insertado;
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

    /// Borra la clave y rebalancea el camino recorrido: O(log n).
    public bool Eliminar(TClave clave)
    {
        ArgumentNullException.ThrowIfNull(clave, nameof(clave));

        bool eliminado = false;
        _raiz = Eliminar(_raiz, clave, ref eliminado);
        if (eliminado)
        {
            Cantidad--;
        }
        return eliminado;
    }

    public TClave[] ClavesEnOrden()
    {
        TClave[] resultado = new TClave[Cantidad];
        int posicion = 0;
        RecorrerClaves(_raiz, resultado, ref posicion);
        return resultado;
    }

    public TValor[] ValoresEnOrden()
    {
        TValor[] resultado = new TValor[Cantidad];
        int posicion = 0;
        RecorrerValores(_raiz, resultado, ref posicion);
        return resultado;
    }
    internal TClave ClaveRaiz =>
        _raiz != null ? _raiz.Clave : throw new InvalidOperationException("El árbol está vacío.");

    /// Solo para pruebas. Comprueba que las claves están en orden estricto, que las alturas guardadas
    /// son las reales y que en cada nodo los subárboles difieren como máximo en uno.
    internal bool VerificarInvariantes()
    {
        int contados = 0;
        bool balanceado = VerificarNodo(_raiz, ref contados) >= 0;
        if (!balanceado || contados != Cantidad)
        {
            return false;
        }

        TClave[] claves = ClavesEnOrden();
        for (int i = 1; i < claves.Length; i++)
        {
            if (_comparador.Compare(claves[i - 1], claves[i]) >= 0)
            {
                return false;
            }
        }
        return true;
    }

    private Nodo Insertar(Nodo? nodo, TClave clave, TValor valor, ref bool insertado)
    {
        if (nodo == null)
        {
            insertado = true;
            return new Nodo(clave, valor);
        }

        int comparacion = _comparador.Compare(clave, nodo.Clave);
        if (comparacion == 0)
        {
            return nodo; // Clave repetida: se conserva la entrada existente.
        }

        if (comparacion < 0)
        {
            nodo.Izquierdo = Insertar(nodo.Izquierdo, clave, valor, ref insertado);
        }
        else
        {
            nodo.Derecho = Insertar(nodo.Derecho, clave, valor, ref insertado);
        }

        if (!insertado)
        {
            return nodo;
        }
        return Rebalancear(nodo);
    }

    private Nodo? Eliminar(Nodo? nodo, TClave clave, ref bool eliminado)
    {
        if (nodo == null)
        {
            return null;
        }

        int comparacion = _comparador.Compare(clave, nodo.Clave);
        if (comparacion < 0)
        {
            nodo.Izquierdo = Eliminar(nodo.Izquierdo, clave, ref eliminado);
        }
        else if (comparacion > 0)
        {
            nodo.Derecho = Eliminar(nodo.Derecho, clave, ref eliminado);
        }
        else
        {
            eliminado = true;
            if (nodo.Izquierdo == null)
            {
                return nodo.Derecho;
            }
            if (nodo.Derecho == null)
            {
                return nodo.Izquierdo;
            }

            // Dos hijos: el sucesor en orden (el menor del subárbol derecho) ocupa su lugar.
            Nodo sucesor = nodo.Derecho;
            while (sucesor.Izquierdo != null)
            {
                sucesor = sucesor.Izquierdo;
            }
            nodo.Clave = sucesor.Clave;
            nodo.Valor = sucesor.Valor;
            bool quitado = false;
            nodo.Derecho = Eliminar(nodo.Derecho, sucesor.Clave, ref quitado);
        }

        if (!eliminado)
        {
            return nodo;
        }
        return Rebalancear(nodo);
    }

    private static Nodo Rebalancear(Nodo nodo)
    {
        ActualizarAltura(nodo);
        int factor = FactorDeBalance(nodo);

        if (factor > 1)
        {
            // Pesa la izquierda. Caso izquierda-derecha: primero se rota el hijo.
            if (FactorDeBalance(nodo.Izquierdo!) < 0)
            {
                nodo.Izquierdo = RotarIzquierda(nodo.Izquierdo!);
            }
            return RotarDerecha(nodo);
        }

        if (factor < -1)
        {
            // Pesa la derecha. Caso derecha-izquierda: primero se rota el hijo.
            if (FactorDeBalance(nodo.Derecho!) > 0)
            {
                nodo.Derecho = RotarDerecha(nodo.Derecho!);
            }
            return RotarIzquierda(nodo);
        }

        return nodo;
    }

    private static Nodo RotarDerecha(Nodo nodo)
    {
        Nodo nuevaRaiz = nodo.Izquierdo!;
        nodo.Izquierdo = nuevaRaiz.Derecho;
        nuevaRaiz.Derecho = nodo;
        ActualizarAltura(nodo);
        ActualizarAltura(nuevaRaiz);
        return nuevaRaiz;
    }

    private static Nodo RotarIzquierda(Nodo nodo)
    {
        Nodo nuevaRaiz = nodo.Derecho!;
        nodo.Derecho = nuevaRaiz.Izquierdo;
        nuevaRaiz.Izquierdo = nodo;
        ActualizarAltura(nodo);
        ActualizarAltura(nuevaRaiz);
        return nuevaRaiz;
    }

    private static int AlturaDe(Nodo? nodo) => nodo == null ? 0 : nodo.Altura;

    private static void ActualizarAltura(Nodo nodo) =>
        nodo.Altura = 1 + Math.Max(AlturaDe(nodo.Izquierdo), AlturaDe(nodo.Derecho));

    private static int FactorDeBalance(Nodo nodo) => AlturaDe(nodo.Izquierdo) - AlturaDe(nodo.Derecho);

    private void RecorrerClaves(Nodo? nodo, TClave[] destino, ref int posicion)
    {
        if (nodo == null)
        {
            return;
        }
        RecorrerClaves(nodo.Izquierdo, destino, ref posicion);
        destino[posicion++] = nodo.Clave;
        RecorrerClaves(nodo.Derecho, destino, ref posicion);
    }

    private void RecorrerValores(Nodo? nodo, TValor[] destino, ref int posicion)
    {
        if (nodo == null)
        {
            return;
        }
        RecorrerValores(nodo.Izquierdo, destino, ref posicion);
        destino[posicion++] = nodo.Valor;
        RecorrerValores(nodo.Derecho, destino, ref posicion);
    }

    ///Devuelve la altura real del subárbol, o -1 si alguna regla se rompe.
    private static int VerificarNodo(Nodo? nodo, ref int contados)
    {
        if (nodo == null)
        {
            return 0;
        }

        contados++;
        int izquierda = VerificarNodo(nodo.Izquierdo, ref contados);
        int derecha = VerificarNodo(nodo.Derecho, ref contados);
        if (izquierda < 0 || derecha < 0)
        {
            return -1;
        }

        int altura = 1 + Math.Max(izquierda, derecha);
        if (Math.Abs(izquierda - derecha) > 1 || nodo.Altura != altura)
        {
            return -1;
        }
        return altura;
    }
}
