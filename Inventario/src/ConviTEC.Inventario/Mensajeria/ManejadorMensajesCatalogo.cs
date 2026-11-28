using System;
using System.IO;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Mensajeria;

/// Atiende <c>catalogo.actualizado</c>, <c>catalogo.instantanea</c> y <c>catalogo.solicitar</c>.
/// <list type="bullet">
/// <item>Cambio e instantánea: se aplican a la réplica (<see cref="CatalogoReplicado"/>), que decide por revisión y
/// deja el resultado durable antes de que el consumidor acuse. No hay respuesta.</item>
/// <item>Solicitud: responde a la sede que pregunta con todas las ofertas propias, ceros incluidos, en una
/// <c>catalogo.instantanea</c> hacia su cola de inventario.</item>
/// </list>
/// Un mensaje ilegible, de una sede que no es remota o con el cuerpo inválido se descarta con su motivo: no hay nada que
/// reintentar. Si el guardado falla se devuelve a la cola y se reprocesa entero.
public sealed class ManejadorMensajesCatalogo : IManejadorMensaje
{
    private static readonly string[] TiposAtendidos =
    {
        TiposMensajeCatalogo.Actualizado,
        TiposMensajeCatalogo.Solicitar,
        TiposMensajeCatalogo.Instantanea,
    };

    private readonly string _sedeId;
    private readonly InventarioLocal _inventario;
    private readonly CatalogoReplicado _catalogo;

    public ManejadorMensajesCatalogo(string sedeId, InventarioLocal inventario, CatalogoReplicado catalogo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(inventario, nameof(inventario));
        ArgumentNullException.ThrowIfNull(catalogo, nameof(catalogo));
        if (!string.Equals(inventario.SedeId, sedeId, StringComparison.Ordinal)
            || !string.Equals(catalogo.SedeId, sedeId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"El inventario ('{inventario.SedeId}') y la réplica ('{catalogo.SedeId}') deben ser de la sede '{sedeId}'.");
        }

        _sedeId = sedeId;
        _inventario = inventario;
        _catalogo = catalogo;
    }

    public string[] Tipos => (string[])TiposAtendidos.Clone();

    public ResultadoDespacho Manejar(Sobre sobre)
    {
        ArgumentNullException.ThrowIfNull(sobre, nameof(sobre));

        if (!_catalogo.EsSedeRemota(sobre.OrigenSedeId))
        {
            return ResultadoDespacho.Descartar(
                $"{sobre.Tipo} {sobre.MensajeId}: '{sobre.OrigenSedeId}' no es una sede remota de '{_sedeId}'.");
        }

        try
        {
            switch (sobre.Tipo)
            {
                case TiposMensajeCatalogo.Actualizado:
                    return AplicarCambio(sobre);
                case TiposMensajeCatalogo.Instantanea:
                    return AplicarInstantanea(sobre);
                case TiposMensajeCatalogo.Solicitar:
                    return ResponderSolicitud(sobre);
                default:
                    return ResultadoDespacho.Descartar($"Tipo '{sobre.Tipo}' no es de catálogo (mensaje {sobre.MensajeId}).");
            }
        }
        catch (Exception e) when (e is DatosInventarioInvalidosException || e is IOException || e is UnauthorizedAccessException)
        {
            // Nada cambió en memoria ni en disco: el mensaje se reprocesa entero cuando el disco vuelva.
            return ResultadoDespacho.Reintentar($"No se pudo guardar {sobre.Tipo} {sobre.MensajeId}: {e.Message}");
        }
    }

    private ResultadoDespacho AplicarCambio(Sobre sobre)
    {
        if (!CuerpoCatalogo.TryLeerCambio(sobre.Cuerpo, out EntradaCatalogo? entrada, out string motivo))
        {
            return ResultadoDespacho.Descartar($"catalogo.actualizado {sobre.MensajeId} inválido: {motivo}");
        }

        _catalogo.AplicarCambio(sobre.OrigenSedeId, entrada!);
        return ResultadoDespacho.Acusar();
    }

    private ResultadoDespacho AplicarInstantanea(Sobre sobre)
    {
        if (!CuerpoCatalogo.TryLeerInstantanea(sobre.Cuerpo, out EntradaCatalogo[]? entradas, out string motivo))
        {
            return ResultadoDespacho.Descartar($"catalogo.instantanea {sobre.MensajeId} inválida: {motivo}");
        }

        try
        {
            _catalogo.AplicarInstantanea(sobre.OrigenSedeId, entradas!);
        }
        catch (ArgumentException e)
        {
            // Un producto repetido dentro de la instantánea la invalida entera: no se aplica nada.
            return ResultadoDespacho.Descartar($"catalogo.instantanea {sobre.MensajeId} inválida: {e.Message}");
        }
        return ResultadoDespacho.Acusar();
    }

    private ResultadoDespacho ResponderSolicitud(Sobre sobre)
    {
        EntradaStock[] propias = _inventario.ConsultarEntradas();
        EntradaCatalogo[] entradas = new EntradaCatalogo[propias.Length];
        for (int i = 0; i < propias.Length; i++)
        {
            entradas[i] = new EntradaCatalogo(propias[i].Stock.Producto, propias[i].Stock.Disponible, propias[i].Revision);
        }

        Sobre respuesta = Sobre.ConCuerpoTexto(
            TiposMensajeCatalogo.Instantanea,
            IdentificadorDerivado.DeSolicitudCatalogo(sobre.MensajeId),
            sobre.CorrelacionId,
            _sedeId,
            sobre.OrigenSedeId,
            CuerpoCatalogo.EscribirInstantanea(entradas));
        return ResultadoDespacho.Acusar(new SobreSaliente(TopologiaConvitec.ColaInventario(sobre.OrigenSedeId), respuesta));
    }
}
