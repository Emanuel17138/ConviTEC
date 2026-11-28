using System;
using System.IO;
using System.Text.Json;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Mensajeria;

/// Atiende <c>stock.reservar</c>, <c>stock.confirmar</c> y <c>stock.liberar</c>: saca el comando del sobre, lo
/// aplica con <see cref="ProveedorStock"/> (que lo deja durable) y arma el <c>stock.resultado</c> para la cola de
/// pedidos de la coordinadora. La respuesta solo existe después de persistir. Si el guardado falla no hay respuesta
/// y el mensaje se devuelve a la cola: el mismo comando se procesará de nuevo como nuevo.
public sealed class ManejadorComandosStock : IManejadorMensaje
{
    private static readonly string[] TiposAtendidos =
    {
        TiposComandoStock.Reservar,
        TiposComandoStock.Confirmar,
        TiposComandoStock.Liberar,
    };

    private readonly string _sedeId;
    private readonly ProveedorStock _proveedor;

    public ManejadorComandosStock(string sedeId, ProveedorStock proveedor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(proveedor, nameof(proveedor));
        _sedeId = sedeId;
        _proveedor = proveedor;
    }

    public string[] Tipos => (string[])TiposAtendidos.Clone();

    public ResultadoDespacho Manejar(Sobre sobre)
    {
        ArgumentNullException.ThrowIfNull(sobre, nameof(sobre));

        ResultadoStock resultado;
        try
        {
            resultado = Aplicar(sobre);
        }
        catch (Exception e) when (e is DatosInventarioInvalidosException || e is IOException || e is UnauthorizedAccessException)
        {
            // El guardado atómico reporta los fallos de disco como DatosInventarioInvalidosException. Nada cambió
            // en memoria ni en disco, así que el comando se reprocesa entero cuando el disco vuelva.
            return ResultadoDespacho.Reintentar($"No se pudo guardar el efecto de {sobre.MensajeId}: {e.Message}");
        }

        return ResultadoDespacho.Acusar(new SobreSaliente(
            TopologiaConvitec.ColaPedidos(sobre.OrigenSedeId), ArmarRespuesta(sobre, resultado)));
    }

    private ResultadoStock Aplicar(Sobre sobre)
    {
        string? pedidoId = LeerTexto(sobre.Cuerpo, "pedidoId");
        switch (sobre.Tipo)
        {
            case TiposComandoStock.Reservar:
                return _proveedor.Reservar(
                    new SolicitudReserva(sobre.MensajeId, sobre.OrigenSedeId, pedidoId, LeerLineas(sobre.Cuerpo)));
            case TiposComandoStock.Confirmar:
                return _proveedor.Confirmar(new SolicitudDecision(sobre.MensajeId, sobre.OrigenSedeId, pedidoId));
            case TiposComandoStock.Liberar:
                return _proveedor.Liberar(new SolicitudDecision(sobre.MensajeId, sobre.OrigenSedeId, pedidoId));
            default:
                throw new InvalidOperationException($"Tipo '{sobre.Tipo}' no es un comando de stock.");
        }
    }

    private Sobre ArmarRespuesta(Sobre comando, ResultadoStock resultado)
    {
        string cuerpo = CuerpoResultado.Escribir(resultado);
        return Sobre.ConCuerpoTexto(
            TiposComandoStock.Resultado,
            IdentificadorDerivado.DeComando(comando.MensajeId),
            comando.MensajeId,
            _sedeId,
            comando.OrigenSedeId,
            cuerpo);
    }

    // Lectura tolerante: un campo ausente o de otro tipo se vuelve null y ProveedorStock responde DATOS_INVALIDOS.

    private static string? LeerTexto(JsonElement cuerpo, string campo)
    {
        if (cuerpo.ValueKind == JsonValueKind.Object
            && cuerpo.TryGetProperty(campo, out JsonElement valor)
            && valor.ValueKind == JsonValueKind.String)
        {
            return valor.GetString();
        }
        return null;
    }

    private static LineaSolicitada?[]? LeerLineas(JsonElement cuerpo)
    {
        if (cuerpo.ValueKind != JsonValueKind.Object
            || !cuerpo.TryGetProperty("lineas", out JsonElement lineas)
            || lineas.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        LineaSolicitada?[] resultado = new LineaSolicitada?[lineas.GetArrayLength()];
        int posicion = 0;
        foreach (JsonElement elemento in lineas.EnumerateArray())
        {
            resultado[posicion++] = elemento.ValueKind == JsonValueKind.Null ? null : LeerLinea(elemento);
        }
        return resultado;
    }

    private static LineaSolicitada LeerLinea(JsonElement elemento)
    {
        if (elemento.ValueKind != JsonValueKind.Object)
        {
            return new LineaSolicitada(null, null, null);
        }

        int? cantidad = null;
        if (elemento.TryGetProperty("cantidad", out JsonElement c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out int entera))
        {
            cantidad = entera;
        }

        long? precio = null;
        if (elemento.TryGetProperty("precioUnitarioCentimos", out JsonElement p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out long centimos))
        {
            precio = centimos;
        }

        return new LineaSolicitada(LeerTexto(elemento, "productoId"), cantidad, precio);
    }
}
