using System;

namespace ConviTEC.Inventario.Negocio;

/// Lista de las otras sedes: a ellas se publican los cambios de catálogo (RF03) y de ellas se esperan instantáneas.
public static class SedesRemotas
{
    /// Valida y devuelve una copia. Rechaza identificadores vacíos, repetidos (sensible a mayúsculas) o iguales a la sede propia.
    public static string[] Validar(string sedePropia, string[] sedesRemotas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedePropia, nameof(sedePropia));
        ArgumentNullException.ThrowIfNull(sedesRemotas);

        string[] copia = new string[sedesRemotas.Length];
        for (int i = 0; i < sedesRemotas.Length; i++)
        {
            string sede = sedesRemotas[i];
            if (string.IsNullOrWhiteSpace(sede))
            {
                throw new ArgumentException($"La sede remota {i + 1} está vacía.");
            }
            if (string.Equals(sede, sedePropia, StringComparison.Ordinal))
            {
                throw new ArgumentException($"La sede propia '{sedePropia}' no puede estar entre las remotas.");
            }
            for (int j = 0; j < i; j++)
            {
                if (string.Equals(sede, copia[j], StringComparison.Ordinal))
                {
                    throw new ArgumentException($"La sede remota '{sede}' está repetida.");
                }
            }
            copia[i] = sede;
        }
        return copia;
    }
}
