# Consulta HTTP del inventario local (RF01 · rf01-consulta)

El proyecto `src/ConviTEC.Inventario.Api` es el adaptador HTTP de la sede. No tiene reglas de negocio: carga el estado con `CargadorInventario`, lo entrega a `RutasInventario` y proyecta el resultado a JSON. La lógica sigue en `ConviTEC.Inventario`, que no conoce HTTP.

## Endpoint

`GET /api/v1/inventario` → 200 con un arreglo de `StockLocal` de la sede, por `productoId` ascendente, agotados incluidos. Una sede sin productos devuelve 200 y `[]`.

```json
[
  {
    "producto": {"productoId":"CAF","nombre":"Café","descripcion":"Café molido 500 g","categoria":"Bebidas","precioUnitarioCentimos":150000},
    "existencia": 5,
    "reservada": 2,
    "disponible": 3
  }
]
```

- La proyección vive en `Http/StockLocalRespuesta.cs`: fija los campos exactos del contrato y desacopla el JSON del dominio. `disponible` sale de `StockLocal.Disponible` (existencia − reservada).
- El precio es un entero en céntimos de colón (número JSON). Claves en camelCase.
- La sede la determina la instancia: no hay parámetro de sede. Cada consulta lee el estado vivo de `InventarioLocal`, así que RF02 verá reflejados sus movimientos sin cambiar el endpoint.
- Un fallo inesperado responde 500 con `{codigo:"ERROR_INTERNO",mensaje}`. No se filtran detalles internos (el detalle va al log).

Las reposiciones (`POST /api/v1/inventario/{productoId}/reposiciones`) están en [reposicion-http.md](reposicion-http.md).

## Arranque de la sede

```powershell
dotnet run --project src/ConviTEC.Inventario.Api -- --sede LIB --datos <directorio-de-la-sede> --url http://localhost:5101
```

| Opción | Variable de entorno | Significado |
|---|---|---|
| `--sede` | `CONVITEC_SEDE` | `sedeId` de la instancia (sensible a mayúsculas). Obligatoria. |
| `--datos` | `CONVITEC_DATOS` | Directorio propio de la sede con sus archivos JSON. Obligatorio. |
| `--url` | `CONVITEC_URL` | Dirección de escucha. Por defecto `http://localhost:5000`. |

Orden del arranque: leer configuración → `CargadorInventario.Cargar` (inicial o recuperación, con validación completa) → registrar el endpoint → abrir el puerto. Si la configuración falta (código de salida 2) o los archivos son inválidos (código 1), el proceso termina con un mensaje y **no abre el puerto**: nunca se sirve un estado parcial.

Cada sede usa su propio directorio y su propio puerto; dos instancias no comparten archivos (RNF12, RNF14).

## Pruebas

`tests/ConviTEC.Inventario.Tests/Api` levanta el backend real en un puerto libre de `127.0.0.1` (Kestrel) y consulta con `HttpClient`, sin servidor simulado. `SedeHttp` carga los datos con el `CargadorInventario` real desde un directorio temporal. Cubre:

- contrato exacto: CAF 5/2 con precio 150000 muestra 3 disponibles, SAL con existencia 0 aparece con 0, solo los campos del contrato;
- orden ascendente y sede sin productos (200 y `[]`);
- reinicio: estado guardado 4/1 con semilla de 8 devuelve 4, 1 y 3;
- dos instancias con cantidades distintas devuelven cada una las suyas;
- ruta inexistente (404) y método no permitido (405) sin tocar el inventario;
- lectura de `--sede`, `--datos` y `--url`, y fallo claro si faltan.
