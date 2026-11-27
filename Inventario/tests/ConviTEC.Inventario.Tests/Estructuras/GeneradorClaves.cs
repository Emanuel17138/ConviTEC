using System;

namespace ConviTEC.Inventario.Tests.Estructuras;

/// <summary>Genera claves de prueba P000001, P000002, ... sin usar colecciones nativas ni ordenamientos incorporados.</summary>
internal static class GeneradorClaves
{
    public static string[] EnOrden(int cantidad)
    {
        string[] claves = new string[cantidad];
        for (int i = 0; i < cantidad; i++)
        {
            // Relleno de 6 dígitos: el orden ordinal coincide con el numérico hasta 999999 claves.
            claves[i] = "P" + (i + 1).ToString("D6");
        }
        return claves;
    }

    /// <summary>Las mismas claves en orden pseudoaleatorio reproducible (Fisher-Yates).</summary>
    public static string[] Barajadas(int cantidad, int semilla)
    {
        string[] claves = EnOrden(cantidad);
        Random azar = new Random(semilla);
        for (int i = claves.Length - 1; i > 0; i--)
        {
            int j = azar.Next(i + 1);
            (claves[i], claves[j]) = (claves[j], claves[i]);
        }
        return claves;
    }
}
