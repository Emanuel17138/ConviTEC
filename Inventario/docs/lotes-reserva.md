# Lotes de reserva: lado proveedor (RF02)

`ProveedorStock` (`Negocio/ProveedorStock.cs`) aplica `stock.reservar`, `stock.confirmar` y `stock.liberar` sobre el inventario de la sede. Recibe el comando ya sacado del sobre (`SolicitudReserva` o `SolicitudDecision`) y devuelve el `ResultadoStock` del contrato. El sobre y el transporte RabbitMQ están en [mensajeria-rabbitmq.md](mensajeria-rabbitmq.md).

Cada comando corre entero dentro de `InventarioLocal.Aplicar`: exclusión mutua local, cambio completo (cantidades, lote y resultado) guardado de forma atómica y resultado devuelto solo después de persistir. Si el guardado falla, no cambia nada y el mismo comando se reprocesa como nuevo.

## Pasos comunes, en orden

1. **Mismo `mensajeId` ya registrado.** Mismo tipo, origen y huella del cuerpo: se devuelve el resultado original sin escribir, aunque el lote haya avanzado. Otro contenido: `CLAVE_REUTILIZADA`, sin efecto y sin registro.
2. **`pedidoId` ausente o vacío:** `DATOS_INVALIDOS`, sin lote.
3. **Lote existente de otra coordinadora** (`origenSedeId` distinto del que lo creó): `ESTADO_INCOMPATIBLE` con el estado actual del lote, sin efecto.
4. Transición propia del comando. El resultado se registra en el mismo cambio que el efecto.

La huella (`HuellaComando`) es el JSON canónico del cuerpo: líneas en el orden recibido y nulos explícitos.

## stock.reservar

Si el pedido ya tiene lote (también uno LIBERADA, que cubre la reserva tardía): `ESTADO_INCOMPATIBLE` con el estado actual del lote y sin tocar nada.

Si es nuevo, se valida todo el lote antes de retener una unidad, por categorías y en este orden (el mensaje nombra el primer fallo):

1. `DATOS_INVALIDOS`: sin líneas, línea nula, producto vacío o repetido (comparación ordinal), cantidad ≤ 0 o precio < 0 o ausente.
2. `PRODUCTO_DESCONOCIDO`.
3. `PRECIO_DISTINTO` frente al precio de catálogo.
4. `STOCK_INSUFICIENTE`: cantidad mayor que el disponible.

Si falla: lote RECHAZADA, cantidades intactas, revisión sin cambio. Si todo pasa: sube `reservada` en cada línea, lote RESERVADA y `OK`.

## stock.confirmar

| Lote | Efecto | estadoReserva / codigo |
|---|---|---|
| no existe | ninguno, no se crea lote | INEXISTENTE / ESTADO_INCOMPATIBLE |
| RESERVADA | resta a `existencia` y a `reservada`; lote CONFIRMADA | CONFIRMADA / OK |
| CONFIRMADA | ninguno | CONFIRMADA / OK |
| LIBERADA o RECHAZADA | ninguno | estado actual / ESTADO_INCOMPATIBLE |

## stock.liberar

| Lote | Efecto | estadoReserva / codigo |
|---|---|---|
| no existe | crea lote LIBERADA sin líneas (liberación anticipada) | LIBERADA / OK |
| RESERVADA | resta solo a `reservada`; lote LIBERADA | LIBERADA / OK |
| LIBERADA | ninguno | LIBERADA / OK |
| RECHAZADA | cierra en LIBERADA, sin mover unidades | LIBERADA / OK |
| CONFIRMADA | ninguno | CONFIRMADA / ESTADO_INCOMPATIBLE |

## Revisión y publicación

Toda modificación de cantidades sube la revisión del producto una sola vez por cambio y reemplaza la salida pendiente de `catalogo.actualizado` para cada otra sede. Por eso confirmar sube la revisión aunque el disponible conserve el valor de la reserva. Los rechazos y las liberaciones sin reserva solo registran lote y resultado: no tocan revisión ni salidas.

## Ejemplo (CAF, existencia 5)

| Paso | existencia / reservada / disponible |
|---|---|
| inicio | 5 / 0 / 5 |
| reservar 2 | 5 / 2 / 3 |
| confirmar | 3 / 0 / 3 |
| (en vez de confirmar) liberar | 5 / 0 / 5 |
