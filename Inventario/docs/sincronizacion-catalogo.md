# Sincronización del catálogo entre sedes (RF03 · rf03-sincronizacion)

Cada sede conserva una copia de las ofertas de las demás (`CatalogoReplicado`). Esta pieza la mantiene al día **solo con mensajes RabbitMQ**: nunca se leen archivos ni estructuras de otra sede (RNF14). Hay dos caminos, que se complementan:

| Camino | Quién lo inicia | Mensaje | Para qué sirve |
|---|---|---|---|
| Difusión de cambios | La sede propietaria, cada vez que cambian sus existencias o reservas | `catalogo.actualizado` | Mantener las copias al día en la operación normal. |
| Instantánea | La sede que arranca o se reconecta | `catalogo.solicitar` → `catalogo.instantanea` | Reconstruir la copia sin que haya ventas nuevas, y completar la primera sincronización. |

## Mensajes

Todos usan el sobre v1 y viajan por el exchange `convitec.v1`, hacia la cola `inventario.<destino>`. Las claves de ruta son los nombres de las colas. El propietario de los datos es `origenSedeId`.

| Tipo | Cuerpo | Quién lo envía |
|---|---|---|
| `catalogo.actualizado` | `{producto,disponible,revision}` | Propietaria → cada otra sede |
| `catalogo.solicitar` | `{}` | La que arranca → cada otra sede |
| `catalogo.instantanea` | `{entradas:[{producto,disponible,revision}]}` (con ceros; vacía es válida) | La consultada → la que preguntó |

`producto` es el `Producto` completo (`productoId,nombre,descripcion,categoria,precioUnitarioCentimos`). Los valores son absolutos: la réplica aplica una entrada solo si la pareja sede–producto es nueva o su revisión supera la almacenada, tanto si llega como cambio como dentro de una instantánea. Por eso el transporte puede duplicar y reordenar sin deshacer nada.

### Identificadores

| Mensaje | `mensajeId` | `correlacionId` |
|---|---|---|
| `catalogo.actualizado` | El `mensajeId` guardado junto a la `SalidaPendiente`. Un reintento lo reutiliza. | El mismo `mensajeId`. |
| `catalogo.solicitar` | UUID nuevo por sede y solicitud. Se conserva mientras el broker no la confirme. | El mismo `mensajeId`. |
| `catalogo.instantanea` | Derivado de la solicitud (`IdentificadorDerivado.DeSolicitudCatalogo`, SHA-1 versión 5). Si se reentrega la solicitud, la respuesta lleva el mismo ID. | El de la solicitud. |

## Piezas

| Pieza | Capa | Función |
|---|---|---|
| `ArbolAvl.Eliminar` / `ArbolBinarioBusqueda.Eliminar` | Estructuras | Borrado propio, O(log n) en el AVL con rebalanceo (sucesor en orden). Hace falta para retirar una salida. |
| `InventarioLocal.RetirarSalidas` | Negocio | Quita las salidas entregadas. Guarda el snapshot completo sin ellas y solo después las quita de memoria. |
| `InventarioLocal.SalidasRegistradas` | Negocio | Evento, disparado fuera de la exclusión mutua, cuando un cambio dejó publicaciones nuevas. |
| `TiposMensajeCatalogo`, `CuerpoCatalogo` | Mensajería | Los tres tipos y el formato de sus cuerpos, en un solo lugar. La lectura no lanza: informa el motivo. |
| `ManejadorMensajesCatalogo` | Mensajería | Atiende los tres tipos al recibir. Se registra en el despachador. |
| `SincronizadorCatalogo` | Mensajería | Envía cambios y solicita instantáneas. Usa el puerto `IPublicadorMensajes`; no conoce RabbitMQ. |
| `ConexionRabbit.Reconectada` | Adaptador | Evento cuando la recuperación automática restablece la conexión. |
| `ServicioMensajeria` | Api | Arranca el sincronizador sobre el `PublicadorRabbit` de la conexión y lo avisa al reconectar. |

`ConsumidorInventario` y `DespachadorInventario` no cambian: ya despachaban por tipo y publicaban las salidas del resultado antes de acusar.

## Al recibir (`ManejadorMensajesCatalogo`)

| Situación | Acción del consumidor | Efecto |
|---|---|---|
| `catalogo.actualizado` válido | acusar | `AplicarCambio`: ya es durable al acusar. No completa la primera sincronización de su sede. |
| `catalogo.instantanea` válida | acusar | `AplicarInstantanea`: aplica cada entrada con la misma regla y marca la sede como sincronizada, aunque venga vacía. |
| `catalogo.solicitar` válido | acusar y publicar | Responde con **todas** las ofertas propias (`InventarioLocal.ConsultarEntradas`, ceros incluidos) en `catalogo.instantanea` hacia `inventario.<origen>`. |
| Origen que no es una sede remota configurada, cuerpo inválido o producto repetido en una instantánea | descartar (acusa y registra el motivo) | No hay a quién responder ni nada que reintentar. No se toca la copia. |
| Fallo de disco al guardar | `nack` con reencolado | Nada cambió; se reprocesa entero. |

La respuesta a una solicitud no necesita bandeja de salida: se calcula del estado propio y, si se pierde antes del acuse, el broker reentrega la solicitud y se vuelve a responder con el mismo `mensajeId`.

## Al enviar (`SincronizadorCatalogo`)

### Cambios

Cada operación que modifica existencias o reservas (reposición, reserva, confirmación, liberación) deja en el **mismo snapshot** una `SalidaPendiente` por cada otra sede (RF02). Esa salida lleva `mensajeId`, destino, producto, disponible y revisión absolutos. Por pareja destino–producto solo se conserva la última.

