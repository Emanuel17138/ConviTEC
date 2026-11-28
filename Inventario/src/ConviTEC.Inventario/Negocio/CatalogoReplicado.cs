using System;
using System.Diagnostics.CodeAnalysis;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Negocio;

/// Copia local de las ofertas de las otras sedes (RF03) y único escritor de ella. Cada oferta es el valor absoluto
/// más reciente por pareja sede–producto: solo se aplica una entrada si la pareja es nueva o su revisión supera la
/// almacenada, tanto si llega como cambio como dentro de una instantánea, así un mensaje tardío o duplicado no
/// deshace nada. Igual que <see cref="InventarioLocal"/>, cada cambio prepara el contenido nuevo, guarda el archivo
/// completo de forma atómica y solo después lo instala en memoria. Solo lo crea <see cref="CargadorCatalogo"/>.
public sealed class CatalogoReplicado
{
    private readonly object _cerrojo = new object();
    private readonly EstadoCatalogo _estado;
    private readonly RepositorioCatalogo _repositorio;
    private readonly string[] _sedesRemotas;

    public string SedeId { get; }

    internal CatalogoReplicado(string sedeId, EstadoCatalogo estado, RepositorioCatalogo repositorio, string[] sedesRemotas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(estado, nameof(estado));
        ArgumentNullException.ThrowIfNull(repositorio, nameof(repositorio));
        SedeId = sedeId;
        _estado = estado;
        _repositorio = repositorio;
        _sedesRemotas = SedesRemotas.Validar(sedeId, sedesRemotas);
    }

