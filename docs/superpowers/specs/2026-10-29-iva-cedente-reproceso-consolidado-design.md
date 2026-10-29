# IvaCedente: tipología forzada y consolidado con documento de sustento (diseño corto)

Fecha: 2026-10-29 · Work item: AB#101191 (ampliación tras el lote completo) · Línea: batch
Complementa a `2026-10-28-iva-cedente-design.md`; no reabre sus decisiones.

## Intención

Tras el primer lote completo en PRO (2372 filas, 618 llamadas, 70 errores y 22 respuestas sin resultado) hacen falta dos cosas:

1. Poder pedir a DocumentIA que no clasifique y trate el documento como una tipología fija (`resumen.documental`), de modo que el prompt se ejecute siempre y se ahorre la llamada de clasificación. Hoy el core corta con `BAJA_CONFIANZA_CLASIFICACION` antes del prompt (9 de los 22 sin resultado) y la clasificación cuesta una llamada por documento.
2. Que la fila por activo diga qué documento aporta el IVA y la deducción, con sus datos, y no solo la lista de Object ID que sostienen el valor.

## Objetivos, no objetivos, antiobjetivos

- Objetivos: tipología forzada opcional en la llamada con prompt; columnas de sustento en el consolidado; detectar cuotas de IVA distintas entre títulos del mismo activo; dejar el motivo de `Revisar`.
- No objetivos: arreglar los documentos inaccesibles en GDC (56) ni los que Document Intelligence rechaza como corruptos (14): fallan antes de clasificar y ninguna tipología los recupera. Cambiar el prompt. Cambiar la regla "sí gana, luego no, luego no consta".
- Antiobjetivos: perder la compatibilidad de lectura del CSV por documento (se lee por cabecera, debe seguir leyendo salidas anteriores); romper la reanudación.

## Hechos del core verificados el 2026-10-29

- `Instrucciones.ExpectedType = "resumen.documental"` existe en el catálogo (`config/tipologias/resumen.documental.validation.json`). El orquestador omite la clasificación (`Modelo = expectedtype-input`, confianza 1.0), no entra en baja confianza y, con `ClassificationOnly` y prompt, ejecuta `PromptActivity`. Un ExpectedType que no resuelve se ignora y se clasifica normalmente.
- El nombre va con punto (`resumen.documental`), no con guion bajo.

## Enfoques considerados

Reproceso:
- A (elegido): reprocesar solo las filas fallidas cuyo activo aún puede cambiar: 32 de GDC inaccesible (sin coste si GDC sigue sin servirlas) y 3 sin resultado de prompt. Los 14 corruptos y las filas de activos ya "sí/sí" no se tocan.
- B: muestra de 8 de GDC más los 3. Descartado por el usuario: los de GDC no cuestan.
- C: las 92 fallidas. Descartado: no cambia ningún resultado.

Representación en el consolidado:
- A: columnas estructuradas (filtrables en Excel).
- B: una frase `Sustento` por activo (legible, no filtrable).
- C (elegido por el usuario): ambas.

## Diseño

### Tipología forzada

- Nueva clave `TipologiaForzada` en `IvaConfig` y `config.sample.json`, por defecto `null`. Nueva opción de línea de comandos `--tipologia <codigo>` en `procesar`, que la sustituye (mi decisión: evita editar `config.json` para una tanda).
- `ConstructorPeticiones.ConPrompt` pone `Instrucciones.ExpectedType = TipologiaForzada` cuando no está vacía. La petición de preclasificación (`Clasificacion`) no la lleva: su función es clasificar.
- La consola muestra la tipología forzada en la cabecera de consola junto al entorno y las colas. La columna `Tipologia` del CSV por documento recoge lo que devuelva DocumentIA (se espera `resumen.documental`), que sirve de marca de las filas reprocesadas así.

### Consolidado por activo

Columnas nuevas, tras `DeduccionIVA` y antes de `ObjectIdsSoporte`:

| Columna | Contenido |
|---|---|
| `DocIVA` | Object ID del título elegido para `HuboIVA` cuando vale `sí` o `no`; vacío en `no consta` y `sin título`. |
| `TipoIVA`, `CuotaIVA` | Del título elegido, solo cuando `HuboIVA = sí`; números con punto decimal como en el CSV por documento. |
| `Cedente`, `FechaAdquisicion` | Del título elegido. |
| `DocDeduccion` | Object ID del título elegido para `DeduccionIVA` cuando vale `sí` o `no`; vacío en otro caso. |
| `Sustento` | Frase: `IVA sí 21% 64.050,00 € (doc 12345, cedente X, 2008-03-12); deducción sí (doc 12345)`. Variantes: `IVA no (doc …)`, `IVA no consta`, `sin título previo`; tipo sin decimales innecesarios, cuota con separadores españoles y `€`; se omite lo que falte. |
| `MotivoRevisar` | Lista separada por `;` de: `filas sin resolver (n)`, `IVA contradictorio`, `deducción contradictoria`, `cuotas de IVA distintas`. Vacía cuando `Revisar = no`. |

Elección del título para `DocIVA`: entre los títulos con `HuboIVA` igual al valor consolidado, el que tenga `CuotaIVA`; a igualdad, el que tenga `TipoIVA`; a igualdad, mayor `Confianza`; a igualdad, el primero en el orden del fichero. Para `DocDeduccion`: entre los títulos con `DeduccionIVA` igual al valor consolidado, el mismo documento que `DocIVA` si cumple; si no, mayor `Confianza`, luego orden.

Regla nueva de revisión: si entre los títulos con `HuboIVA = sí` hay más de una `CuotaIVA` no vacía distinta, `Revisar = sí` con motivo `cuotas de IVA distintas` (hoy 6 activos). `ObjectIdsSoporte` y `Revisar` se mantienen como están.

### Tests y documentación

- Tests de `Consolidador` para la elección del título (cuota, tipo, confianza, orden), `DocDeduccion`, `MotivoRevisar` por cada motivo, `Sustento` en sus variantes y la regla de cuotas distintas. Test de `ConstructorPeticiones` con y sin tipología forzada. Test del parseo de `--tipologia`.
- README de la consola: clave, opción, columnas y regla nueva. Nota en este documento si cambia algo al implementar.

## Procedimiento del reproceso (operativo, con sí explícito contra PRO)

1. Copia de seguridad de `lote-salida.csv`.
2. Entrada `lote-reproceso-entrada.csv` con las 35 filas elegidas (32 GDC que afectan y 3 sin resultado).
3. Quitar de `lote-salida.csv` las 3 filas `respuesta no parseable` (están resueltas y la reanudación no las repite); las 32 `error` se repiten con `--reintentar-errores`.
4. `procesar --entrada lote-reproceso-entrada.csv --salida lote-salida.csv --reintentar-errores --tipologia resumen.documental --colas 2`. Las filas del lote que no están en la entrada se conservan al final del fichero.
5. `consolidar` sobre la salida y comparar con el consolidado anterior.

## Notas de implementación (2026-10-29)

- `DocDeduccion`: cuando el título de `DocIVA` no sostiene la deducción, el código aplica la misma regla completa que para `DocIVA` (cuota, luego tipo, luego confianza, luego orden), no solo confianza y orden. Así lo fijó el plan y lo documenta el README; el efecto práctico es mínimo porque los títulos con deducción casi siempre coinciden con los que traen cuota.
- Implementado en la rama `feature/101191-iva-cedente-sustento`; revisión final sin hallazgos críticos ni importantes.

## Lo que queda al constructor

Nombres de los tests, formato exacto de la cuota en `Sustento` (es-ES), orden de los motivos en `MotivoRevisar`.
