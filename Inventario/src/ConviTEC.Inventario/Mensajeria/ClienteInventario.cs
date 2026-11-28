using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConviTEC.Inventario.Dominio;

namespace ConviTEC.Inventario.Mensajeria;

/// Cliente de inventario para Pedidos (Integrante 2): arma y envía <c>stock.reservar</c>, <c>stock.confirmar</c> y
/// <c>stock.liberar</c> a la cola <c>inventario.&lt;proveedora&gt;</c> y lee el <c>stock.resultado</c> que vuelve por
/// <c>pedidos.&lt;coordinadora&gt;</c>.
///
/// Los métodos <c>Crear*</c> reciben el <c>mensajeId</c>: Pedidos lo genera, guarda la intención con ese ID y, ante
/// pérdida de acuse o falta de destinatario, reenvía el MISMO sobre. Una petición nueva usa su mensajeId como
/// correlacionId.
public sealed class ClienteInventario
{
    private readonly string _sedeId;
    private readonly IPublicadorMensajes _publicador;

    public ClienteInventario(string sedeId, IPublicadorMensajes publicador)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(publicador, nameof(publicador));
        _sedeId = sedeId;
        _publicador = publicador;
    }

    public static string NuevoMensajeId() => Guid.NewGuid().ToString();

    public Sobre CrearReservar(string mensajeId, string proveedoraSedeId, string pedidoId, LineaReserva[] lineas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pedidoId, nameof(pedidoId));
        ArgumentNullException.ThrowIfNull(lineas, nameof(lineas));

        using MemoryStream salida = new MemoryStream();
        using (Utf8JsonWriter escritor = new Utf8JsonWriter(salida))
        {
            escritor.WriteStartObject();
            escritor.WriteString("pedidoId", pedidoId);
            escritor.WriteStartArray("lineas");
            for (int i = 0; i < lineas.Length; i++)
            {
                ArgumentNullException.ThrowIfNull(lineas[i], nameof(lineas));
                escritor.WriteStartObject();
                escritor.WriteString("productoId", lineas[i].ProductoId);
                escritor.WriteNumber("cantidad", lineas[i].Cantidad);
                escritor.WriteNumber("precioUnitarioCentimos", lineas[i].PrecioUnitarioCentimos);
                escritor.WriteEndObject();
            }
            escritor.WriteEndArray();
            escritor.WriteEndObject();
        }
        return Crear(TiposComandoStock.Reservar, mensajeId, proveedoraSedeId, Encoding.UTF8.GetString(salida.ToArray()));
    }

    public Sobre CrearConfirmar(string mensajeId, string proveedoraSedeId, string pedidoId) =>
        CrearDecision(TiposComandoStock.Confirmar, mensajeId, proveedoraSedeId, pedidoId);

    public Sobre CrearLiberar(string mensajeId, string proveedoraSedeId, string pedidoId) =>
        CrearDecision(TiposComandoStock.Liberar, mensajeId, proveedoraSedeId, pedidoId);

    /// Publica el comando en la cola de inventario de su destino. Lanza <see cref="EnvioFallidoException"/> si el
    /// broker no lo confirma o no hay cola; el llamador reenvía el mismo sobre.
    public Task EnviarAsync(Sobre comando, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(comando, nameof(comando));
        if (!EsComandoDeStock(comando.Tipo))
        {
            throw new ArgumentException($"'{comando.Tipo}' no es un comando de stock.", nameof(comando));
        }
        if (!string.Equals(comando.OrigenSedeId, _sedeId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"El comando es de la sede '{comando.OrigenSedeId}', no de '{_sedeId}'.", nameof(comando));
        }
        return _publicador.PublicarAsync(TopologiaConvitec.ColaInventario(comando.DestinoSedeId), comando, cancelacion);
    }

    /// Lee un <c>stock.resultado</c> recibido de la cola de pedidos. Devuelve false, con el motivo, si el sobre
    /// no es una respuesta válida; el llamador la descarta o la registra.
    public static bool TryLeerResultado(Sobre sobre, out ResultadoStock? resultado, out string motivo)
    {
        ArgumentNullException.ThrowIfNull(sobre, nameof(sobre));
        if (!string.Equals(sobre.Tipo, TiposComandoStock.Resultado, StringComparison.Ordinal))
        {
            resultado = null;
            motivo = $"El tipo '{sobre.Tipo}' no es stock.resultado.";
            return false;
        }
        return CuerpoResultado.TryLeer(sobre.Cuerpo, out resultado, out motivo);
    }

    private Sobre CrearDecision(string tipo, string mensajeId, string proveedoraSedeId, string pedidoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pedidoId, nameof(pedidoId));

        using MemoryStream salida = new MemoryStream();
        using (Utf8JsonWriter escritor = new Utf8JsonWriter(salida))
        {
            escritor.WriteStartObject();
            escritor.WriteString("pedidoId", pedidoId);
            escritor.WriteEndObject();
        }
        return Crear(tipo, mensajeId, proveedoraSedeId, Encoding.UTF8.GetString(salida.ToArray()));
    }

    private Sobre Crear(string tipo, string mensajeId, string proveedoraSedeId, string cuerpoJson) =>
        Sobre.ConCuerpoTexto(tipo, mensajeId, mensajeId, _sedeId, proveedoraSedeId, cuerpoJson);

    private static bool EsComandoDeStock(string tipo) =>
        string.Equals(tipo, TiposComandoStock.Reservar, StringComparison.Ordinal)
        || string.Equals(tipo, TiposComandoStock.Confirmar, StringComparison.Ordinal)
        || string.Equals(tipo, TiposComandoStock.Liberar, StringComparison.Ordinal);
}
