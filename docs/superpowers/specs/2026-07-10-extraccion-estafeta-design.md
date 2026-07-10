# Extracción Estafeta — Script de extracción forzada por tipología (Diseño)

> Fecha: 2026-07-10
> Repo: DocumentIA.Batch
> Estado: aprobado (pendiente de plan de implementación)

## 1. Propósito

Script **PowerShell 7** standalone ("Extracción Estafeta") que, dada una carpeta raíz con una
subcarpeta por tipología, procesa cada PDF **forzando la extracción** contra el backend
**PROD**, deja que el **orquestador decida si ejecuta AssetResolver** según la configuración de
cada tipología, y genera **un Excel por tipología** (con el mismo formato que produce el batch
de extracción), un **Excel resumen global** y un **log** de ejecución.

La tipología es conocida a priori (una por subcarpeta), por lo que **no hay clasificación**:
se envía `expectedType` y se fuerza el reprocesado.

### Contexto de las subcarpetas

La raíz contiene una subcarpeta por tipología, nombrada con el código de tipología. Algunas
usan guion bajo donde el identificador real usa punto:

```
cera.15  cera.16  cera.44_basura  cera.44_vado  cera.46
comu.04  comu.07  deac.07  docj.10  docj.19
```

El `expectedType` se obtiene del nombre de carpeta reemplazando `_` por `.`
(`cera.44_vado` → `cera.44.vado`).

## 2. Referencias del comportamiento a replicar

- Contrato de ingest y polling: `src/DocumentIA.Batch/Services/DocumentIaBackendClient.cs`
  (endpoints `/api/ingest` con fallback `/api/IngestDocument`, multipart `metadata`+`file`,
  header `x-functions-key`, normalización de `statusQueryUri`, polling de durable status).
- Construcción del request: `src/DocumentIA.Batch/ViewModels/MainViewModel.cs`
  (`BuildIngestRequest`: `expectedType`, `forceReprocess`, `skipDuplicateCheck`, `extraction`
  auto, `skipGDCUpload`).
- Formato del Excel: `src/DocumentIA.Batch/Services/BatchExportRows.cs`
  (cabeceras fijas + dinámicas + sufijo) y
  `src/DocumentIA.Batch/Services/BatchExcelExportService.cs`
  (escritura xlsx vía zip + OpenXML `inlineStr`, sin dependencias externas).
- Resolución de AssetResolver (repo `documento-ia-clasificacion-mvp`):
  `src/backend/DocumentIA.Functions/Orchestrators/DocumentProcessOrchestrator.cs:1864`

  ```csharp
  var assetResolverEnabled = entrada.Instrucciones.AssetResolver?.Enabled
      ?? tipologiaResuelta.AssetResolverEnabled;
  ```

  Si el request **no** incluye el bloque `assetResolver`, el orquestador cae en la
  configuración de la tipología. Por eso el script **no envía** `assetResolver` y no necesita
  consultar BBDD ni autenticarse contra Entra.

## 3. Parámetros / configuración

El script acepta parámetros y, opcionalmente, un fichero de config JSON. Los parámetros pisan
la config del fichero.

| Parámetro | Req. | Default | Descripción |
|-----------|------|---------|-------------|
| `-RootPath` | sí | — | Carpeta que contiene las subcarpetas de tipología |
| `-BackendUrl` | sí | — | Base PROD, p.ej. `https://<func-prod>.azurewebsites.net` |
| `-FunctionKey` | sí | env `DOCUMENTIA_FUNCTION_KEY` | Clave de función PROD (parámetro o variable de entorno; nunca hardcode) |
| `-ConfigPath` | no | — | Fichero JSON con cualquiera de los valores anteriores |
| `-MaxParallel` | no | `2` | Ingests concurrentes |
| `-MaxRetries` | no | `1` | Reintentos en fallo transitorio (5xx / timeout) |
| `-TimeoutSeconds` | no | `600` | Timeout de polling por documento |
| `-PollIntervalSeconds` | no | `5` | Intervalo de polling |
| `-SkipGdcUpload` | no | `true` | No subir a GDC (extracción de prueba) |
| `-DryRun` | no | `false` | Lista qué haría sin llamar al backend |

