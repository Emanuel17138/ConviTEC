# Archivos del inventario local (RF01, RNF15, RNF16)

Cada sede tiene su propio directorio y nunca lee archivos de otra (RNF14). Los archivos son JSON UTF-8 con claves camelCase. Se leen con `System.Text.Json` sobre DTO que solo usan arreglos nativos (`T[]`), sin colecciones (RNF06).

| Archivo | Contenido | Cuándo se lee |
|---|---|---|
| `maestro-productos.json` | `{productos:[{productoId,nombre,descripcion,categoria,precioUnitarioCentimos}]}` | Solo en el arranque inicial |
| `existencias-iniciales.json` | `{sedeId,existencias:[{productoId,existencia}]}`: productos que maneja la sede | Solo en el arranque inicial |
| `estado-inventario.json` | `{version,sedeId,stock:[{producto:{...},existencia,reservada,revision}],lotes,comandos,reposiciones,salidasPendientes}` (ver más abajo) | Siempre que exista (recuperación) |

Ejemplos en `datos/ejemplo/`.

## Arranque inicial y recuperación

`CargadorInventario.Cargar(rutas, sedesRemotas)` decide por la presencia de `estado-inventario.json`:

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

`RepositorioInventario.GuardarEstado(EstadoInventarioDto)` escribe `estado-inventario.json.tmp`, fuerza el contenido a disco (`Flush(true)`) y lo renombra sobre el definitivo con `File.Move(..., overwrite: true)`. Un corte a mitad de escritura deja intacto el snapshot anterior. Un `.tmp` sobrante se ignora al recuperar y el siguiente guardado lo sobrescribe.

## Contenido del snapshot (RF02)

Un solo archivo guarda juntos todo lo que debe sobrevivir a un reinicio. Se mantiene `version: 1`: las secciones nuevas son campos opcionales y un snapshot de RF01 (sin ellas) se recupera con revisión 0 y secciones vacías.

| Sección | Contenido | Clave en memoria (AVL) |
|---|---|---|
| `stock[].revision` | Revisión entera creciente del producto. Sube una vez por cambio que lo modifica, incluso al confirmar sin variar el disponible | `productoId` |
| `lotes` | `{pedidoId,coordinadoraSedeId,estado,lineas:[{productoId,cantidad,precioUnitarioCentimos}]}` | `pedidoId` |
| `comandos` | `{mensajeId,tipo,origenSedeId,contenido,resultado:{pedidoId,operacion,estadoReserva,codigo,mensaje}}`: respuesta original de cada comando | `mensajeId` |
| `reposiciones` | `{operacionId,productoId,cantidad,existencia,reservada}`: clave de repetición y StockLocal resultante | `operacionId` |
| `salidasPendientes` | `{mensajeId,destinoSedeId,productoId,disponible,revision}`: intención de enviar `catalogo.actualizado` | destino + producto |

Reglas que valida la recuperación (con archivo y posición en el mensaje): claves duplicadas en cualquier sección, estado de reserva desconocido o `INEXISTENTE` en un lote, lote `RESERVADA`/`CONFIRMADA` sin líneas, con producto ajeno o con producto repetido, cantidad de línea ≤ 0, reposición o salida de un producto que la sede no maneja, resultado ausente en un comando y salida dirigida a la propia sede. Un lote `RECHAZADA` o `LIBERADA` puede tener cero líneas y, si es `RECHAZADA`, líneas de productos ajenos: no se le exige coherencia con el stock.

## Cambios bajo exclusión mutua (RF02)

`InventarioLocal` es el único escritor. Todo cambio entra por `Aplicar(operacion)`, que bajo un cerrojo local:

1. Ejecuta la operación sobre un `CambioInventario`: ahí se lee (primero lo preparado, luego lo vivo) y se anota `ModificarStock`, `CrearLote`/`ActualizarLote`, `RegistrarComando` y `RegistrarReposicion`. Si lanza una excepción, el cambio se descarta entero. Si no anotó nada, no se escribe nada.
2. Sube **una vez** la revisión de cada producto modificado y crea una salida pendiente nueva por cada producto modificado y cada otra sede (`--remotas`). La salida lleva el disponible y la revisión absolutos y **reemplaza** a la pendiente de esa pareja destino–producto: mientras una sede está caída el archivo no crece sin límite.
3. Guarda el snapshot **completo** de forma atómica.
4. Solo si guardó, instala el cambio en los AVL en memoria. Si el guardado falla, la excepción sale y memoria y disco quedan en el estado anterior completo.

El resultado de la operación (por ejemplo, la respuesta a un comando) solo se entrega después del paso 4. Las consultas de `InventarioLocal` toman el mismo cerrojo, así que nunca ven un cambio a medias.

Cortes del proceso:

| Dónde ocurre el corte | Estado que recupera el siguiente arranque |
|---|---|
| Antes o durante la escritura del temporal | El snapshot anterior completo. El `.tmp` sobrante se ignora |
| Con el temporal completo, antes de `File.Move` | El snapshot anterior completo |
| Después de `File.Move` | El snapshot nuevo completo (aunque el proceso no llegara a responder: repetir el mensaje devuelve el resultado guardado) |

Las pruebas simulan los dos cortes con un punto de interrupción interno de `RepositorioInventario` (`EtapaGuardado.TemporalEscrito` y `EtapaGuardado.Reemplazado`).

## Sedes remotas

`--remotas SJ,ALA,LIM` (o `CONVITEC_REMOTAS`) lista las otras sedes. Es opcional (vacío por defecto); se rechazan identificadores vacíos, repetidos o iguales a la sede propia. RF03 reutiliza esta misma lista para solicitar instantáneas.

## Consulta local reutilizable

`InventarioLocal` ofrece `ConsultarInventario()` (todo el stock por `productoId` ascendente, agotados incluidos, `[]` si no hay productos), `BuscarStock(productoId, out stock)`, `Contiene`, `Cantidad`, `BuscarRevision`, `BuscarLote`, `BuscarComando`, `BuscarReposicion` y `ConsultarSalidasPendientes()`. RF02 reutiliza esta misma instancia; el endpoint HTTP la proyecta a JSON (ver [consulta-http.md](consulta-http.md)). La reposición (`ServicioReposiciones`) usa `Aplicar` para registrar la clave junto con el efecto (ver [reposicion-http.md](reposicion-http.md)).

## Lotes de reserva

Las transiciones de `stock.reservar`, `stock.confirmar` y `stock.liberar` sobre estos lotes y comandos están en [lotes-reserva.md](lotes-reserva.md).
