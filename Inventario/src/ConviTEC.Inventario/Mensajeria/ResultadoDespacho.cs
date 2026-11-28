using System;

namespace ConviTEC.Inventario.Mensajeria;

/// Qué debe hacer el consumidor con el mensaje de entrada una vez despachado.
public enum AccionConsumo
{
    /// Procesado y durable: publicar las salidas y acusar.
    Acusar,

    /// No se pudo procesar ahora (fallo de guardado): devolver el mensaje a la cola sin acusar.
    Reintentar,

    /// Ilegible o sin manejador: no hay a quién responder ni nada que reintentar. Se acusa y se descarta.
    Descartar,
}

/// Un mensaje que el consumidor debe publicar: clave de enrutamiento y sobre.
public sealed class SobreSaliente
{
    public string ClaveRuta { get; }
    public Sobre Sobre { get; }

    public SobreSaliente(string claveRuta, Sobre sobre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claveRuta, nameof(claveRuta));
        ArgumentNullException.ThrowIfNull(sobre, nameof(sobre));
        ClaveRuta = claveRuta;
        Sobre = sobre;
    }
}

/// Resultado de despachar un mensaje de entrada: la acción y, si procede, los mensajes que salen.
public sealed class ResultadoDespacho
{
    public AccionConsumo Accion { get; }
    public SobreSaliente[] Salidas { get; }

    /// Texto para el registro cuando la acción es Reintentar o Descartar.
    public string Motivo { get; }

    private ResultadoDespacho(AccionConsumo accion, SobreSaliente[] salidas, string motivo)
    {
        Accion = accion;
        Salidas = salidas;
        Motivo = motivo;
    }

    public static ResultadoDespacho Acusar(params SobreSaliente[] salidas)
    {
        ArgumentNullException.ThrowIfNull(salidas, nameof(salidas));
        return new ResultadoDespacho(AccionConsumo.Acusar, salidas, string.Empty);
    }

    public static ResultadoDespacho Reintentar(string motivo) =>
        new ResultadoDespacho(AccionConsumo.Reintentar, Array.Empty<SobreSaliente>(), motivo);

    public static ResultadoDespacho Descartar(string motivo) =>
        new ResultadoDespacho(AccionConsumo.Descartar, Array.Empty<SobreSaliente>(), motivo);
}
