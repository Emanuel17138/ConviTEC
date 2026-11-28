using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ConviTEC.Inventario.Negocio;

/// Huella canónica del cuerpo de un comando: JSON camelCase con las líneas en el orden recibido y los nulos
/// explícitos. Dos entregas del mismo comando dan la misma huella; cualquier cambio de contenido, otra.
internal static class HuellaComando
{
    public static string DeReserva(SolicitudReserva solicitud)
    {
        using MemoryStream salida = new MemoryStream();
        using (Utf8JsonWriter escritor = new Utf8JsonWriter(salida))
        {
            escritor.WriteStartObject();
            escritor.WriteString("pedidoId", solicitud.PedidoId);
            if (solicitud.Lineas == null)
            {
                escritor.WriteNull("lineas");
            }
            else
            {
                escritor.WriteStartArray("lineas");
                for (int i = 0; i < solicitud.Lineas.Length; i++)
                {
                    EscribirLinea(escritor, solicitud.Lineas[i]);
                }
                escritor.WriteEndArray();
            }
            escritor.WriteEndObject();
        }
        return Encoding.UTF8.GetString(salida.ToArray());
    }

    public static string DeDecision(SolicitudDecision solicitud)
    {
        using MemoryStream salida = new MemoryStream();
        using (Utf8JsonWriter escritor = new Utf8JsonWriter(salida))
        {
            escritor.WriteStartObject();
            escritor.WriteString("pedidoId", solicitud.PedidoId);
            escritor.WriteEndObject();
        }
        return Encoding.UTF8.GetString(salida.ToArray());
    }

    private static void EscribirLinea(Utf8JsonWriter escritor, LineaSolicitada? linea)
    {
        if (linea == null)
        {
            escritor.WriteNullValue();
            return;
        }
        escritor.WriteStartObject();
        escritor.WriteString("productoId", linea.ProductoId);
        if (linea.Cantidad.HasValue)
        {
            escritor.WriteNumber("cantidad", linea.Cantidad.Value);
        }
        else
        {
            escritor.WriteNull("cantidad");
        }
        if (linea.PrecioUnitarioCentimos.HasValue)
        {
            escritor.WriteNumber("precioUnitarioCentimos", linea.PrecioUnitarioCentimos.Value);
        }
        else
        {
            escritor.WriteNull("precioUnitarioCentimos");
        }
        escritor.WriteEndObject();
    }
}