    /// Verdadero si <paramref name="sedeId"/> es una de las otras sedes configuradas (comparación ordinal).
    public bool EsSedeRemota(string sedeId)
    {
        ArgumentNullException.ThrowIfNull(sedeId, nameof(sedeId));
        for (int i = 0; i < _sedesRemotas.Length; i++)
        {
            if (string.Equals(_sedesRemotas[i], sedeId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// Verdadero cuando ya llegó la primera instantánea de cada otra sede configurada. Sin sedes remotas lo está
    /// desde el inicio. Una copia inicializada se conserva al reiniciar.
    public bool EstaInicializado
    {
        get
        {
            lock (_cerrojo)
            {
                return PendientesSinCerrojo().Length == 0;
            }
        }
    }

    /// Sedes remotas cuya primera instantánea falta, en el orden de la configuración.
    public string[] SedesPendientes()
    {
        lock (_cerrojo)
        {
            return PendientesSinCerrojo();
        }
    }

    public bool EstaSincronizada(string sedeId)
    {
        ArgumentNullException.ThrowIfNull(sedeId, nameof(sedeId));
        lock (_cerrojo)
        {
            return _estado.Sincronizadas.Contiene(sedeId);
        }
    }

    public bool BuscarOferta(string sedeId, string productoId, [MaybeNullWhen(false)] out OfertaReplicada oferta)
    {
        ArgumentNullException.ThrowIfNull(sedeId, nameof(sedeId));
        ArgumentNullException.ThrowIfNull(productoId, nameof(productoId));
        lock (_cerrojo)
        {
            return _estado.Ofertas.Buscar(OfertaReplicada.ClaveDe(productoId, sedeId), out oferta);
        }
    }

    /// Todas las ofertas de las sedes remotas configuradas, por productoId ascendente y luego sedeId. Incluye
    /// ceros. Arreglo vacío si no hay ninguna.
    public OfertaReplicada[] ConsultarOfertas()
    {
        lock (_cerrojo)
        {
            OfertaReplicada[] todas = _estado.Ofertas.ValoresEnOrden();
            int vigentes = 0;
            for (int i = 0; i < todas.Length; i++)
            {
                if (EsSedeRemota(todas[i].SedeId))
                {
                    vigentes++;
                }
            }

            OfertaReplicada[] resultado = new OfertaReplicada[vigentes];
            int posicion = 0;
            for (int i = 0; i < todas.Length; i++)
            {
                if (EsSedeRemota(todas[i].SedeId))
                {
                    resultado[posicion++] = todas[i];
                }
            }
            return resultado;
        }
    }

    /// Aplica un <c>catalogo.actualizado</c> de <paramref name="origenSedeId"/>. Devuelve true si cambió la copia
    /// (pareja nueva o revisión mayor) y false si la entrada era vieja o repetida, caso en el que no se escribe.
    /// Un cambio aislado nunca completa la primera sincronización de su sede. Vuelve ya hecho durable.
    public bool AplicarCambio(string origenSedeId, EntradaCatalogo entrada)
    {
        ValidarOrigen(origenSedeId);
        ArgumentNullException.ThrowIfNull(entrada, nameof(entrada));

        lock (_cerrojo)
        {
            ArbolAvl<string, OfertaReplicada> nuevas = EstadoInventario.NuevoIndice<OfertaReplicada>();
            if (!Supera(origenSedeId, entrada))
            {
                return false;
            }
            Anotar(nuevas, origenSedeId, entrada);

            Guardar(nuevas, _estado.Sincronizadas.ClavesEnOrden());
            FusionIndices.Instalar(_estado.Ofertas, nuevas);
            return true;
        }
    }

    /// Aplica un <c>catalogo.instantanea</c>: cada entrada con la misma regla que un cambio, sin borrar las ofertas
    /// que la instantánea no menciona, y marca la primera sincronización de la sede aunque venga vacía. Todo se
    /// valida antes de tocar nada: un producto repetido dentro de la instantánea la rechaza entera. Devuelve cuántas
    /// entradas modificaron la copia.
    public int AplicarInstantanea(string origenSedeId, EntradaCatalogo[] entradas)
    {
        ValidarOrigen(origenSedeId);
        ArgumentNullException.ThrowIfNull(entradas, nameof(entradas));
        ValidarEntradas(entradas);

        lock (_cerrojo)
        {
            ArbolAvl<string, OfertaReplicada> nuevas = EstadoInventario.NuevoIndice<OfertaReplicada>();
            for (int i = 0; i < entradas.Length; i++)
            {
                if (Supera(origenSedeId, entradas[i]))
                {
                    Anotar(nuevas, origenSedeId, entradas[i]);
                }
            }

            bool primeraVez = !_estado.Sincronizadas.Contiene(origenSedeId);
            if (nuevas.EstaVacio && !primeraVez)
            {
                return 0;
            }

            string[] sincronizadas = primeraVez
                ? ConMarca(origenSedeId)
                : _estado.Sincronizadas.ClavesEnOrden();
            Guardar(nuevas, sincronizadas);
            FusionIndices.Instalar(_estado.Ofertas, nuevas);
            if (primeraVez)
            {
                _estado.Sincronizadas.Insertar(origenSedeId, origenSedeId);
            }
            return nuevas.Cantidad;
        }
    }

    /// Verdadero si la pareja sede–producto es nueva o la revisión de la entrada supera la almacenada.
    private bool Supera(string sedeId, EntradaCatalogo entrada)
    {
        string clave = OfertaReplicada.ClaveDe(entrada.Producto.ProductoId, sedeId);
        return !_estado.Ofertas.Buscar(clave, out OfertaReplicada? actual) || entrada.Revision > actual.Revision;
    }

    private static void Anotar(ArbolAvl<string, OfertaReplicada> nuevas, string sedeId, EntradaCatalogo entrada)
    {
        OfertaReplicada oferta = new OfertaReplicada(sedeId, entrada.Producto, entrada.Disponible, entrada.Revision);
        nuevas.Insertar(oferta.Clave, oferta);
    }

    /// Guarda el archivo completo: lo vivo con las sustituciones de <paramref name="nuevas"/> y las sedes marcadas.
    private void Guardar(ArbolAvl<string, OfertaReplicada> nuevas, string[] sincronizadas)
    {
        _repositorio.GuardarCatalogo(ConversionEstado.ConstruirCatalogo(
            SedeId, sincronizadas, FusionIndices.Fusionar(_estado.Ofertas, nuevas)));
    }

    /// Sedes ya marcadas más <paramref name="sedeId"/>, en orden ordinal.
    private string[] ConMarca(string sedeId)
    {
        ArbolAvl<string, string> todas = EstadoInventario.NuevoIndice<string>();
        string[] actuales = _estado.Sincronizadas.ClavesEnOrden();
        for (int i = 0; i < actuales.Length; i++)
        {
            todas.Insertar(actuales[i], actuales[i]);
        }
        todas.Insertar(sedeId, sedeId);
        return todas.ClavesEnOrden();
    }

    private string[] PendientesSinCerrojo()
    {
        int faltan = 0;
        for (int i = 0; i < _sedesRemotas.Length; i++)
        {
            if (!_estado.Sincronizadas.Contiene(_sedesRemotas[i]))
            {
                faltan++;
            }
        }

        string[] pendientes = new string[faltan];
        int posicion = 0;
        for (int i = 0; i < _sedesRemotas.Length; i++)
        {
            if (!_estado.Sincronizadas.Contiene(_sedesRemotas[i]))
            {
                pendientes[posicion++] = _sedesRemotas[i];
            }
        }
        return pendientes;
    }

    private void ValidarOrigen(string origenSedeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origenSedeId, nameof(origenSedeId));
        if (!EsSedeRemota(origenSedeId))
        {
            throw new ArgumentException(
                $"La sede '{origenSedeId}' no es una sede remota de '{SedeId}': no se acepta su catálogo.", nameof(origenSedeId));
        }
    }

    /// Rechaza entradas nulas y productos repetidos dentro de una misma instantánea.
    private static void ValidarEntradas(EntradaCatalogo[] entradas)
    {
        ArbolAvl<string, string> vistos = EstadoInventario.NuevoIndice<string>();
        for (int i = 0; i < entradas.Length; i++)
        {
            if (entradas[i] == null)
            {
                throw new ArgumentException($"La entrada {i} de la instantánea es nula.", nameof(entradas));
            }
            string productoId = entradas[i].Producto.ProductoId;
            if (!vistos.Insertar(productoId, productoId))
            {
                throw new ArgumentException($"El producto '{productoId}' está repetido en la instantánea.", nameof(entradas));
            }
        }
    }
}
