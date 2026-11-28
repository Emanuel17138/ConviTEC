using ConviTEC.Inventario.Estructuras;

namespace ConviTEC.Inventario.Negocio;

/// Operaciones de los escritores únicos (<see cref="InventarioLocal"/> y <see cref="CatalogoReplicado"/>) para
/// guardar primero el contenido nuevo completo y solo después instalarlo en memoria.
internal static class FusionIndices
{
    /// Contenido del snapshot: lo vivo con las sustituciones de <paramref name="nuevo"/> en su lugar y,
    /// al final, las entradas que aún no existían. No modifica ninguno de los dos índices.
    public static T[] Fusionar<T>(ArbolAvl<string, T> vivo, ArbolAvl<string, T> nuevo)
    {
        string[] clavesVivas = vivo.ClavesEnOrden();
        T[] valoresVivos = vivo.ValoresEnOrden();
        string[] clavesNuevas = nuevo.ClavesEnOrden();
        T[] valoresNuevos = nuevo.ValoresEnOrden();

        int soloNuevos = 0;
        for (int i = 0; i < clavesNuevas.Length; i++)
        {
            if (!vivo.Contiene(clavesNuevas[i]))
            {
                soloNuevos++;
            }
        }

        T[] resultado = new T[valoresVivos.Length + soloNuevos];
        for (int i = 0; i < valoresVivos.Length; i++)
        {
            resultado[i] = nuevo.Buscar(clavesVivas[i], out T? sustituto) ? sustituto : valoresVivos[i];
        }
        int posicion = valoresVivos.Length;
        for (int i = 0; i < clavesNuevas.Length; i++)
        {
            if (!vivo.Contiene(clavesNuevas[i]))
            {
                resultado[posicion++] = valoresNuevos[i];
            }
        }
        return resultado;
    }

    /// Copia en <paramref name="vivo"/> cada entrada de <paramref name="nuevo"/>: sustituye la existente o la inserta.
    public static void Instalar<T>(ArbolAvl<string, T> vivo, ArbolAvl<string, T> nuevo)
    {
        string[] claves = nuevo.ClavesEnOrden();
        T[] valores = nuevo.ValoresEnOrden();
        for (int i = 0; i < claves.Length; i++)
        {
            if (!vivo.Reemplazar(claves[i], valores[i]))
            {
                vivo.Insertar(claves[i], valores[i]);
            }
        }
    }
}
