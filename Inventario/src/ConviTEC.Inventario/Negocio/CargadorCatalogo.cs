using System;
using ConviTEC.Inventario.Dominio;
using ConviTEC.Inventario.Estructuras;
using ConviTEC.Inventario.Persistencia;

namespace ConviTEC.Inventario.Negocio;

/// Reconstruye la réplica del catálogo desde <c>catalogo-replicado.json</c>, entrada por entrada (RNF16). Sin
/// archivo (primer arranque) devuelve una réplica vacía y no inicializada, sin escribir nada. Un archivo ilegible
/// o inválido no se ignora en silencio: el arranque falla con archivo y posición, sin instalar un estado parcial.
public sealed class CargadorCatalogo
{
    /// <paramref name="sedesRemotas"/>: las otras sedes, de las que se espera una primera instantánea.
    public CatalogoReplicado Cargar(RutasArchivosInventario rutas, string[] sedesRemotas)
    {
        ArgumentNullException.ThrowIfNull(rutas, nameof(rutas));
        ArgumentNullException.ThrowIfNull(sedesRemotas, nameof(sedesRemotas));

        return Cargar(rutas, sedesRemotas, new RepositorioCatalogo(rutas));
    }

    /// Permite a las pruebas inyectar un repositorio con puntos de corte.
    internal CatalogoReplicado Cargar(RutasArchivosInventario rutas, string[] sedesRemotas, RepositorioCatalogo repositorio)
    {
        SedesRemotas.Validar(rutas.SedeId, sedesRemotas);

        EstadoCatalogo estado = new EstadoCatalogo();
        if (repositorio.ExisteCatalogoGuardado())
        {
            Reconstruir(repositorio.LeerCatalogo(), estado, rutas.SedeId, rutas.CatalogoReplicado);
        }
        return new CatalogoReplicado(rutas.SedeId, estado, repositorio, sedesRemotas);
    }

    private static void Reconstruir(CatalogoReplicadoDto dto, EstadoCatalogo estado, string sedeId, string archivo)
    {
        if (dto.Version != CatalogoReplicadoDto.VersionActual)
        {
            throw new DatosInventarioInvalidosException(archivo,
                $"versión de catálogo no soportada: {dto.Version?.ToString() ?? "(falta)"}.");
        }
        if (!string.Equals(dto.SedeId, sedeId, StringComparison.Ordinal))
        {
            throw new DatosInventarioInvalidosException(archivo,
                $"el archivo es de la sede '{dto.SedeId ?? "(falta)"}' y esta instancia es '{sedeId}'.");
        }
        if (dto.SedesSincronizadas == null)
        {
            throw new DatosInventarioInvalidosException(archivo, "falta el campo 'sedesSincronizadas'.");
        }
        if (dto.Ofertas == null)
        {
            throw new DatosInventarioInvalidosException(archivo, "falta el campo 'ofertas'.");
        }

        CargarSedesSincronizadas(dto.SedesSincronizadas, estado, sedeId, archivo);
        CargarOfertas(dto.Ofertas, estado, sedeId, archivo);
    }

    private static void CargarSedesSincronizadas(string?[] sedes, EstadoCatalogo estado, string sedeId, string archivo)
    {
        for (int i = 0; i < sedes.Length; i++)
        {
            string posicion = $"sedesSincronizadas[{i}]";
            string sede = CamposObligatorios.Texto(sedes[i], "sedeId", archivo, posicion);
            ExigirSedeAjena(sede, sedeId, archivo, posicion);
            if (!estado.Sincronizadas.Insertar(sede, sede))
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion}: sede duplicada '{sede}'.");
            }
        }
    }

    private static void CargarOfertas(OfertaReplicadaGuardadaDto?[] ofertas, EstadoCatalogo estado, string sedeId, string archivo)
    {
        for (int i = 0; i < ofertas.Length; i++)
        {
            string posicion = $"ofertas[{i}]";
            OfertaReplicadaGuardadaDto dto = ofertas[i] ?? throw new DatosInventarioInvalidosException(archivo, $"{posicion} es nulo.");

            string sede = CamposObligatorios.Texto(dto.SedeId, "sedeId", archivo, posicion);
            ExigirSedeAjena(sede, sedeId, archivo, posicion);
            Producto producto = ConversionProducto.Convertir(dto.Producto, archivo, posicion + ".producto");
            int disponible = CamposObligatorios.Valor(dto.Disponible, "disponible", archivo, posicion);
            long revision = CamposObligatorios.Valor(dto.Revision, "revision", archivo, posicion);

            OfertaReplicada oferta;
            try
            {
                oferta = new OfertaReplicada(sede, producto, disponible, revision);
            }
            catch (ArgumentException e)
            {
                throw new DatosInventarioInvalidosException(archivo, $"{posicion}: {e.Message}", e);
            }
            if (!estado.Ofertas.Insertar(oferta.Clave, oferta))
            {
                throw new DatosInventarioInvalidosException(archivo,
                    $"{posicion}: oferta duplicada para el producto '{producto.ProductoId}' de la sede '{sede}'.");
            }
        }
    }

    /// La sede no replica su propio catálogo: sus cantidades son las del inventario local.
    private static void ExigirSedeAjena(string sede, string sedePropia, string archivo, string posicion)
    {
        if (string.Equals(sede, sedePropia, StringComparison.Ordinal))
        {
            throw new DatosInventarioInvalidosException(archivo, $"{posicion}: la sede no replica su propio catálogo ('{sede}').");
        }
    }
}
