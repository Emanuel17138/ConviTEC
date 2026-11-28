# Mensajería RabbitMQ del inventario (RF02, `rf02-mensajes`)

Transporte de los comandos de stock entre sedes. El negocio (`ProveedorStock`) no sabe nada de RabbitMQ: el núcleo habla con dos puertos y un adaptador los conecta al broker.

| Pieza | Proyecto | Función |
|---|---|---|
| `Sobre`, `CodificadorSobre` | `ConviTEC.Inventario` (`Mensajeria/`) | Sobre v1 `{version,tipo,mensajeId,correlacionId,origenSedeId,destinoSedeId,cuerpo}` en JSON UTF-8 camelCase. La decodificación nunca lanza: devuelve el motivo. |
| `TopologiaConvitec` | idem | Nombres: exchange `convitec.v1`, colas `inventario.<sedeId>` y `pedidos.<sedeId>`. |
| `DespachadorInventario`, `IManejadorMensaje` | idem | Valida el sobre (versión, destino) y entrega al manejador registrado para el `tipo`. |
| `ManejadorComandosStock` | idem | Atiende `stock.reservar`, `stock.confirmar`, `stock.liberar` con `ProveedorStock` y arma `stock.resultado`. |
| `ClienteInventario` | idem | Lo usa Pedidos (Integrante 2): arma, envía y lee resultados. |
| `IPublicadorMensajes` | idem | Puerto de salida. |
| `ConexionRabbit`, `PublicadorRabbit`, `ConsumidorInventario` | `ConviTEC.Inventario.Rabbit` | Adaptador RabbitMQ (`RabbitMQ.Client` 7). |
| `ServicioMensajeria` | `ConviTEC.Inventario.Api` | Mantiene el consumidor mientras la sede corre. |

## Topología

`ConexionRabbit.DeclararTopologiaAsync` declara el exchange directo durable `convitec.v1` y, por sede, las colas durables `inventario.<sedeId>` y `pedidos.<sedeId>` vinculadas con una clave igual a su nombre. Declara las de la sede propia **y las de las remotas**: declarar es idempotente, y así un mensaje para una sede apagada espera en su cola en vez de devolverse como "sin destinatario". Solo Inventario consume `inventario.*` y solo Pedidos consume `pedidos.*`.

## Flujo de un comando en la proveedora

1. El consumidor recibe un mensaje de `inventario.<sede>` (un mensaje a la vez, acuse manual).
2. `DespachadorInventario` lo decodifica y lo entrega al manejador de su `tipo`.
3. `ManejadorComandosStock` llama a `ProveedorStock`, que deja durable el efecto, el lote y el resultado en un solo snapshot (`InventarioLocal.Aplicar`).
4. Con eso hecho, el consumidor publica `stock.resultado` en `pedidos.<origenSedeId>` con confirmación del publicador y `mandatory`.
5. Solo entonces acusa el comando.

| Situación | Acción | Efecto |
|---|---|---|
| Sobre ilegible, versión ≠ 1, destino ajeno o `tipo` sin manejador | acusar y descartar (se registra el motivo) | No hay a quién responder ni nada que reintentar. |
| Fallo al guardar (disco) | `nack` con reencolado tras 500 ms | No cambió nada; se reprocesa entero. |
| Respuesta sin confirmar o sin cola destinataria | `nack` con reencolado tras 500 ms | El comando ya es durable; al reprocesarlo, `ProveedorStock` devuelve el resultado original y se republica. |
| Caída entre persistir y acusar | el broker reentrega | Igual que arriba: el descuento ocurre una sola vez. |

No hace falta una bandeja de salida para los resultados: el resultado ya está en `ComandoRegistrado` y la reentrega lo regenera.

### Identificador de la respuesta

`mensajeId` de `stock.resultado` es un UUID determinista derivado del `mensajeId` del comando (`IdentificadorDerivado.DeComando`, SHA-1 con versión 5). Una respuesta republicada lleva el mismo ID, y Pedidos puede detectar el duplicado. `correlacionId` es el `mensajeId` del comando, y `destinoSedeId` es su `origenSedeId` (la coordinadora). Si otra sede intenta decidir sobre un lote ajeno, la respuesta va a esa sede con `ESTADO_INCOMPATIBLE`.

