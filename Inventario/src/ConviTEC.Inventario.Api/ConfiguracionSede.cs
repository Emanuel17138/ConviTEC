using System;

namespace ConviTEC.Inventario.Api;

/// Parámetros de una instancia de sede: qué sede es, dónde están sus archivos, en qué dirección escucha
/// y cuáles son las otras sedes. Cada sede usa su propio directorio y su propio puerto (RNF12, RNF14).
public sealed class ConfiguracionSede
{
    public const string UrlPorDefecto = "http://localhost:5000";

    public string SedeId { get; }
    public string DirectorioDatos { get; }
    public string Url { get; }

    /// Las otras sedes, destino de las publicaciones de catálogo. Vacío si la sede opera sola.
    public string[] SedesRemotas { get; }

    public ConfiguracionSede(string sedeId, string directorioDatos, string url, string[]? sedesRemotas = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(directorioDatos, nameof(directorioDatos));
        ArgumentException.ThrowIfNullOrWhiteSpace(url, nameof(url));
        SedeId = sedeId;
        DirectorioDatos = directorioDatos;
        Url = url;
        SedesRemotas = Negocio.SedesRemotas.Validar(sedeId, sedesRemotas ?? Array.Empty<string>());
    }

    /// Lee los argumentos <c>--sede</c>, <c>--datos</c>, <c>--url</c> y <c>--remotas</c> (lista separada por comas);
    /// si faltan usa las variables de entorno <c>CONVITEC_SEDE</c>, <c>CONVITEC_DATOS</c>, <c>CONVITEC_URL</c> y
    /// <c>CONVITEC_REMOTAS</c>. Lanza ArgumentException si falta sede o datos, o si las remotas son inválidas.
    public static ConfiguracionSede Leer(string[] argumentos)
    {
        ArgumentNullException.ThrowIfNull(argumentos, nameof(argumentos));

        string? sede = ValorDe(argumentos, "--sede") ?? Environment.GetEnvironmentVariable("CONVITEC_SEDE");
        string? datos = ValorDe(argumentos, "--datos") ?? Environment.GetEnvironmentVariable("CONVITEC_DATOS");
        string? url = ValorDe(argumentos, "--url") ?? Environment.GetEnvironmentVariable("CONVITEC_URL");
        string? remotas = ValorDe(argumentos, "--remotas") ?? Environment.GetEnvironmentVariable("CONVITEC_REMOTAS");

        if (string.IsNullOrWhiteSpace(sede))
        {
            throw new ArgumentException("Falta la sede: use --sede <sedeId> o la variable CONVITEC_SEDE.");
        }
        if (string.IsNullOrWhiteSpace(datos))
        {
            throw new ArgumentException("Falta el directorio de datos: use --datos <ruta> o la variable CONVITEC_DATOS.");
        }
        return new ConfiguracionSede(
            sede, datos, string.IsNullOrWhiteSpace(url) ? UrlPorDefecto : url, SepararRemotas(remotas));
    }

    /// "ALA,SJ" → ["ALA","SJ"]. Texto vacío o ausente → ninguna. Un elemento vacío ("ALA,,SJ") se conserva
    /// para que la validación lo rechace con un mensaje claro.
    private static string[] SepararRemotas(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return Array.Empty<string>();
        }

        string[] partes = texto.Split(',');
        for (int i = 0; i < partes.Length; i++)
        {
            partes[i] = partes[i].Trim();
        }
        return partes;
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
