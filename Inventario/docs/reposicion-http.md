# Reposición de stock por HTTP (RF02 · rf02-reposicion)

Lo consume la pantalla de la sede (Integrante 4). La regla vive en `ServicioReposiciones` (negocio); `RutasInventario` solo traduce HTTP ↔ resultado.

## Endpoint

`POST /api/v1/inventario/{productoId}/reposiciones` con `{"operacionId":"<UUID>","cantidad":<entero>}`.

| Resultado | HTTP | Cuerpo |
|---|---|---|
| Reposición aplicada, o repetida con la misma clave y contenido | 200 | `StockLocal` resultante de **esa** operación (mismo formato que `GET /inventario`) |
| Cantidad ausente, no entera, ≤ 0 o que desborda la existencia | 400 | `{codigo:"CANTIDAD_INVALIDA",mensaje}` |
| Cuerpo vacío, JSON roto, raíz que no es objeto, `operacionId` ausente o que no es UUID | 400 | `{codigo:"DATOS_INVALIDOS",mensaje}` |
| `productoId` que la sede no maneja (sensible a mayúsculas) | 404 | `{codigo:"PRODUCTO_DESCONOCIDO",mensaje}` |
| Misma `operacionId` con otra cantidad u otro producto | 409 | `{codigo:"CLAVE_REUTILIZADA",mensaje}` |

Un fallo de disco al guardar responde 500 `ERROR_INTERNO` sin tocar la memoria: el cliente reintenta con la misma clave.

## Orden de validación

1. Cuerpo interpretable (`DATOS_INVALIDOS`) y `operacionId` UUID (`DATOS_INVALIDOS`).
2. `cantidad` entero positivo (`CANTIDAD_INVALIDA`).
3. Ya dentro del escritor único (`InventarioLocal.Aplicar`, bajo exclusión mutua):
   1. Si la clave ya existe: mismo producto y cantidad → resultado guardado; otro contenido → `CLAVE_REUTILIZADA`. La clave se revisa **antes** que el producto, porque el contrato dice que otra cantidad o producto con esa clave es 409.
   2. Producto inexistente → `PRODUCTO_DESCONOCIDO` (no se registra la clave).
   3. `existencia + cantidad` que supere `int.MaxValue` → `CANTIDAD_INVALIDA`.

Todo rechazo ocurre antes de anotar nada, así que cantidades, revisión, claves y publicaciones quedan intactas y el archivo no se reescribe.

## Efecto de una reposición aplicada

En **un solo** cambio atómico (snapshot completo con escritura temporal y reemplazo, ver [archivos-inventario.md](archivos-inventario.md)):

- `existencia += cantidad`; `reservada` no cambia, así que `disponible` sube lo mismo.
- La revisión del producto sube en 1.
- Se registra `ReposicionRegistrada {operacionId, productoId, cantidad, existencia, reservada}`: la clave de repetición y el `StockLocal` resultante.
- Por cada otra sede queda una salida pendiente `catalogo.actualizado` con el disponible y la revisión resultantes (valores absolutos). RF03 se encarga de enviarla.

La respuesta sale solo después de que todo quedó durable.

## Idempotencia

Repetir la misma clave y contenido **no** modifica nada y devuelve el `StockLocal` guardado, aunque la existencia actual ya haya avanzado: reponer 3 sobre 5/2 da 8/2/6; reponer 1 con otra clave da 9/2/7; repetir la primera sigue dando 8/2/6. Como la clave se persiste con el efecto, esto también vale tras un reinicio.

## Pruebas

- `Negocio/ServicioReposicionesTests.cs`: caso de aceptación (8/2/6, 9/2/7, repetición 8/2/6), `CLAVE_REUTILIZADA`, cantidad inválida (0, negativa, ausente, desborde en el límite), producto desconocido (incluido `caf`), `operacionId` inválido, archivo byte a byte igual en cada rechazo, revisión y salidas pendientes por sede, repetición sin reescritura, reinicio, fallo de disco con reintento y concurrencia (misma clave desde 8 hilos aplica una vez; claves distintas suman exacto).
- `Api/ReposicionesHttpTests.cs`: contrato exacto y reflejo en `GET /inventario`, repetición, 409, 400 (cantidad y datos), 404, método no permitido, aislamiento entre sedes y reinicio sobre los mismos archivos.
