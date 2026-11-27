# Índice del catálogo local de productos (RF01, RNF07)

## Qué se necesita

El inventario local se consulta por `productoId` (búsqueda exacta, sensible a mayúsculas) y `GET /api/v1/inventario` lo entrega en orden ascendente de `productoId`. El estado se reconstruye al arrancar insertando producto por producto (RNF16). No se pueden usar colecciones ni ordenamientos incorporados (RNF06, RNF09), así que el índice es una estructura propia.

## Alternativas

`n` es la cantidad de productos.

| Estructura | Buscar | Insertar | Recorrido en orden | Observaciones |
|---|---|---|---|---|
| Árbol binario de búsqueda (BST) | O(log n) promedio, **O(n)** peor caso | O(log n) promedio, **O(n)** peor caso | O(n) | Con claves ya ordenadas degenera en una lista. |
| **Árbol AVL (elegido)** | **O(log n)** garantizado | **O(log n)** garantizado | O(n) | Altura ≤ 1,44·log2(n+2). Cada inserción hace a lo sumo una rotación simple o doble. |
| Arreglo ordenado + búsqueda binaria | O(log n) | O(n) (desplazar elementos) | O(n) | Simple, pero cada inserción mueve elementos. |
| Tabla hash propia | O(1) promedio | O(1) promedio | O(n log n): hay que ordenar aparte | No guarda el orden que exige `GET /inventario`. |

## Por qué AVL

1. **Peor caso acotado.** El maestro de productos suele llegar ordenado por código desde un archivo. Ese es justo el peor caso del BST. El AVL se reequilibra y mantiene la altura logarítmica sin importar el orden de llegada.
2. **El orden sale gratis.** El recorrido en orden entrega los productos por `productoId` ascendente, que es lo que devuelve `GET /api/v1/inventario`. No hace falta un ordenamiento adicional.
3. **Reconstrucción por inserción.** Al recuperar el estado se insertan los productos uno a uno, y la estructura queda balanceada por construcción.
4. **Interno al módulo.** Quien consume el módulo solo ve `IIndiceOrdenado<TClave, TValor>`. Cambiar de estructura no afecta a Pedidos, Web ni al catálogo replicado.

El costo es más código (cuatro rotaciones y alturas por nodo). Para un catálogo pequeño la diferencia con un BST es poca, pero el AVL elimina el caso degenerado por completo.

## Medición

`ComparacionIndicesTests` inserta `P000001..P001000` en orden ascendente en ambos árboles:

| Estructura | Altura con 1000 claves ordenadas |
|---|---|
| BST sin balance | 1000 |
| AVL | ≤ 14 (cota teórica ⌊1,44·log2(1002)⌋) |

`ArbolAvlTests` también verifica, tras inserciones ordenadas y aleatorias, que en cada nodo las alturas de los subárboles difieren como máximo en uno. Las inserciones C/B/A, A/B/C, C/A/B y A/C/B dejan siempre B como raíz.

## Decisiones de implementación

- **Clave ordinal.** El comparador se inyecta. Para `productoId` se usa `StringComparer.Ordinal`: sensible a mayúsculas y sin reglas culturales. `CAF` y `caf` son claves distintas.
- **Duplicados.** `Insertar` devuelve `false` y conserva la entrada original. No sobrescribe.
- **Sin colecciones nativas.** Los recorridos devuelven arreglos nativos. El BST usa una pila y una cola propias sobre arreglos, así que no desborda la pila de llamadas aunque degenere.
- **Independencia.** El índice no conoce archivos, HTTP ni `Producto`. Es genérico en clave y valor.
