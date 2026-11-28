# Consulta del catálogo global (RF03 · rf03-consulta)

La sede ofrece una sola vista del catálogo global, leída por dos consumidores:

- **Web (Integrante 4):** `GET /api/v1/catalogo`.
- **Pedidos (Integrante 2):** el puerto local `IConsultaCatalogo.ConsultarCatalogo() → ProductoGlobal[]`, que la sede registra en el contenedor de servicios como singleton.

Ambos leen la copia de **esta** sede: nunca se consulta a otra sede ni se leen sus archivos (RNF14). Una oferta es la última información recibida; la reserva de RF02 vuelve a comprobar el stock en la sede propietaria.

## Contrato

`GET /api/v1/catalogo` → 200 con un arreglo de `ProductoGlobal`, por `productoId` ascendente y, dentro de cada producto, por `sedeId` ascendente (comparación ordinal, sensible a mayúsculas).

```json
[
  {
    "producto": {"productoId":"ARR","nombre":"Arroz","descripcion":"Arroz 2 kg","categoria":"Despensa","precioUnitarioCentimos":90000},
    "ofertas": [{"sedeId":"LIB","disponible":7,"revision":2}]
  },
  {
    "producto": {"productoId":"CAF","nombre":"Café","descripcion":"Café molido 500 g","categoria":"Bebidas","precioUnitarioCentimos":150000},
    "ofertas": [
      {"sedeId":"LIB","disponible":0,"revision":3},
      {"sedeId":"SJ","disponible":5,"revision":0}
    ]
  }
]
```

- Las ofertas con cero se incluyen (agotado). Una sede sin datos de ese producto **se omite** (en el ejemplo, ALA no informó CAF).
- La oferta de la propia sede sale de su inventario vivo (`disponible = existencia − reservada`, con la revisión actual). Las de las demás salen de la réplica.
- Vista inicializada sin productos: 200 y `[]`.
- **503** `{codigo:"CATALOGO_INICIALIZANDO", mensaje}` mientras falte la primera instantánea de alguna otra sede. El mensaje nombra las sedes pendientes. Un cambio aislado (`catalogo.actualizado`) no completa la primera sincronización de su sede: solo la instantánea. Una copia ya inicializada se conserva al reiniciar (la marca está en `catalogo-replicado.json`) y se consulta de inmediato.
- Otros métodos sobre la ruta: 405. Fallo inesperado: 500 `ERROR_INTERNO`.

### Puerto para Pedidos

```csharp
public interface IConsultaCatalogo
{
    bool EstaInicializado { get; }
    ProductoGlobal[] ConsultarCatalogo();   // lanza CatalogoInicializandoException si falta una primera instantánea
}
```

`CatalogoInicializandoException` lleva `Codigo` (`CATALOGO_INICIALIZANDO`) y `SedesPendientes`. Pedidos la traduce a su propio 503 `CATALOGO_INICIALIZANDO`. Los arreglos devueltos son copias: modificarlos no altera la vista.

## Cómo se construye la vista (`Negocio/ConsultaCatalogo.cs`)

1. Si `CatalogoReplicado.SedesPendientes()` no está vacía → `CatalogoInicializandoException`. Nunca se devuelve una vista parcial.
2. Se inserta cada oferta (la propia de `InventarioLocal.ConsultarEntradas()` y las remotas de `CatalogoReplicado.ConsultarOfertas()`) en un `ArbolAvl` propio con la clave ordinal `OfertaReplicada.ClaveDe(productoId, sedeId)`.
3. El recorrido en orden del AVL entrega las ofertas ya por producto y sede, y las de un mismo producto quedan contiguas. Un solo recorrido las agrupa en `ProductoGlobal`.

**Por qué así (RNF07, RNF09):** no se usa `Sort`/`OrderBy`; el orden lo da el propio árbol. Con *n* ofertas el costo es O(n log n) y la memoria O(n), sin tocar el estado real: la vista es de solo lectura. La clave usa el separador U+001F, menor que cualquier carácter imprimible, así que `CA` precede a `CAF` y nunca se mezclan sus ofertas.

Las lecturas de inventario y réplica toman cada una su propio cerrojo, sin mantener ambos a la vez: no hay riesgo de interbloqueo, y cada oferta es siempre un valor absoluto coherente en sí mismo.

## Archivos

| Capa | Archivo |
|---|---|
| Dominio | `Dominio/Oferta.cs`, `Dominio/ProductoGlobal.cs`, `Dominio/CodigosCatalogo.cs` |
| Negocio | `Negocio/IConsultaCatalogo.cs`, `Negocio/ConsultaCatalogo.cs`, `Negocio/CatalogoInicializandoException.cs`, `InventarioLocal.ConsultarEntradas()` |
| HTTP | `Http/RutasCatalogo.cs`, `Http/ProductoGlobalRespuesta.cs` (reutiliza `ProductoRespuesta`) |
| Arranque | `ServidorSede.Crear(inventario, url, mensajeria, catalogo)` y `Program.cs` |

## Pruebas

- `Negocio/ConsultaCatalogoTests.cs`: inicialización (503 lógico, cambio aislado, instantánea vacía), orden, ceros y omisión, prefijos de código (`CA`/`CAF`), mayúsculas, oferta propia tras reposición, revisión 8 → 10/9/10 también tras reiniciar, copia inicializada tras reiniciar, aislamiento de copias y validaciones.
- `Api/CatalogoHttpTests.cs` (Kestrel real): 503 con cuerpo exacto, paso a 200, `[]`, campos exactos en camelCase, **HTTP y puerto con el mismo contenido**, reposición visible, reinicio, 405.

Pendiente tras integrar `rf03-sincronizacion`: la prueba de cuatro sedes sobre la combinación con RF02 (reponer en LIB, reservar desde SJ y confirmar; las cuatro copias convergen).