## 4. Flujo

### 4.1 Descubrimiento

1. Enumerar subcarpetas de `-RootPath` (solo primer nivel).
2. Por subcarpeta: `expectedType` = nombre con `_`→`.`.
3. Por subcarpeta: enumerar `*.pdf` **solo en el primer nivel** de esa subcarpeta (no recursivo).

### 4.2 Procesado por documento

Por cada PDF:

1. Construir request de ingest (multipart `metadata` + `file`):
   - `instrucciones.expectedType = <código>`
   - `instrucciones.forceReprocess = true`
   - `instrucciones.skipDuplicateCheck = true`
   - `instrucciones.classificationOnly = false`
   - `instrucciones.skipGDCUpload = <-SkipGdcUpload>`
   - `instrucciones.classification = { provider: auto, model: auto }`
   - `instrucciones.extraction = { provider: auto, model: auto }`
   - **sin** `instrucciones.assetResolver`
   - `documento = { name, content.base64 }` (se envía como multipart; metadata sin base64)
   - `trazabilidad = { correlationId: <guid>, submittedBy: "ExtraccionEstafeta" }`
2. POST a `/api/ingest`; si 404, fallback a `/api/IngestDocument`. Header `x-functions-key`.
3. Leer `statusQueryUri` de la respuesta; normalizarlo.
4. Polling de durable status hasta `Completed` / `Failed` / timeout, respetando
   `-PollIntervalSeconds` y `-TimeoutSeconds`.
5. `Completed` → guardar el `output` JSON en disco y registrar la fila del documento.
6. `Failed` / timeout / excepción → registrar en log; la fila del Excel queda con las columnas
   de extracción vacías (igual que hace el batch cuando no hay output).
7. Reintento transitorio (`-MaxRetries`) en 5xx o timeout de red. 401 → mensaje claro sobre la
   function key y aborto (clave inválida afecta a todo el lote).

### 4.3 Concurrencia

Throttle global de `-MaxParallel` (default 2) ingests concurrentes mediante
`ForEach-Object -Parallel -ThrottleLimit`. Requiere **PowerShell 7+**.

## 5. Generación de Excel (formato batch)

Se porta a PowerShell la lógica de `BatchExportRows` + `BatchExcelExportService`:

- **Cabeceras fijas (prefijo):** `Identificacion.Documento`, `Identificacion.Guid`,
  `Identificacion.Tipologia`, `Identificacion.TipologiaFamilia`, `Identificacion.TipologiaVersion`,
  `Identificacion.FechaProceso`, `Integridad.CRC32`, `Integridad.SHA256`, `Integridad.MD5`,
  `Integridad.IdActivo`, `Integridad.IdActivoEntrada`, `Integridad.IdActivoCambiado`.
- **Cabeceras dinámicas (por tipología):** para cada campo presente en `DatosExtraidos` (unión
  de los documentos de esa tipología, orden de aparición, case-insensitive):
  `DatosExtraidos.<campo>` y `DetalleEjecucion.Extraccion.ConfianzaPorCampo.<campo>`.
- **Cabeceras fijas (sufijo):** `DetalleEjecucion.Extraccion.Modelo`,
  `DetalleEjecucion.Extraccion.CamposConDuda`, `DetalleEjecucion.AssetResolver.ActivosAAII`,
  `DetalleEjecucion.AssetResolver.ActivosAACC`, `DetalleEjecucion.AssetResolver.Mensaje`,
  `DetalleEjecucion.Prompt`, `Resultado.Estado`, `Resultado.MensajeError`,
  `Resultado.ConfianzaGlobal`, `Resultado.EstadoCalidad`, `Resultado.ConfianzaClasificacion`,
  `Resultado.ConfianzaExtraccion`, `Resultado.ConfianzaValidacion`,
  `Resultado.ReutilizadaPorDuplicado`, `Resultado.MensajeReutilizacion`.
