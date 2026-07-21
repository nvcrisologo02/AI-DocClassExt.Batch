#Requires -Version 7.0
<#
.SYNOPSIS
    Regenera los Excel de Extracción Estafeta a partir de los JSON ya guardados en
    <RootPath>/Results/<carpeta>/json/*.json, SIN reprocesar contra el backend.

.DESCRIPTION
    Reutiliza las mismas funciones que Invoke-ExtraccionEstafeta.ps1 (lib/*.ps1) para
    reconstruir, por tipología, su <codigo>.xlsx y el _resumen.xlsx global.
    Cada escritura se hace con try/catch: si un fichero xlsx está abierto (bloqueado),
    se omite ese y se continúa con el resto, informando al final cuáles fallaron.

    Solo reconstruye a partir de documentos con JSON guardado (los que completaron).
    No regenera _log.csv ni _run.json (dependen de datos de ejecución no persistidos en el JSON).

.EXAMPLE
    pwsh -NoProfile -File .\scripts\extraccion-estafeta\Rebuild-EEExcelFromJson.ps1 -RootPath "H:\Documentia\Test Estafeta"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RootPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$LibDir = Join-Path $PSScriptRoot 'lib'
. (Join-Path $LibDir 'OutputJson.ps1')
. (Join-Path $LibDir 'ExportTable.ps1')
. (Join-Path $LibDir 'ExcelWriter.ps1')
. (Join-Path $LibDir 'Outputs.ps1')

$resultsDir = Join-Path $RootPath 'Results'
if (-not (Test-Path -LiteralPath $resultsDir -PathType Container)) {
    throw "No existe la carpeta Results en: $resultsDir"
}

$kpiRows = New-Object 'System.Collections.Generic.List[object]'
$tipologiaTables = New-Object 'System.Collections.Generic.List[object]'
$written = New-Object 'System.Collections.Generic.List[string]'
$failures = New-Object 'System.Collections.Generic.List[string]'

foreach ($dir in (Get-ChildItem -LiteralPath $resultsDir -Directory | Sort-Object Name)) {
    $jsonDir = Join-Path $dir.FullName 'json'
    if (-not (Test-Path -LiteralPath $jsonDir -PathType Container)) { continue }
    $jsonFiles = @(Get-ChildItem -LiteralPath $jsonDir -File -Filter '*.json' | Sort-Object Name)
    if ($jsonFiles.Count -eq 0) { continue }

    $codigo = Get-EETipologiaCode $dir.Name

    $docs = New-Object 'System.Collections.Generic.List[object]'
    $openDocs = New-Object 'System.Collections.Generic.List[object]'
    $kpiInputs = New-Object 'System.Collections.Generic.List[object]'

    foreach ($jf in $jsonFiles) {
        $jd = Open-EEJsonDocument $jf.FullName
        if ($jd) {
            $docs.Add(@{ HasOutput = $true; Root = $jd.RootElement })
            $openDocs.Add($jd)
            $estadoCalidad = Get-EEPathValue $jd.RootElement 'Resultado.EstadoCalidad'
            $confRaw = Get-EEPathValue $jd.RootElement 'Resultado.ConfianzaGlobal'
            $conf = 0.0; $hasConf = [double]::TryParse($confRaw, [ref]$conf)
            $confVal = $null; if ($hasConf) { $confVal = $conf }
            $kpiInputs.Add(@{ Estado = 'Completado'; EstadoCalidad = $estadoCalidad; ConfianzaGlobal = $confVal })
        } else {
            $docs.Add(@{ HasOutput = $false; Root = [System.Text.Json.JsonElement]::new() })
            $kpiInputs.Add(@{ Estado = 'Error'; EstadoCalidad = ''; ConfianzaGlobal = $null })
        }
    }

    $table = Build-EEExportTable $docs.ToArray()
    foreach ($jd in $openDocs) { $jd.Dispose() }

    $xlsxPath = Join-Path $dir.FullName ($codigo + '.xlsx')
    try {
        Write-EEWorkbook -Path $xlsxPath -Sheets @(@{ Name = 'Resumen'; Headers = $table.Headers; Rows = $table.Rows })
        $written.Add($xlsxPath)
        Write-Host ("  OK   {0} ({1} docs)" -f $xlsxPath, $jsonFiles.Count)
    } catch {
        $failures.Add("$xlsxPath -> $($_.Exception.Message)")
        Write-Warning ("  FALLO {0}: {1}" -f $xlsxPath, $_.Exception.Message)
    }

    $kpiRows.Add((Get-EEKpiRow -Tipologia $codigo -Results $kpiInputs.ToArray()))
    $tipologiaTables.Add(@{ Name = $codigo; Headers = $table.Headers; Rows = $table.Rows })
}

$resumenPath = Join-Path $resultsDir '_resumen.xlsx'
try {
    Write-EEResumenWorkbook -Path $resumenPath -KpiRows $kpiRows.ToArray() -TipologiaTables $tipologiaTables.ToArray()
    $written.Add($resumenPath)
    Write-Host ("  OK   {0}" -f $resumenPath)
} catch {
    $failures.Add("$resumenPath -> $($_.Exception.Message)")
    Write-Warning ("  FALLO {0}: {1}" -f $resumenPath, $_.Exception.Message)
}

Write-Host ""
Write-Host ("Regenerados {0} xlsx en {1}" -f $written.Count, $resultsDir)
if ($failures.Count -gt 0) {
    Write-Warning ("No se pudieron escribir {0} (cierra el Excel si sigue abierto y vuelve a lanzar):" -f $failures.Count)
    $failures | ForEach-Object { Write-Warning ("  {0}" -f $_) }
    exit 1
}
