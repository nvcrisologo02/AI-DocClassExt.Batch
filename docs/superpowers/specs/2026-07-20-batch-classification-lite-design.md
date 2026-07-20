# Batch Classification Lite — Diseño

**Fecha:** 2026-07-20
**Estado:** Aprobado (pendiente de plan de implementación)
**Ámbito:** Nueva herramienta de clasificación masiva simplificada, hasta 100.000 documentos por ejecución, distribuida como ejecutable autocontenido e independiente del cliente principal.

---

## 1. Contexto y decisiones previas

La herramienta actual `DocumentIA.Batch.Classification` (WPF .NET 8) ya clasifica de forma masiva contra DocumentIA (ingest asíncrono + polling del status de Durable Functions), pero:

- No tiene catálogo de entornos (solo `BackendUrl`/`FunctionKey` en texto plano).
- No tiene histórico persistente de "ya procesado" (el SQLite `BatchHistorialService` solo existe en la app de extracción).
- El detalle solo muestra el response; el request nunca se persiste.
- El reproceso de errores es manual y limitado a la sesión actual.
- Muestra Summary, justificación del camino de clasificación y traza de actividades, que el Lite elimina.

### Decisiones tomadas con el usuario

| # | Decisión | Elección |
|---|----------|----------|
| 1 | Ubicación del código | Proyecto nuevo `DocumentIA.Batch.ClassificationLite` en la misma solución, reutilizando `DocumentIaBackendClient`. La app Classification actual queda intacta. |
| 2 | Polling | Adaptativo por documento: rápido al inicio, backoff hasta el `Polling Interval` configurado (60s). |
| 3 | Entornos | Solo PRO precargado en la config distribuida; DEV/PRE se añaden a mano desde la configuración. |
| 4 | Histórico | SQLite local por usuario/máquina en `%LocalAppData%`. El dedup del backend actúa de red de seguridad entre usuarios. |
| 5 | Recuperación | Al arrancar con ejecución incompleta: diálogo Continuar/Descartar. Los docs en vuelo se re-enganchan vía `StatusQueryUri`. |
| 6 | Ejecución desatendida | Sí: se contempla dejar la app corriendo fuera de horario laboral para alcanzar el objetivo diario. |
| 7 | Request en detalle | Se persiste el request **sin** el base64 del fichero (placeholder con tamaño). |
| 8 | Force Reprocess | Ignora el histórico local **y** envía `forceReprocess=true` al backend. |
| 9 | Arquitectura interna | Motor sin UI con SQLite como fuente de verdad; la UI solo observa. |

---

## 2. Arquitectura general

```text
DocumentIA.Batch.sln
├── src/DocumentIA.Batch                      (librería compartida existente: DocumentIaBackendClient, DTOs)
├── src/DocumentIA.Batch.Classification       (herramienta actual, intacta)
├── src/DocumentIA.Batch.ClassificationLite   (NUEVO — WPF net8.0-windows, WinExe)
└── tests/DocumentIA.Batch.ClassificationLite.Tests  (NUEVO)
```

- Referencia solo a `DocumentIA.Batch` (cliente HTTP + DTOs de ingest/polling ya probados). **No** referencia `DocumentIA.Batch.Classification` ni `DocumentIA.Batch.Markdown`.
- El exportador se reimplementa slim dentro del Lite siguiendo el patrón OOXML manual existente (ZIP + `XmlWriter`, sin dependencias nuevas).

### Capas internas del Lite

| Capa | Contenido | Dependencias |
|------|-----------|--------------|
| `Data/` | Repositorio SQLite (Microsoft.Data.Sqlite + Dapper), esquema versionado con `PRAGMA user_version`, WAL | Ninguna de UI |
| `Engine/` | `FolderScanner`, `LiteEngine` (scheduler), `AdaptivePollingStrategy`, `RetryCoordinator` | `Data/` + `DocumentIaBackendClient` |
| `Services/` | `LiteConfigService` (config.json), `LiteExportService` (Excel/CSV) | `Data/` |
| `ViewModels/` + `Views/` | Ventana única + diálogos de Configuración, Detalle y Reanudación | Observa eventos del motor |