1. Se publica cada salida en `inventario.<destino>` con mensaje persistente, confirmación del publicador y `mandatory`.
2. **Solo si el broker confirmó y alguna cola recibió el mensaje** se considera entregada. `PublicadorRabbit` lanza `EnvioFallidoException` en cualquier otro caso (sin confirmación, sin cola, canal caído).
3. Las entregadas se retiran con un solo guardado (`RetirarSalidas`). Se retira una salida únicamente si sigue pendiente **con el mismo `mensajeId`**: si un cambio posterior la sustituyó mientras se publicaba, la nueva se conserva y se enviará después.
4. Una salida que falla se queda pendiente y no frena a las de otros destinos. Se reintenta cada 5 s con el mismo `mensajeId`.

Si cae el proceso entre la confirmación y el retiro, al reiniciar se reenvía. Es inofensivo: el receptor ignora lo que no supera su revisión almacenada.

El `Producto` del mensaje se toma del stock actual; el disponible y la revisión, de la salida guardada. Así lo enviado es siempre la observación del momento del cambio.

### Solicitudes

Al arrancar el sincronizador y cada vez que `ConexionRabbit.Reconectada` se dispara, se pide `catalogo.solicitar` a **todas** las otras sedes. Una solicitud sin confirmar se reintenta con el mismo `mensajeId`; una reconexión no pisa la que sigue pendiente.

**Por qué la solicitud no se guarda en disco.** Es idempotente y barata de repetir: cada arranque la vuelve a pedir y la respuesta es una copia completa de valores absolutos. Guardar la intención solo añadiría un archivo más que recuperar sin ganar nada. La diferencia con los cambios es que un cambio **sí** se perdería sin su salida pendiente, porque la sede receptora no tendría forma de saber que existió: la instantánea la corregiría, pero solo si alguien la vuelve a pedir.

### Bucle

`EjecutarAsync` espera una señal (`Avisar`) y, mientras quede algo sin confirmar, reintenta cada 5 s. El evento `SalidasRegistradas` lo despierta al instante cuando hay un cambio nuevo, así que no hay sondeo cuando todo está entregado. Un fallo inesperado se registra y se reintenta: nunca detiene la difusión. Si el broker no estaba disponible al arrancar la sede, `ServicioMensajeria` lo reintenta y el sincronizador empieza cuando conecta, lo que cuenta como "iniciar".

## Arranque de una sede nueva o que se reconecta

1. Arranca, carga sus archivos (RF01, RF02, RF03) y, si hay broker, empieza a consumir `inventario.<sede>`.
2. El sincronizador pide la instantánea a cada otra sede. Si una está apagada, la solicitud espera en su cola: las colas de todas las sedes se declaran al arrancar.
3. Cada instantánea que llega marca esa sede como sincronizada. Mientras falte alguna, `GET /api/v1/catalogo` responde 503 `CATALOGO_INICIALIZANDO`. Con todas, responde 200 (`[]` si ninguna sede tiene productos).
4. Una copia ya inicializada se conserva en el archivo y se puede consultar al reiniciar aunque las demás sedes estén apagadas.

## Pruebas

Sin broker (publicador en memoria): `CuerpoCatalogoTests`, `ManejadorMensajesCatalogoTests`, `SincronizadorCatalogoTests`, `RetiroSalidasTests` y las pruebas de `Eliminar` en los índices (`IndiceOrdenadoContratoTests`, `ArbolAvlTests`).

Con RabbitMQ real: `RabbitCatalogoTests` (misma configuración que `RabbitDosSedesTests`: `CONVITEC_RABBITMQ_TEST`, prefijo único por prueba, borra sus colas).

| Prueba | Caso de aceptación |
|---|---|
| `SjConTresRemotas_...` | Dos instantáneas y un cambio de la tercera mantienen el 503; la instantánea vacía de la tercera lo pasa a consultable. Una sede sin datos se omite. |
| `CuatroSedes_...` | Cuatro instancias con sus propios archivos. Reponer en LIB llega a las otras tres. Reservar desde SJ reduce el disponible de LIB en todas las copias. Confirmar conserva ese disponible con revisión superior. Solo LIB modifica sus cantidades. |
| `SedeApagadaMientrasSeRepone_...` | Sede desconectada, dos reposiciones, reinicio de la publicadora y reconexión: converge. La copia inicializada es consultable al reiniciar con las demás apagadas. |
| `SedeNuevaSinVentas_...` | Una sede nueva reconstruye las ofertas, ceros incluidos, sin que haya ventas nuevas. |
| `DestinoSinCola_...` | Sin cola destinataria la salida no se retira, sobrevive al reinicio y se entrega con el mismo `mensajeId` cuando la cola aparece. |
| `MensajeDeCatalogoDeUnaSedeAjena_...` | Un mensaje de una sede no configurada o con cuerpo roto se descarta sin bloquear los siguientes. |

Las pruebas de revisión (8, luego 10, 9 y 10 repetido) están en `CatalogoReplicadoTests` y `ManejadorMensajesCatalogoTests`.

### Límites conocidos

- La reconexión automática del cliente RabbitMQ (`Reconectada`) está enlazada y su efecto (volver a pedir las instantáneas) se prueba sin broker con `AvisarReconexion`, pero no hay una prueba automática que corte y restablezca la conexión real.
- `RabbitDosSedesTests` (RF02, anterior a esta rama) falla de forma intermitente (≈ 20 % de las corridas, también sobre el commit base `a7fd7c4`) con `AlreadyClosedException` ("Heartbeat missing" o `CHANNEL_ERROR - expected 'channel.open'`). No se investigó la causa ni se tocó.
