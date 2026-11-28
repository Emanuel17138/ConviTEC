using System.Diagnostics.CodeAnalysis;

namespace ConviTEC.Inventario.Estructuras;

/// Índice propio clave-valor que mantiene las claves ordenadas. No depende de archivos ni de HTTP.
public interface IIndiceOrdenado<TClave, TValor>
{
    int Cantidad { get; }

    bool EstaVacio { get; }

    ///Altura del árbol en nodos. Un índice vacío tiene altura 0.
    int Altura { get; }

    /// Inserta la pareja. Devuelve false y no modifica nada si la clave ya existe.
    bool Insertar(TClave clave, TValor valor);

    ///Búsqueda exacta. Lanza <see cref="System.ArgumentNullException"/> si la clave es nula.
    bool Buscar(TClave clave, [MaybeNullWhen(false)] out TValor valor);

    bool Contiene(TClave clave);

    /// Sustituye el valor de una clave que ya existe, sin cambiar la forma del árbol.
    /// Devuelve false y no modifica nada si la clave no existe. Lanza <see cref="System.ArgumentNullException"/> si es nula.
    bool Reemplazar(TClave clave, TValor valor);

    /// Quita la clave y su valor. Devuelve false y no modifica nada si la clave no existe.
    /// Lanza <see cref="System.ArgumentNullException"/> si la clave es nula.
    bool Eliminar(TClave clave);

    ///Claves en orden ascendente. Arreglo de longitud 0 si el índice está vacío.
    TClave[] ClavesEnOrden();

    ///Valores en el orden ascendente de sus claves. Arreglo de longitud 0 si el índice está vacío.
    TValor[] ValoresEnOrden();
}
