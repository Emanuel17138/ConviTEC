using System;
using System.Security.Cryptography;
using System.Text;

namespace ConviTEC.Inventario.Mensajeria;

/// UUID determinista (estilo v5, SHA-1) para el <c>mensajeId</c> de la respuesta a un comando. Si el comando
/// se reentrega y se vuelve a responder, la respuesta lleva el mismo ID, así quien la recibe puede detectar el
/// duplicado sin que haya que guardar nada más.
public static class IdentificadorDerivado
{
    private const string Espacio = "convitec.v1/stock.resultado/";

    public static string DeComando(string mensajeIdComando)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mensajeIdComando, nameof(mensajeIdComando));

        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(Espacio + mensajeIdComando));
        byte[] b = new byte[16];
        Array.Copy(hash, b, 16);
        b[6] = (byte)((b[6] & 0x0F) | 0x50);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);

        string hex = Convert.ToHexString(b).ToLowerInvariant();
        return hex.Substring(0, 8) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4) + "-"
            + hex.Substring(16, 4) + "-" + hex.Substring(20, 12);
    }
}
