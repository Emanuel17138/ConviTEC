using System;

namespace ConviTEC.Inventario.Api;

/// Parámetros de una instancia de sede: qué sede es, dónde están sus archivos y en qué dirección escucha.
/// Cada sede usa su propio directorio y su propio puerto (RNF12, RNF14).
public sealed class ConfiguracionSede
{
    public const string UrlPorDefecto = "http://localhost:5000";

    public string SedeId { get; }
    public string DirectorioDatos { get; }
    public string Url { get; }

    public ConfiguracionSede(string sedeId, string directorioDatos, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(directorioDatos, nameof(directorioDatos));
        ArgumentException.ThrowIfNullOrWhiteSpace(url, nameof(url));
        SedeId = sedeId;
        DirectorioDatos = directorioDatos;
        Url = url;
    }

    /// Lee los argumentos <c>--sede</c>, <c>--datos</c> y <c>--url</c>; si faltan usa las variables de entorno
    /// <c>CONVITEC_SEDE</c>, <c>CONVITEC_DATOS</c> y <c>CONVITEC_URL</c>. Lanza ArgumentException si falta sede o datos.
    public static ConfiguracionSede Leer(string[] argumentos)
    {
        ArgumentNullException.ThrowIfNull(argumentos, nameof(argumentos));

        string? sede = ValorDe(argumentos, "--sede") ?? Environment.GetEnvironmentVariable("CONVITEC_SEDE");
        string? datos = ValorDe(argumentos, "--datos") ?? Environment.GetEnvironmentVariable("CONVITEC_DATOS");
        string? url = ValorDe(argumentos, "--url") ?? Environment.GetEnvironmentVariable("CONVITEC_URL");

        if (string.IsNullOrWhiteSpace(sede))
        {
            throw new ArgumentException("Falta la sede: use --sede <sedeId> o la variable CONVITEC_SEDE.");
        }
        if (string.IsNullOrWhiteSpace(datos))
        {
            throw new ArgumentException("Falta el directorio de datos: use --datos <ruta> o la variable CONVITEC_DATOS.");
        }
        return new ConfiguracionSede(sede, datos, string.IsNullOrWhiteSpace(url) ? UrlPorDefecto : url);
    }

    private static string? ValorDe(string[] argumentos, string nombre)
    {
        for (int i = 0; i < argumentos.Length - 1; i++)
        {
            if (string.Equals(argumentos[i], nombre, StringComparison.Ordinal))
            {
                return argumentos[i + 1];
            }
        }
        return null;
    }
}