### Cuerpo tolerante

El manejador lee el cuerpo sin lanzar: un campo ausente o de otro tipo se vuelve `null` y `ProveedorStock` responde `DATOS_INVALIDOS` (por ejemplo, `cantidad` 2.5 o `"2"`).

### Limitación conocida

Una coordinadora cuya sede no esté declarada en el broker (origen que no es la propia ni una remota configurada) hace que la respuesta no tenga cola: el comando se reencola indefinidamente. En el despliegue de cuatro sedes todas se declaran, así que no ocurre.

## Cliente para Pedidos

```csharp
ClienteInventario cliente = new ClienteInventario("SJ", publicador);

string mensajeId = ClienteInventario.NuevoMensajeId();           // Pedidos lo guarda con la intención
Sobre reservar = cliente.CrearReservar(mensajeId, "LIB", pedidoId,
    new[] { new LineaReserva("CAF", 2, 150000) });
await cliente.EnviarAsync(reservar);                              // reintentar con el MISMO sobre si falla

// Al consumir pedidos.SJ:
if (ClienteInventario.TryLeerResultado(sobreRecibido, out ResultadoStock? r, out string motivo)) { /* r.EstadoReserva, r.Codigo */ }
```

Una petición nueva usa su `mensajeId` como `correlacionId`. Un reintento reutiliza el sobre completo. `EnviarAsync` lanza `EnvioFallidoException` si el broker no confirma o no hay cola; Pedidos conserva la intención y reintenta.

## Cómo suma RF03

`ManejadorMensajesCatalogo` es un `IManejadorMensaje` con los tipos `catalogo.actualizado`, `catalogo.solicitar` y `catalogo.instantanea`, registrado en `Program.cs` con `despachador.Registrar(...)`. No toca el consumidor ni el despachador. La instantánea que responde a una solicitud sale como cualquier otra salida del despacho: el consumidor la publica en `inventario.<origen>` y después acusa. El envío de los cambios y la solicitud de instantáneas los hace `SincronizadorCatalogo`, que corre dentro de `ServicioMensajeria` sobre el mismo `PublicadorRabbit`. Ver [sincronizacion-catalogo.md](sincronizacion-catalogo.md).

## Configuración y ejecución

La sede lee `--rabbitmq <uri>` o la variable `CONVITEC_RABBITMQ`. Por defecto usa `amqp://guest:guest@localhost:5672/`. Si el broker no está disponible al arrancar, el servidor HTTP sirve igual y la conexión se reintenta cada 5 s.

```powershell
dotnet run --project src/ConviTEC.Inventario.Api -- --sede LIB --datos .\datos\lib --url http://localhost:5001 --remotas SJ
dotnet run --project src/ConviTEC.Inventario.Api -- --sede SJ  --datos .\datos\sj  --url http://localhost:5002 --remotas LIB
```

### RabbitMQ en Windows

Instalar Erlang/OTP y RabbitMQ Server, y activar el panel de administración: `rabbitmq-plugins enable rabbitmq_management` (http://localhost:15672, usuario `guest`).

## Pruebas

- Sin broker: `CodificadorSobreTests`, `DespachadorInventarioTests`, `ManejadorComandosStockTests`, `ClienteInventarioTests`, `IdentificadorDerivadoTests`.
- Sin broker, catálogo: `CuerpoCatalogoTests`, `ManejadorMensajesCatalogoTests`, `SincronizadorCatalogoTests` y `RetiroSalidasTests`.
- Con RabbitMQ real, catálogo: `RabbitCatalogoTests` (ver [sincronizacion-catalogo.md](sincronizacion-catalogo.md)).
- Con RabbitMQ real: `RabbitDosSedesTests`. Usan `amqp://guest:guest@localhost:5672/` o la URI de `CONVITEC_RABBITMQ_TEST`. Cada prueba crea sedes con prefijo único (`T<hex>-LIB`, `T<hex>-SJ`) y borra sus colas. Cubren: reservar y confirmar entre dos sedes, liberar, dos reservas simultáneas de 4 contra 5, liberación anticipada, reintento del mismo sobre, reinicio entre el descuento y el acuse, comando enviado con la sede apagada, mensaje ilegible, publicación sin cola y conexión inalcanzable.
