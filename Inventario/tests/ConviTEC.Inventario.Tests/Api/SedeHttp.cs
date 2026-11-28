using System;
using System.Net.Http;
using System.Threading.Tasks;
using ConviTEC.Inventario.Api;
using ConviTEC.Inventario.Negocio;
using ConviTEC.Inventario.Persistencia;
using ConviTEC.Inventario.Tests.Persistencia;
using Microsoft.AspNetCore.Builder;

namespace ConviTEC.Inventario.Tests.Api;

/// Levanta el backend real de una sede (Kestrel en un puerto libre de localhost) y lo consulta con HttpClient.
/// Los datos se cargan con el CargadorInventario real desde un directorio temporal propio de la sede.
public sealed class SedeHttp : IAsyncDisposable
{
    public const string Maestro = """
        {"productos":[
          {"productoId":"SAL","nombre":"Sal","descripcion":"Sal marina 1 kg","categoria":"Despensa","precioUnitarioCentimos":50000},
          {"productoId":"CAF","nombre":"Café","descripcion":"Café molido 500 g","categoria":"Bebidas","precioUnitarioCentimos":150000},
          {"productoId":"ARR","nombre":"Arroz","descripcion":"Arroz 2 kg","categoria":"Despensa","precioUnitarioCentimos":90000}
        ]}
        """;

    private readonly DirectorioTemporal _directorio;
    private readonly WebApplication _app;
    private readonly HttpClient _cliente;
    private readonly bool _poseeDirectorio;

    public ResultadoArranque Arranque { get; }
    public HttpClient Cliente => _cliente;

    private SedeHttp(DirectorioTemporal directorio, ResultadoArranque arranque, WebApplication app, HttpClient cliente, bool poseeDirectorio)
    {
        _directorio = directorio;
        Arranque = arranque;
        _app = app;
        _cliente = cliente;
        _poseeDirectorio = poseeDirectorio;
    }

    /// Arranca una sede nueva (arranque inicial) con el maestro común y las existencias dadas.
    public static Task<SedeHttp> ArrancarInicialAsync(string sedeId, string existenciasJson)
    {
        DirectorioTemporal directorio = new DirectorioTemporal();
        directorio.Escribir(RutasArchivosInventario.NombreMaestro, Maestro);
        directorio.Escribir(RutasArchivosInventario.NombreExistenciasIniciales,
            "{\"sedeId\":\"" + sedeId + "\",\"existencias\":" + existenciasJson + "}");
        return ArrancarAsync(directorio, sedeId);
    }

    /// Arranca sobre un directorio que ya contiene archivos (p. ej. un estado guardado: recuperación).
    /// Con <paramref name="poseeDirectorio"/> en false, al cerrar la sede el directorio se conserva
    /// para simular un reinicio con otra instancia sobre los mismos archivos.
    public static async Task<SedeHttp> ArrancarAsync(DirectorioTemporal directorio, string sedeId, bool poseeDirectorio = true)
    {
        try
        {
            ResultadoArranque arranque = new CargadorInventario().Cargar(directorio.Rutas(sedeId));
            WebApplication app = ServidorSede.Crear(arranque.Inventario, "http://127.0.0.1:0");
            await app.StartAsync();

            string direccion = PrimeraDireccion(app);
            HttpClient cliente = new HttpClient { BaseAddress = new Uri(direccion) };
            return new SedeHttp(directorio, arranque, app, cliente, poseeDirectorio);
        }
        catch
        {
            if (poseeDirectorio)
            {
                directorio.Dispose();
            }
            throw;
        }
    }

    private static string PrimeraDireccion(WebApplication app)
    {
        foreach (string direccion in app.Urls)
        {
            return direccion;
        }
        throw new InvalidOperationException("El servidor no publicó ninguna dirección.");
    }

    public async ValueTask DisposeAsync()
    {
        _cliente.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        if (_poseeDirectorio)
        {
            _directorio.Dispose();
        }
    }
}