El motor no conoce WPF: es testeable sin ventana y las pausas/cancelaciones son limpias (estado en BD).

---

## 3. Modelo de datos SQLite

**Ubicación:** `%LocalAppData%\DocumentIA.BatchLite\lite.db` — permanente, nunca se borra automáticamente. Modo WAL.

### Tabla `Executions`

| Campo | Tipo | Notas |
|-------|------|-------|
| `ExecutionId` | TEXT PK | GUID |
| `RootPath` | TEXT | Carpeta origen (local o UNC) |
| `IncludeSubfolders` | INTEGER | 0/1 |
| `Status` | TEXT | Running / Paused / Completed / Cancelled / Aborted |
| `StartedAt`, `CompletedAt` | TEXT | ISO 8601 UTC |
| `ConfigSnapshotJson` | TEXT | Config vigente al lanzar (trazabilidad) |

### Tabla `Documents`

| Campo | Tipo | Notas |
|-------|------|-------|
| `Id` | INTEGER PK AUTOINCREMENT | |
| `ExecutionId` | TEXT FK | |
| `FileName`, `FullPath` | TEXT | |
| `FileSize` | INTEGER | bytes |
| `LastModifiedUtc` | TEXT | ISO 8601 UTC |
| `Status` | TEXT | Pending / InFlight / Succeeded / Error / DefinitiveError / SkippedHistory / Cancelled |
| `BatchNumber` | INTEGER | Lote interno |
| `RetryCount` | INTEGER | |
| `InstanceId`, `StatusQueryUri` | TEXT | Para re-enganche tras caída |
| `TDN1`, `TDN2` | TEXT | |
| `Confidence` | REAL | |
| `Pages` | INTEGER | |
| `PagesIncluded` | TEXT | Recuento o rango, según lo que devuelva el `output` (`PaginasIncluidas`) |
| `ProcessDate` | TEXT | |
| `DurationMs` | INTEGER | |
| `RequestJson` | TEXT | Sin base64 (placeholder `"<base64 omitido, N bytes>"`) |
| `ResponseJson` | TEXT | `output` completo de Durable Functions |
| `ErrorMessage` | TEXT | |

**Índice:** `(FileName, FileSize, LastModifiedUtc)`.

### Identificación de "ya procesado"

- Clave: `FileName + FileSize + LastModifiedUtc` (**sin ruta**, según spec funcional): el mismo fichero copiado o movido a otra carpeta se omite igualmente.
- Chequeo: existe fila `Succeeded` con esa clave en cualquier ejecución anterior.
- Los omitidos se insertan como `SkippedHistory` **copiando** TDN1/TDN2/Confidence del resultado histórico: cada ejecución es autocontenida para grid y export.
- `Force Reprocess = TRUE` desactiva el chequeo local y añade `forceReprocess=true` al request.

---

## 4. Motor de procesamiento

### Escaneo progresivo

- `Directory.EnumerateFiles` en background (soporta UNC), extensión PDF (como la herramienta actual).
- Inserción en SQLite por transacciones de ~500 filas; el chequeo de histórico se hace durante la inserción.
- Un fichero ilegible genera error de esa fila, nunca aborta el escaneo.
- Drag & drop de ficheros o carpetas alimenta el mismo flujo.

### Lotes internos y ciclo por documento

- Partición en lotes de `Internal Batch Size` (default 1000). El usuario percibe una única ejecución.
- Por lote: semáforo de `Parallel Queries` (default 2) sobre el ciclo completo por documento:
  1. Leer fichero → base64.
  2. Construir `IngestRequest` según configuración (`classificationOnly`, `nivelClasificacion`, `provider`, `model`, `forceReprocess`); fijos: `skipGdcUpload=true` y `maxPagesForClassificationOnly=10` (interno, no expuesto).
  3. `POST` ingest → guardar `InstanceId`, `StatusQueryUri` y `RequestJson` (sin base64) → estado `InFlight`.
  4. Polling adaptativo hasta estado terminal.
  5. Actualizar fila: `Succeeded` (TDN1/TDN2/Confidence/Pages/PagesIncluded/tiempos desde el `output`) o `Error`.

### Polling adaptativo

