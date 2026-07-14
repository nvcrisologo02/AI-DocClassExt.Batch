# Reproceso de documentos no completados — DocumentIA.Batch.Classification

- **Fecha:** 2026-07-14
- **App:** `src/DocumentIA.Batch.Classification` (WPF .NET 8, clasificación documental por lotes)
- **Estado:** diseño validado, pendiente de plan de implementación
- **Work items:** AB#99903 (PBI padre) — AB#99904, AB#99905, AB#99906, AB#99907 (tasks)

## Problema

Tras ejecutar un lote de clasificación, algunos documentos quedan en `Error` (fallo HTTP, excepción, orquestación `Failed`/`Terminated`), en `Cancelled` o directamente en `Pendiente` sin llegar a procesarse (por ejemplo, los que esperaban cola cuando se canceló el lote). Hoy la app no ofrece ninguna forma de relanzarlos: hay que cerrar y volver a cargar el lote completo.

## Objetivo

Permitir reprocesar manualmente, desde la sesión actual, los documentos que no obtuvieron resultado tras la ejecución, con posibilidad de seleccionar filas concretas.

## Decisiones de alcance (validadas)

| Decisión | Valor |
|---|---|
| Ámbito | Sesión actual de la app (no historial persistido) |
| Elegibles | `Error` (incl. `RuntimeStatus` `Failed`/`Terminated`), `Cancelled` y `Pendiente` tras haber corrido un lote |
| No elegibles | `OK`, `REVISION`, `VALIDACION_CON_ERRORES`, `BAJA_CONFIANZA`, estados en curso |
| Disparo | Manual, botón nuevo "Reprocesar" |
| ForceReprocess | Respeta el checkbox global existente (sin cambio de contrato) |
| Selección | Columna de checkbox por fila + "seleccionar todos" tri-estado |

## Diseño

### 1. `ClassificationReprocessPolicy` (Services, lógica pura)

Clase estática nueva, análoga a `RetryPolicy` del batch de extracción, sin dependencias de UI:

- `IsReprocessable(ClassificationDocumentItem file, bool hasBatchRun)`:
  - `true` si `Status == "Error"`, o `RuntimeStatus` es `Failed`/`Terminated`, o `Status == "Cancelled"`.
  - `true` si `Status == "Pendiente"` **y** `hasBatchRun == true`.
  - `false` en cualquier otro caso (comparaciones `OrdinalIgnoreCase`).
- `ResetForReprocess(ClassificationDocumentItem file)`: devuelve el documento a `Status = "Pendiente"` y limpia toda la traza del intento anterior:
  - Identificadores y estado durable: `CorrelationId`, `InstanceId`, `RuntimeStatus`, `StatusQueryUri`.
  - Error y fechas: `MensajeError`, `FechaInicio`, `FechaFin`.
  - Salida y auditoría: `OutputJsonPath`, `IdentificacionDocumento`, `TipologiaIdentificada`, `ConfianzaGlobal`, `ResultadoEstado`, `IdentificacionGuid`, `FechaProceso`, `Paginas`.
  - Clasificación: `Tdn1`, `Tdn2`, `Matricula`, `Clasificador`, `FallbackLlm`, `FallbackRazon`, `JustificacionClasificacion`, `ClassificationOnlyOutput`, `TipologiaFamilia`, `TipologiaVersion`, `TipologiaNombre`, `TipologiaMgdcMatricula`, `GdcTipoDocumento`, `GdcSubtipoDocumento`, `GdcSerie`, `GptDescripcion`.
  - Resumen y timeline: `Resumen`, `RecorteAplicado`, `PaginasIncluidas`, `MarkdownGenerado`, `OrigenMarkdown`, `ModeloLlmUsado`, `ActividadActual`, `ActividadesCompletadas`, `ActividadesTotales`, `DuracionTotalMs`, `TimelineActividades`.
  - Proveedores y reutilización: `Proveedor`, `MotivoDescarte`, `ReutilizadaPorDuplicado`, `MensajeReutilizacion`, `DetalleProveedores` (vaciar colección).
  - `IsSelected = false` (la marca se consume al lanzar el reproceso).