- **Una fila por documento.** Valores obtenidos por navegación de path sobre el output JSON
  (equivalente a `GetPathValue`). Documentos sin output → fila con columnas vacías.
- Escritura xlsx mediante paquete OOXML mínimo (`[Content_Types].xml`, `_rels`, `workbook.xml`,
  `worksheets/sheetN.xml`) con celdas `inlineStr`, **sin módulos externos**, replicando el
  writer del batch para garantizar formato idéntico.

## 6. Salidas — `<RootPath>/Results/`

```
Results/
  <carpeta_tipologia>/            (nombre original de la subcarpeta, p.ej. cera.44_vado)
    json/<doc>.json              output JSON crudo por documento
    <codigo>.xlsx                Excel de la tipología (formato batch); hoja "Resumen"
  _resumen.xlsx                  Excel resumen global
  _log.csv                       estado por documento
  _run.json                      metadatos de la ejecución
```

- **`_resumen.xlsx`** (resumen global): primera hoja **"KPIs"** con una fila por tipología
  (procesados, completados, error, revisión, confianza media) y, a continuación, **una hoja por
  tipología** replicando su tabla completa.
- **`_log.csv`**: por documento → tipología, fichero, estado final (`Completed`/`Error`/
  `Timeout`/`Cancelled`), `instanceId`, `correlationId`, inicio, fin, duración, mensaje.
- **`_run.json`**: parámetros efectivos (sin la function key), timestamps de inicio/fin, y
  totales agregados.

## 7. Manejo de errores

- Parámetros obligatorios ausentes o `-RootPath` inexistente → fallo inmediato con mensaje.
- Fallo de un documento **no** detiene el lote: se registra en `_log.csv` y su fila del Excel
  queda con columnas de extracción vacías.
- `401 Unauthorized` → mensaje claro indicando revisar la function key; aborta el lote.
- Fallos transitorios (5xx, timeouts de red) → hasta `-MaxRetries` reintentos con backoff simple.
- Timeout de proceso por documento (`-TimeoutSeconds`) → se marca `Timeout` y se continúa.

## 8. Testing

Funciones puras aisladas y testeadas con **Pester**, usando un output JSON real como fixture:

1. **Mapeo carpeta → código** (`_`→`.`).
2. **Descubrimiento de campos `DatosExtraidos`** y **construcción de cabeceras** (prefijo +
   dinámicas + sufijo) a partir de un conjunto de outputs.
3. **Extracción de valores por path** sobre el output JSON (incluyendo casos de path ausente →
   vacío).
4. **Construcción de fila** completa para un documento.

Modo **`-DryRun`**: imprime subcarpetas → códigos, PDFs detectados y URL destino, sin llamar al
backend. Sirve como validación del descubrimiento antes de tocar PROD.

## 9. Ubicación (repo DocumentIA.Batch)

- Spec: `docs/superpowers/specs/2026-07-10-extraccion-estafeta-design.md` (este fichero).
- Plan: `docs/superpowers/plans/` (se generará tras aprobar el spec).
- Script y utilidades: `scripts/extraccion-estafeta/`
  - `Invoke-ExtraccionEstafeta.ps1` — entrada principal.
  - módulos/funciones auxiliares (cliente ingest, writer xlsx, construcción de tabla) según se
    detalle en el plan.
  - `tests/` — pruebas Pester + fixtures de output JSON.

## 10. Fuera de alcance (YAGNI)

- Clasificación (la tipología es conocida).
- Lectura de BBDD / autenticación Entra (el orquestador decide AssetResolver por config).
- Subida a GDC por defecto (desactivada; configurable).
- Reanudación/checkpoint de lotes interrumpidos (una ejecución = un lote completo).
