using System;
using System.IO;
using System.Text;
using System.Text.Json;
using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Mensajeria;

/// Cuerpos de <c>catalogo.actualizado</c> (<c>{producto,disponible,revision}</c>) y <c>catalogo.instantanea</c>
/// (<c>{entradas:[...]}</c>). El formato vive en un solo lugar: lo escribe la sede propietaria y lo lee la réplica.
/// La lectura nunca lanza: reporta el motivo para que el manejador descarte el mensaje sin bloquear la cola.
internal static class CuerpoCatalogo
{
    public static string EscribirCambio(EntradaCatalogo entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada, nameof(entrada));

        using MemoryStream salida = new MemoryStream();
        using (Utf8JsonWriter escritor = new Utf8JsonWriter(salida))
        {
            EscribirEntrada(escritor, entrada);
        }
        return Encoding.UTF8.GetString(salida.ToArray());
    }

    public static string EscribirInstantanea(EntradaCatalogo[] entradas)
    {
        ArgumentNullException.ThrowIfNull(entradas, nameof(entradas));

        using MemoryStream salida = new MemoryStream();
        using (Utf8JsonWriter escritor = new Utf8JsonWriter(salida))
        {
            escritor.WriteStartObject();
            escritor.WriteStartArray("entradas");
            for (int i = 0; i < entradas.Length; i++)
            {
                EscribirEntrada(escritor, entradas[i]);
            }
            escritor.WriteEndArray();
            escritor.WriteEndObject();
        }
        return Encoding.UTF8.GetString(salida.ToArray());
    }

    public static bool TryLeerCambio(JsonElement cuerpo, out EntradaCatalogo? entrada, out string motivo) =>
        TryLeerEntrada(cuerpo, "el cuerpo", out entrada, out motivo);

    public static bool TryLeerInstantanea(JsonElement cuerpo, out EntradaCatalogo[]? entradas, out string motivo)
    {
        entradas = null;
        if (cuerpo.ValueKind != JsonValueKind.Object
            || !cuerpo.TryGetProperty("entradas", out JsonElement lista)
            || lista.ValueKind != JsonValueKind.Array)
        {
            motivo = "El cuerpo de catalogo.instantanea debe ser un objeto con 'entradas' como arreglo.";
            return false;
        }

        EntradaCatalogo[] leidas = new EntradaCatalogo[lista.GetArrayLength()];
        int posicion = 0;
        foreach (JsonElement elemento in lista.EnumerateArray())
        {
            if (!TryLeerEntrada(elemento, $"entradas[{posicion}]", out EntradaCatalogo? entrada, out motivo))
            {
                return false;
            }
            leidas[posicion++] = entrada!;
        }

        entradas = leidas;
        motivo = string.Empty;
        return true;
    }

    private static void EscribirEntrada(Utf8JsonWriter escritor, EntradaCatalogo entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada, nameof(entrada));

        escritor.WriteStartObject();
        escritor.WriteStartObject("producto");
        escritor.WriteString("productoId", entrada.Producto.ProductoId);
        escritor.WriteString("nombre", entrada.Producto.Nombre);
        escritor.WriteString("descripcion", entrada.Producto.Descripcion);
        escritor.WriteString("categoria", entrada.Producto.Categoria);
        escritor.WriteNumber("precioUnitarioCentimos", entrada.Producto.PrecioUnitarioCentimos);
        escritor.WriteEndObject();
        escritor.WriteNumber("disponible", entrada.Disponible);
        escritor.WriteNumber("revision", entrada.Revision);
        escritor.WriteEndObject();
    }

    private static bool TryLeerEntrada(JsonElement elemento, string donde, out EntradaCatalogo? entrada, out string motivo)
    {
        entrada = null;
        if (elemento.ValueKind != JsonValueKind.Object)
        {
            motivo = $"{donde} debe ser un objeto.";
            return false;
        }
        if (!elemento.TryGetProperty("producto", out JsonElement productoJson))
        {
            motivo = $"{donde}: falta 'producto'.";
            return false;
        }
        if (!TryLeerProducto(productoJson, out Producto? producto, out string causa))
        {
            motivo = $"{donde}: {causa}";
            return false;
        }

        if (!elemento.TryGetProperty("disponible", out JsonElement disponible)
            || disponible.ValueKind != JsonValueKind.Number
            || !disponible.TryGetInt32(out int unidades)
            || unidades < 0)
        {
            motivo = $"{donde}: 'disponible' falta o no es un entero no negativo.";
            return false;
        }
        if (!elemento.TryGetProperty("revision", out JsonElement revision)
            || revision.ValueKind != JsonValueKind.Number
            || !revision.TryGetInt64(out long numeroRevision)
            || numeroRevision < 0)
        {
            motivo = $"{donde}: 'revision' falta o no es un entero no negativo.";
            return false;
        }

        entrada = new EntradaCatalogo(producto!, unidades, numeroRevision);
        motivo = string.Empty;
        return true;
    }

    private static bool TryLeerProducto(JsonElement json, out Producto? producto, out string motivo)
    {
        producto = null;
        if (json.ValueKind != JsonValueKind.Object
            || !TryTexto(json, "productoId", out string? productoId)
            || !TryTexto(json, "nombre", out string? nombre)
            || !TryTexto(json, "descripcion", out string? descripcion)
            || !TryTexto(json, "categoria", out string? categoria)
            || !json.TryGetProperty("precioUnitarioCentimos", out JsonElement precio)
            || precio.ValueKind != JsonValueKind.Number
            || !precio.TryGetInt64(out long centimos))
        {
            motivo = "'producto' mal formado.";
            return false;
        }

        try
        {
            producto = new Producto(productoId!, nombre!, descripcion!, categoria!, centimos);
        }
        catch (ArgumentException e)
        {
            motivo = "'producto' inválido: " + e.Message;
            return false;
        }
        motivo = string.Empty;
        return true;
    }

    private static bool TryTexto(JsonElement objeto, string campo, out string? valor)
    {
        valor = null;
        if (!objeto.TryGetProperty(campo, out JsonElement elemento) || elemento.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        valor = elemento.GetString();
        return valor != null;
    }
}
