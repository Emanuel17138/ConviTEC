# Archivos del inventario local (RF01, RNF15, RNF16)

Cada sede tiene su propio directorio y nunca lee archivos de otra (RNF14). Los archivos son JSON UTF-8 con claves camelCase. Se leen con `System.Text.Json` sobre DTO que solo usan arreglos nativos (`T[]`), sin colecciones (RNF06).

| Archivo | Contenido | Cuándo se lee |
|---|---|---|
| `maestro-productos.json` | `{productos:[{productoId,nombre,descripcion,categoria,precioUnitarioCentimos}]}` | Solo en el arranque inicial |
| `existencias-iniciales.json` | `{sedeId,existencias:[{productoId,existencia}]}`: productos que maneja la sede | Solo en el arranque inicial |
| `estado-inventario.json` | `{version,sedeId,stock:[{producto:{...},existencia,reservada}]}` | Siempre que exista (recuperación) |

Ejemplos en `datos/ejemplo/`.

## Arranque inicial y recuperación

`CargadorInventario.Cargar(rutas)` decide por la presencia de `estado-inventario.json`:

1. **No existe: arranque inicial.** Lee el maestro y construye un AVL de `Producto`. Lee las existencias iniciales, comprueba que cada código esté en el maestro y construye el AVL de `StockLocal` con `reservada = 0`. Guarda de inmediato el primer snapshot, así el siguiente arranque es una recuperación.
2. **Existe: recuperación.** Lee solo el snapshot y reconstruye el AVL insertando entrada por entrada (RNF16). El maestro y las semillas se ignoran: una semilla de 8 no sustituye un estado guardado de 4/1.

Un snapshot ilegible o inválido **no** hace volver a la semilla en silencio: el arranque falla con un mensaje que indica archivo y posición.

## Validación antes de instalar el estado

Todo se construye en estructuras locales y `InventarioLocal` se crea solo al final. Si algo falla se lanza `DatosInventarioInvalidosException` y no queda estado parcial. Se rechazan:

- código de producto duplicado en el maestro, en las existencias o en el snapshot (el `Insertar` del AVL devuelve `false`);
- `reservada` negativa o mayor que `existencia`, `existencia` negativa, precio negativo (lo imponen los constructores de `Producto` y `StockLocal`);
- producto de las existencias ausente del maestro (la comparación distingue mayúsculas: `caf` ≠ `CAF`);
- `sedeId` del archivo distinto al de la instancia, versión de snapshot desconocida;
- campos faltantes, elementos `null`, números no enteros, JSON mal formado, archivos inexistentes.

## Escritura atómica

`RepositorioInventario.GuardarEstado` escribe `estado-inventario.json.tmp`, fuerza el contenido a disco (`Flush(true)`) y lo renombra sobre el definitivo con `File.Move(..., overwrite: true)`. Un corte a mitad de escritura deja intacto el snapshot anterior.

## Consulta local reutilizable

`InventarioLocal` ofrece `ConsultarInventario()` (todo el stock por `productoId` ascendente, agotados incluidos, `[]` si no hay productos), `BuscarStock(productoId, out stock)`, `Contiene` y `Cantidad`. RF02 reutiliza esta misma instancia; el endpoint HTTP de `rf01-consulta` la proyecta a JSON.