- Cadencia por documento: 3s, 5s, 10s, 20s, 30s, y después cada `Polling Interval` (default 60s).
- Timeout por documento: 30 min → `Error` (reintentable).
- Detecta docs rápidos sin castigar el status endpoint en docs lentos; el throughput depende de la duración real de clasificación, no del intervalo.

### Reintentos — estrategia Batch Completion

```text
Procesar lote completo
    ↓
Identificar Error (HTTP fallido, Durable Failed/Terminated, timeout)
    ↓
Reprocesar solo los Error (mismo semáforo)
    ↓
Repetir hasta MaxRetries (default 3)
    ↓
Restantes → DefinitiveError
    ↓
Siguiente lote
```

Sin reintentos inmediatos por documento. `RetryCount` se persiste.

### Pausar / Reanudar / Cancelar

- **Pausar:** no se lanzan documentos nuevos; los en vuelo terminan su polling. Estado `Paused` en BD.
- **Reanudar:** continúa desde `Pending` + pasadas de reintento pendientes.
- **Cancelar:** detiene lanzamientos y polling; en vuelo → `Cancelled`. El backend puede completar esas orquestaciones por su cuenta (documentado; sin efectos secundarios porque `skipGdcUpload=true`).

### Protección de ejecuciones desatendidas

- Durante ejecución activa: `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` para impedir la suspensión del equipo (la pantalla puede apagarse). Se libera al terminar/pausar.

### Manejo de errores destacado

- **Rotación de function key (401 en cadena):** ante N errores 401 consecutivos, la ejecución se auto-pausa con aviso al usuario en vez de quemar reintentos de todo el lote.
- **Fichero modificado entre escaneo y proceso:** se usa la metadata del momento de la lectura.
- **Corte de red con UNC:** error por fichero, reintentable en la pasada de reintentos.

---

## 5. UI

### Ventana principal (única, minimalista)

- **Barra de acciones:** Seleccionar carpeta (con check de subcarpetas), Configuración, Ejecutar, Pausar, Reanudar, Cancelar, Exportar. Drag & drop sobre toda la ventana.
- **Contadores** (agregados SQL, no conteo de filas del grid): Total encontrados, Pendientes, En ejecución, Procesados OK, Errores definitivos, Omitidos por histórico.
- **Grid** virtualizado, columnas exactas: `FileName`, `Status`, `PagesIncluded`, `Pages`, `TDN1`, `TDN2`, `Confidence`, `ProcessDate`, `TotalDurationMs`. Filtro simple: texto sobre FileName + combo de Status.
- Refresco de UI por lotes (~1s) vía dispatcher: la ventana nunca se bloquea con 100k filas.
- **Eliminado por diseño:** Summary, justificación del camino de clasificación, Activity Trace, logs técnicos, información de routing, explicaciones del clasificador.

### Detalle (doble click en fila)

- Request enviada (JSON, base64 sustituido por placeholder).
- Response recibida (JSON completo del `output`).
- Cabecera: TDN1, TDN2, Confidence, ProcessDate, DurationMs.

### Configuración (una modal, 4 grupos)

| Grupo | Opciones | Defaults |
|-------|----------|----------|
| Procesamiento | Environment, Parallel Queries, Internal Batch Size, Polling Interval | PRO, 2, 1000, 60s |
| Clasificación | Classification Level, Provider, Model, Only Classification | TDN1_TDN2, AUTO, AUTO, TRUE |
| Reprocesado | Force Reprocess, Max Retries | FALSE, 3 |
| Históricos | Skip Already Processed | TRUE |

- `Environment`: combo sobre catálogo editable (nombre + URL + function key). Distribución con solo PRO precargado.
- `Provider`/`Model`: mismos valores editables que la herramienta actual (`auto`, `hybrid`, `hybrid-rules-gpt-di`, `hybrid-rules-di-gpt`, `hybrid-tdn`, `rules`, `gpt`, `di`; model texto libre).
- Retry Strategy fija: Batch Completion (no seleccionable).
- Persistencia: `config.json` junto al exe. La BD siempre en `%LocalAppData%`.

---

## 6. Recuperación ante fallos

