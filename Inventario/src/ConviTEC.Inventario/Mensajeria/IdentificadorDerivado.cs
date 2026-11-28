using System;
using System.Security.Cryptography;
using System.Text;

namespace ConviTEC.Inventario.Mensajeria;

/// UUID determinista (estilo v5, SHA-1) para el <c>mensajeId</c> de la respuesta a un mensaje recibido. Si el mensaje
/// se reentrega y se vuelve a responder, la respuesta lleva el mismo ID, así quien la recibe puede detectar el
/// duplicado sin que haya que guardar nada más. Cada tipo de respuesta usa su propio espacio de nombres.
public static class IdentificadorDerivado
{
    private const string EspacioResultado = "convitec.v1/stock.resultado/";
    private const string EspacioInstantanea = "convitec.v1/catalogo.instantanea/";

    /// ID del <c>stock.resultado</c> que responde al comando <paramref name="mensajeIdComando"/>.
    public static string DeComando(string mensajeIdComando) => Derivar(EspacioResultado, mensajeIdComando);

    /// ID del <c>catalogo.instantanea</c> que responde a la solicitud <paramref name="mensajeIdSolicitud"/>.
    public static string DeSolicitudCatalogo(string mensajeIdSolicitud) => Derivar(EspacioInstantanea, mensajeIdSolicitud);

    private static string Derivar(string espacio, string mensajeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mensajeId, nameof(mensajeId));

        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(espacio + mensajeId));
        byte[] b = new byte[16];
        Array.Copy(hash, b, 16);
        b[6] = (byte)((b[6] & 0x0F) | 0x50);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);

        string hex = Convert.ToHexString(b).ToLowerInvariant();
        return hex.Substring(0, 8) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4) + "-"
            + hex.Substring(16, 4) + "-" + hex.Substring(20, 12);
    }
}
