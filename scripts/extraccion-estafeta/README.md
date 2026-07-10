# Extracción Estafeta

Script PowerShell 7 que fuerza la extracción de un lote de PDFs organizados por subcarpeta de
tipología contra el backend de DocumentIA y genera un Excel por tipología (formato batch),
`_resumen.xlsx`, `_log.csv` y `_run.json` en `<RootPath>/Results/`.

## Requisitos

- PowerShell 7+ (`pwsh`).
- Function key de PROD en la variable de entorno `DOCUMENTIA_FUNCTION_KEY` (o parámetro `-FunctionKey`).

## Estructura de entrada esperada

El `RootPath` debe contener una subcarpeta por tipología (primer nivel), con los PDFs a
procesar directamente dentro de cada una (no se recorren subcarpetas anidadas):

```
<RootPath>/
  cera.44_vado/
    doc1.pdf
    doc2.pdf
  comu.04/
    doc3.pdf
```

El nombre de la subcarpeta se mapea a código de tipología sustituyendo `_` por `.`
(p. ej. `cera.44_vado` → `cera.44.vado`).

## Uso

Ejecución real contra el backend:

```powershell
$env:DOCUMENTIA_FUNCTION_KEY = "<clave>"
pwsh -File Invoke-ExtraccionEstafeta.ps1 -RootPath "C:\docs\lote" -BackendUrl "https://<func-prod>.azurewebsites.net"
```

Parámetros opcionales: `-MaxParallel` (por defecto 2), `-MaxRetries` (por defecto 1),
`-TimeoutSeconds` (por defecto 600), `-PollIntervalSeconds` (por defecto 5),
`-SkipGdcUpload` (por defecto `$true`), `-ConfigPath` (fichero JSON con overrides de
configuración).

Vista previa sin llamar al backend (lista tipologías/PDFs descubiertos y no escribe nada en
disco):

```powershell
pwsh -File Invoke-ExtraccionEstafeta.ps1 -RootPath "C:\docs\lote" -BackendUrl "https://x" -DryRun
```

## Salidas

Todo se escribe bajo `<RootPath>/Results/`:

- `<subcarpeta>/json/<fichero>.pdf.json`: salida cruda del backend por documento.
- `<subcarpeta>/<codigo_tipologia>.xlsx`: hoja `Resumen` con una fila por documento procesado
  de esa tipología (mismo formato que el batch existente).
- `_resumen.xlsx`: hoja `KPIs` (total, completados, error, revisión, confianza media por
  tipología) más una hoja por tipología con su tabla de resultados.
- `_log.csv`: log plano de todos los documentos procesados (tipología, fichero, estado,
  instanceId, correlationId, inicio, fin, duración, mensaje).
- `_run.json`: metadatos de la ejecución (backend, root, concurrencia, tipologías, totales).

## Notas

- El script **no** envía `assetResolver` en el request de ingest: el orquestador lo ejecuta
  donde la tipología lo tenga configurado (no es una decisión de este script).
- `skipGDCUpload` por defecto `true` (extracción de prueba; no sube al GDC).
- El procesamiento de PDFs dentro de cada tipología es paralelo con throttle `-MaxParallel`
  (por defecto 2 llamadas concurrentes al backend).
- Ante fallo transitorio se reintenta hasta `-MaxRetries` veces; un error HTTP 401 aborta el
  reintento de ese documento de inmediato (se registra como error, no se reintenta la clave).