Al arrancar, si existe `Execution` en Running/Paused:

```text
Diálogo: "Hay una ejecución incompleta (X pendientes, Y en vuelo, Z completados).
          ¿Continuar o descartar?"

Continuar → InFlight: re-enganche por StatusQueryUri (sin reenviar documento)
            Pending: flujo normal
Descartar → Execution marcada Aborted (filas conservadas como histórico)
```

Cubre cierre inesperado, caída de la app y reinicio del equipo.

---

## 7. Exportación

- Formatos: **Excel** (OOXML manual: ZIP + `XmlWriter`) y **CSV** (`;`, BOM UTF-8), sin dependencias nuevas.
- Campos exportados = columnas del grid: `FileName`, `Status`, `PagesIncluded`, `Pages`, `TDN1`, `TDN2`, `Confidence`, `ProcessDate`, `TotalDurationMs`.
- Exporta la ejecución completa por defecto (incluidos `SkippedHistory` con sus valores históricos); opción de exportar solo el filtro activo.

---

## 8. Distribución

- Script versionado `scripts/publish-classification-lite.ps1`:
  `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
- Resultado: carpeta con `DocumentIA.Batch.ClassificationLite.exe` + `config.json` (solo PRO precargado). Sin instalador: se copia la carpeta.

---

## 9. Testing

Unit tests del motor (sin UI):

- Partición en lotes y avance entre lotes.
- `RetryCoordinator`: flujo batch-completion completo (caso de la spec: 1000 → 20 → 5 → 1 → definitivo).
- Calendario del polling adaptativo (secuencia 3/5/10/20/30/60s y timeout).
- Dedup del scanner: clave triple, `SkippedHistory` con copia de valores, `Force Reprocess`.
- Repositorio SQLite contra BD temporal (esquema, transiciones de estado, agregados de contadores, re-enganche).
- Constructor de request (base64 eliminado, flags correctos).
- Escritores Excel/CSV (9 columnas, escaping).

Smoke E2E manual con lote pequeño contra PRO (la clasificación en dev está degradada por la configuración del DI, ya diagnosticado).

---

## 10. Riesgos técnicos

| Riesgo | Mitigación |
|--------|------------|
| Crecimiento de BD por `ResponseJson` (~0,5-2 GB / 100k docs) | Asumido; purga/VACUUM de ejecuciones antiguas como mejora futura |
| Cortes de red con carpeta UNC | Error por fichero reintentable; el escaneo no aborta |
| Rotación de function key en ejecución larga | Auto-pausa ante 401 en cadena, con aviso |
| Equipo suspendido en desatendido | `SetThreadExecutionState` + recomendación de plan de energía |
| Grid con 100k filas | Virtualización + refresco por lotes + contadores desde SQL |
| Ficheros modificados durante la ejecución | Metadata re-leída al procesar |
| Dos documentos distintos con mismo nombre+tamaño+fecha | Riesgo residual aceptado (clave según spec funcional, sin ruta) |

---

## 11. Recomendaciones operativas (objetivo 4.000-5.000 docs/día)

- `Parallel Queries = 2` constante: convivencia con usuarios interactivos de PRO.
- Lanzar el lote grande al final de la jornada y dejar la ejecución desatendida: con ~20-40s por documento y 2 en vuelo → ~4.300-8.600 docs/24h; el objetivo se alcanza sin subir paralelismo.
- `Skip Already Processed = TRUE` permite relanzar la misma carpeta cada día sin coste: solo procesa lo nuevo.
- El polling adaptativo mantiene baja la presión sobre el status endpoint en documentos lentos.
- Plan de energía del equipo: impedir suspensión (la app además lo fuerza durante la ejecución).
- Para cargas urgentes puntuales, acordar con el equipo de DocumentIA subir `Parallel Queries` a 3-4 temporalmente.

---

## 12. Fuera de alcance

- Subida a GDC (`skipGdcUpload=true` siempre, como la herramienta actual).
- Selección de tipología esperada (`ExpectedType` vacío siempre).
- Extracción de datos (solo clasificación).
- Histórico compartido entre usuarios/máquinas.
- Instalador (MSI/ClickOnce).
- Purga automática de la BD.
