# Diseño corto · IvaCedente, menores diferidos de las revisiones finales (AB#101215)

Fecha: 2026-10-30 · Línea: batch · Rama: `feature/101215-iva-cedente-menores` sobre `main` 6390b3b.

## Intención

Saldar en una tanda corta los ocho menores que las revisiones finales de AB#101191 y AB#101211 dejaron
aparcados, sin reabrir decisiones cerradas y sin cambiar el resultado del lote de PRO del 2026-10-29
(`lote-consolidado-v4.csv`). Cada menor acaba cerrado con commit o descartado con motivo escrito en AB#101215.

## Objetivos, no objetivos, antiobjetivos

- Objetivos: corregir los tres defectos reales del consolidador y de los escritores; cubrir con tests la
  frase `Sustento` tal como sale de `Consolidar`; dejar constancia de los descartes.
- No objetivos: columnas nuevas, cambios en los lectores de CSV, cambios de prompt, cambios en `Revisar`.
- Antiobjetivos: que el consolidado v4 regenerado con esta rama difiera del entregado en algo que no sea una
  discrepancia de cuota que antes se contaba por ruido de coma flotante.

## Enfoques considerados

1. **Arreglos puntuales en `Consolidador` y un helper numérico compartido en `Io`, con un test por punto**
   (elegido): toca cuatro ficheros de producto y dos de tests; es lo que piden los menores.
2. Refactor del consolidador en pasos (recoger, decidir, componer) para que los tests de `Sustento` sean
   unitarios por paso: más de lo que los menores necesitan y abre diff en código estable.
3. Comparar cuotas con tolerancia (epsilon) en vez de redondear: peor, porque la frase de `Sustento` muestra
   dos decimales y el redondeo a céntimo alinea comparación y presentación.

## Decisiones

1. **Object ID repetido dentro de un activo.** En `RecogerDiscrepancias` gana la primera aparición: si el
   Object ID ya está en el diccionario no se sobrescribe. Coherente con `ObjectIdsSoporte` (`Distinct`) y
   con el orden de primera aparición que ya se garantiza. Test: dos filas con el mismo Object ID y valores
   distintos producen una sola discrepancia con los valores de la primera.
2. **Cuotas comparadas a céntimo.** Tanto el motivo `cuotas de IVA distintas` como la discrepancia por cuota
   comparan `Math.Round(cuota, 2, MidpointRounding.AwayFromZero)`. Tests: 1200,001 frente a 1200,004 no
   marca; 1200,00 frente a 1200,01 sí marca y recoge la discrepancia.
3. **Tests de `Sustento` desde `Consolidar`.** Dos tests sobre `fila.Sustento` completo: un activo sin
   discrepancias (frase base con paréntesis de doc, cedente y fecha, y deducción) y un activo con IVA
   contradictorio y cuota distinta (frase con el tramo `; discrepan: ...`). Cubren los dos menores de esta
   familia (sesiones del 29 y del 30).
4. **Formato numérico de los dos CSV.** Un único `CsvUtil.Numero(double?)` con `CultureInfo.InvariantCulture`
   y sin especificador (la cadena más corta que hace ida y vuelta en .NET 8) usado por `SalidaDocumentosCsv`
   y `SalidaActivosCsv`. Punto decimal como hasta ahora; los lectores no cambian. Test: los dos escritores
   emiten el mismo texto para el mismo valor y vacío para nulo.
5. **Descartes, con motivo en AB#101215 al cerrar (escritura con sí explícito):**
   - Orden de motivos de `MotivoRevisar`: ya es determinista (sin resolver, IVA contradictorio, deducción
     contradictoria, cuotas distintas) y está cubierto por `Consolidar_MotivoRevisar_AcumulaEnOrden`.
   - `Tipologia` con la tipología forzada: decisión cerrada del 2026-10-29, la columna escribe lo que devuelve
     el core.
   - Confianza fija 0,95: decisión cerrada del 2026-10-29, limitación del modelo aceptada; el prompt ya pide
     la escala.

## Restricciones

Ediciones en `.cs` sin BOM y con CRLF (reemplazo por script). Un solo `dotnet` a la vez. Build sin
advertencias, `dotnet format --verify-no-changes --include` de los dos proyectos IvaCedente limpio, tests en
verde (152 actuales más los nuevos). Commits con `AB#101215`. Sin llamadas a PRO: `consolidar` sobre la salida
del lote para comprobar que el v4 no cambia salvo por el redondeo.

## Impacto documental

- Técnica: `src/DocumentIA.Batch.IvaCedente/README.md`, apartado `consolidar`: una línea que diga que las
  cuotas se comparan a céntimo. El resto sigue siendo cierto.
- Funcional, arquitectura, explotación: ninguno (sin regla de negocio nueva, sin disparador de ADR, sin
  cambio operativo).

## Queda al constructor

Nombres de los tests, ubicación exacta del helper en `CsvUtil`, orden de los commits (uno por decisión 1-4).
