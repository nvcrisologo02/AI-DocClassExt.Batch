# Diseño corto: discrepancias en `Sustento` y `Revisar` con filas sin resolver (consola IvaCedente)

Fecha: 2026-10-30. Línea: batch. Cambio pequeño sobre `src/DocumentIA.Batch.IvaCedente`.

## Intención

Quien revisa el consolidado necesita ver, sin abrir el CSV por documento, qué títulos discrepan y qué dice cada uno cuando un activo sale a revisión por IVA contradictorio, deducción contradictoria o cuotas distintas. Además, un activo que ya tiene IVA resuelto no debe salir a revisión solo porque le queden ficheros sin resolver: se da por bueno y el motivo queda informado.

## Objetivos

- `Sustento` añade, solo en activos con alguno de los tres motivos de discrepancia, un tramo final con los títulos cuyo valor difiere del título elegido y sus valores.
- Un activo cuyo único motivo es `filas sin resolver (n)` y cuyo `HuboIVA` es `sí` o `no` sale con `Revisar = no`; `MotivoRevisar` conserva el motivo.

## No objetivos

- No cambia la elección del título de sustento ni la regla sí > no > no consta.
- No se añaden columnas al CSV consolidado ni cambia el CSV por documento.
- No se relanza nada contra PRO: `consolidar` relee `lote-salida.csv` y regenera el consolidado.

## Enfoques considerados

- A (elegido): listar solo los títulos que discrepan del elegido, en la misma frase `Sustento`. Mantiene la frase corta en el caso normal y muestra justo la discrepancia.
- B: listar todos los títulos del activo con sus valores. Más completo pero ruidoso en activos con muchos títulos que coinciden.
- C: nueva columna `Discrepancias` en vez de alargar `Sustento`. Filtrable, pero el usuario pidió explícitamente verlo en `Sustento` y el consolidado ya tiene 16 columnas.

## Comportamiento acordado

Tramo de discrepancias, añadido al final de la frase actual con `; discrepan: ` y una entrada por título separada por `; `:

- `IVA contradictorio`: cada título con `HuboIVA` distinto del valor consolidado, como `doc <ObjectId> IVA <valor>`; si vale `sí`, con tipo y cuota en formato es-ES como en la frase principal.
- `cuotas de IVA distintas`: cada título con `HuboIVA = sí` y cuota no vacía distinta de la del título elegido, con su tipo y cuota.
- `deducción contradictoria`: cada título con `DeduccionIVA` distinta de la consolidada (ignorando `no aplica`), como `doc <ObjectId> deducción <valor>`.
- Un mismo título que discrepe en más de una cosa sale en una sola entrada: `doc 456 IVA sí 21% 1.200,00 €, deducción no`.
- Entradas ordenadas por `ObjectId`; sin discrepancias no se añade nada (frase idéntica a la actual).
- `MotivoRevisar` no cambia.
- Nota de implementación (2026-10-30): en `IVA contradictorio` y `deducción contradictoria` solo se listan los títulos con valor `sí` o `no`; `no consta` y `no aplica` no cuentan como discrepancia.

`Revisar` con filas sin resolver (mi criterio, no discutido):

- `HuboIVA` en `sí` o `no` y ningún otro motivo: `Revisar = no`, `MotivoRevisar = filas sin resolver (n)`.
- `HuboIVA` en `no consta` o `sin título` con filas sin resolver: sigue `Revisar = sí`, porque el fichero sin leer podría ser el título que falta.
- Con cualquier otro motivo además: `Revisar = sí`, como hoy.
- El aviso de `ConsolidarCommand` sobre filas incompletas pasa a decir que el motivo queda informado y que solo marcan `Revisar = sí` los activos sin IVA resuelto.

## Impacto

- Código: `Engine/Consolidador.cs` (cálculo de `Revisar` y recogida de discrepancias), `Engine/FraseSustento.cs` (tramo nuevo), `Commands/ConsolidarCommand.cs` (texto del aviso). Probablemente una lista de discrepancias en `FilaActivo` que no se escribe en el CSV.
- Tests: `ConsolidadorTests` (los tres motivos con discrepancias, `Revisar` con filas sin resolver en sus tres variantes; el test actual `Consolidar_ActivoConFilaIncompleta_MarcaRevisarSinCambiarLosValores` cambia de expectativa) y `FraseSustentoTests` (tramo de discrepancias, uno y varios títulos, título con dos discrepancias).
- Documentación: `README.md` de la consola, filas `Sustento`, `MotivoRevisar` y `Revisar` de la tabla del consolidado.
- Resultado: regenerar `docs/auxiliares/temps/2026-10-29/lote-consolidado-v3.csv` como `v4` con `consolidar` (sin coste de IA) y comparar los recuentos de `Revisar`.

## Queda para el constructor

Forma exacta de recoger las discrepancias (lista de tuplas o filas), nombres de los tests, orden de las partes dentro de una entrada.