### 2. Selección por checkbox

- `ClassificationDocumentItem` gana `IsSelected` (bool, con notificación de cambio; el modelo ya implementa `INotifyPropertyChanged`).
- Columna nueva al inicio del DataGrid principal con checkbox por fila y cabecera tri-estado "seleccionar todos", copiando el patrón de `HistorialView` del batch de extracción (`SelectAllFiles` en el ViewModel operando sobre las filas visibles de `FilesView`).

### 3. Comando "Reprocesar" en `ClassificationMainViewModel`

- `ReprocessCommand` + botón "Reprocesar" junto a "Procesar".
- Flag de sesión `_hasBatchRun`: pasa a `true` al finalizar (o cancelarse) el primer lote de la sesión.
- `CanReprocess()`: `!IsProcessing && BackendUrl` configurada `&& Files.Any(f => IsReprocessable(f, _hasBatchRun))`.
- Resolución de candidatos al pulsar:
  1. Si hay filas con `IsSelected == true` → candidatos = marcadas elegibles; las marcadas no elegibles se omiten y se informa en `ProcessStatus` ("N omitidos por no ser reprocesables").
  2. Si no hay ninguna marcada → candidatos = todos los elegibles.
  3. Sin candidatos → mensaje en `ProcessStatus`, sin acción.
- Ejecución: `ResetForReprocess` sobre cada candidato y lanzamiento por el pipeline existente.

### 4. Refactor del pipeline de proceso

- Extraer de `StartProcessingAsync` un método común `ProcessFilesAsync(IReadOnlyList<ClassificationDocumentItem> files, string operationName)` que encapsula: creación de carpeta de run (`CreateRunFolder`), semáforo por `NumeroColas`, `Task.WhenAll`, gestión de cancelación y mensajes de estado.
- "Procesar" lo invoca con los `Pendiente`; "Reprocesar" con los candidatos reseteados. Cada reproceso genera su propia carpeta de run, como cualquier lote.
- Sin cambios en `BuildIngestRequest`: `ForceReprocess` sigue tomando el valor del checkbox global. Consecuencia conocida y aceptada: con el checkbox apagado, un documento ya procesado antes puede volver como reutilizado por duplicado (MD5).

### 5. KPIs, exportación y errores

- KPIs y exportaciones CSV/Excel no cambian: se recalculan sobre el estado actual de la grilla.
- El reset de campos requiere `FilesView.Refresh()` tras aplicar cambios (patrón ya usado en la app).

## Testing

En `tests/DocumentIA.Batch.Classification.Tests`:

- `ClassificationReprocessPolicyTests`:
  - Matriz de elegibilidad: cada estado (`Pendiente`, `En cola`, `Enviando`, `Processing`, `OK`, `REVISION`, `VALIDACION_CON_ERRORES`, `BAJA_CONFIANZA`, `Error`, `Cancelled`) × `hasBatchRun` (`false`/`true`), más `RuntimeStatus` `Failed`/`Terminated` con `Status` no-error.
  - `ResetForReprocess` limpia todos los campos de traza y deja `Status = "Pendiente"`.
- Tests de ViewModel (estilo `ClassificationMainViewModelSummaryTests`):
  - Resolución de candidatos: marcadas vs. ninguna marcada; omisión de marcadas no elegibles.
  - `CanReprocess` deshabilitado antes del primer lote cuando solo hay `Pendiente`.

## Fuera de alcance

- Reproceso desde el historial persistido (runs de sesiones anteriores).
- Reintentos automáticos post-lote.
- Reproceso de documentos con resultado (`REVISION`, `BAJA_CONFIANZA`, etc.).
- Cambios en el batch de extracción (`DocumentIA.Batch`).
