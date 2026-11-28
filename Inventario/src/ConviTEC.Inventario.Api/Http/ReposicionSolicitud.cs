using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConviTEC.Inventario.Api.Http;

/// Cuerpo <c>{operacionId,cantidad}</c> de POST …/reposiciones. Se lee a mano para que cualquier defecto del
/// cuerpo termine en el error común {codigo, mensaje} y no en la respuesta por defecto del framework.
/// Los campos ausentes o de tipo incorrecto quedan en null: quien valida es el negocio.
public sealed record ReposicionSolicitud(string? OperacionId, int? Cantidad)
{
    /// Devuelve null si el cuerpo está vacío, no es JSON válido o su raíz no es un objeto.
    public static async Task<ReposicionSolicitud?> LeerAsync(Stream cuerpo, CancellationToken cancelacion)
    {
        JsonDocument documento;
        try
        {
            documento = await JsonDocument.ParseAsync(cuerpo, cancellationToken: cancelacion);
        }
        catch (JsonException)
        {
            return null;
        }

        using (documento)
        {
            JsonElement raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? operacionId = null;
            if (raiz.TryGetProperty("operacionId", out JsonElement operacion) && operacion.ValueKind == JsonValueKind.String)
            {
                operacionId = operacion.GetString();
            }

            int? cantidad = null;
            if (raiz.TryGetProperty("cantidad", out JsonElement valor)
                && valor.ValueKind == JsonValueKind.Number
                && valor.TryGetInt32(out int entero))
            {
                cantidad = entero;
            }

            return new ReposicionSolicitud(operacionId, cantidad);
        }
    }
}
