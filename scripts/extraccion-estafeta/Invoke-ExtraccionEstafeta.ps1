#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RootPath,
    [string]$BackendUrl,
    [string]$FunctionKey,
    [string]$ConfigPath,
    [int]$MaxParallel = 2,
    [int]$MaxRetries = 1,
    [int]$TimeoutSeconds = 600,
    [int]$PollIntervalSeconds = 5,
    [bool]$SkipGdcUpload = $true,
    [switch]$DryRun,
    [switch]$LibOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$LibDir = Join-Path $PSScriptRoot 'lib'
. (Join-Path $LibDir 'OutputJson.ps1')
. (Join-Path $LibDir 'ExportTable.ps1')
. (Join-Path $LibDir 'ExcelWriter.ps1')
. (Join-Path $LibDir 'IngestClient.ps1')
. (Join-Path $LibDir 'Outputs.ps1')

function Resolve-EEConfig {
    param([hashtable]$Params)
    $cfg = @{
        RootPath = $null; BackendUrl = $null; FunctionKey = $null
        MaxParallel = 2; MaxRetries = 1; TimeoutSeconds = 600; PollIntervalSeconds = 5; SkipGdcUpload = $true
    }
    if ($Params.ContainsKey('ConfigPath') -and $Params.ConfigPath -and (Test-Path $Params.ConfigPath)) {
        $fileCfg = Get-Content $Params.ConfigPath -Raw | ConvertFrom-Json
        foreach ($p in $fileCfg.PSObject.Properties) { $cfg[$p.Name] = $p.Value }
    }
    foreach ($key in @('RootPath','BackendUrl','FunctionKey','MaxParallel','MaxRetries','TimeoutSeconds','PollIntervalSeconds','SkipGdcUpload')) {
        if ($Params.ContainsKey($key) -and $null -ne $Params[$key] -and "$($Params[$key])" -ne '') { $cfg[$key] = $Params[$key] }
    }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.FunctionKey) -and $env:DOCUMENTIA_FUNCTION_KEY) {
        $cfg.FunctionKey = $env:DOCUMENTIA_FUNCTION_KEY
    }
    return $cfg
}

function Get-EEWorkItems {
    param([Parameter(Mandatory)][string]$RootPath)
    if (-not (Test-Path -LiteralPath $RootPath -PathType Container)) {
        throw "RootPath no existe o no es carpeta: $RootPath"
    }
    $items = New-Object 'System.Collections.Generic.List[object]'
    foreach ($dir in (Get-ChildItem -LiteralPath $RootPath -Directory | Sort-Object Name)) {
        if ($dir.Name -eq 'Results') { continue }
        $pdfs = @(Get-ChildItem -LiteralPath $dir.FullName -File -Filter '*.pdf' | Sort-Object Name | ForEach-Object { $_.FullName })
        $items.Add([pscustomobject]@{
            Tipologia  = Get-EETipologiaCode $dir.Name
            FolderName = $dir.Name
            FolderPath = $dir.FullName
            Files      = $pdfs
        })
    }
    return $items.ToArray()
}

function Invoke-EEMain {
    param([hashtable]$Params)

    $cfg = Resolve-EEConfig -Params $Params
    if ([string]::IsNullOrWhiteSpace([string]$cfg.RootPath))   { throw "Falta -RootPath." }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.BackendUrl)) { throw "Falta -BackendUrl." }
    $items = Get-EEWorkItems -RootPath $cfg.RootPath
    $resultsDir = Join-Path $cfg.RootPath 'Results'

    Write-Host "Extraccion Estafeta"
    Write-Host "  Backend : $($cfg.BackendUrl)"
    Write-Host "  Root    : $($cfg.RootPath)"
    Write-Host "  Salida  : $resultsDir"
    foreach ($it in $items) { Write-Host ("  - {0} ({1} PDFs) -> {2}" -f $it.FolderName, $it.Files.Count, $it.Tipologia) }

    if ($Params.ContainsKey('DryRun') -and $Params['DryRun']) {
        if ([string]::IsNullOrWhiteSpace([string]$cfg.FunctionKey)) { Write-Warning "Sin function key (se pediria en ejecucion real)." }
        Write-Host "DryRun: no se llama al backend."
        return
    }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.FunctionKey)) { throw "Falta FunctionKey (parametro o env DOCUMENTIA_FUNCTION_KEY)." }

    $libDir = $LibDir
    $allLog = New-Object 'System.Collections.Generic.List[object]'
    $kpiRows = New-Object 'System.Collections.Generic.List[object]'
    $tipologiaTables = New-Object 'System.Collections.Generic.List[object]'

    foreach ($item in $items) {
        if ($item.Files.Count -eq 0) { continue }
        Write-Host "Procesando $($item.FolderName) ($($item.Files.Count) PDFs)..."
        $jsonDir = Join-Path (Join-Path $resultsDir $item.FolderName) 'json'
        New-Item -ItemType Directory -Path $jsonDir -Force | Out-Null

        $cfgLocal = $cfg
        $tipologiaLocal = $item.Tipologia
        $procResults = $item.Files | ForEach-Object -ThrottleLimit ([int]$cfg.MaxParallel) -Parallel {
            $file = $_
            . (Join-Path $using:libDir 'IngestClient.ps1')
            $c = $using:cfgLocal
            $expected = $using:tipologiaLocal
            $jsonDir = $using:jsonDir
            $name = [System.IO.Path]::GetFileName($file)
            $correlationId = [guid]::NewGuid().ToString()
            $inicio = Get-Date
            $estado = 'Error'; $instanceId = ''; $mensaje = ''; $outputPath = $null
            $attempt = 0
            do {
                try {
                    $ingest = Invoke-EEIngest -BackendUrl $c.BackendUrl -FunctionKey $c.FunctionKey -FilePath $file -ExpectedType $expected -CorrelationId $correlationId -SkipGdcUpload ([bool]$c.SkipGdcUpload)
                    $instanceId = $ingest.InstanceId
                    $status = Wait-EEDurableStatus -StatusQueryUri $ingest.StatusQueryUri -FunctionKey $c.FunctionKey -TimeoutSeconds ([int]$c.TimeoutSeconds) -PollIntervalSeconds ([int]$c.PollIntervalSeconds)
                    if ($status.RuntimeStatus -eq 'Completed' -and $status.OutputJson) {
                        $outputPath = Join-Path $jsonDir ($name + '.json')
                        [System.IO.File]::WriteAllText($outputPath, $status.OutputJson)
                        $estado = 'Completado'
                    } elseif ($status.RuntimeStatus -eq 'Timeout') {
                        $estado = 'Timeout'; $mensaje = 'Timeout de polling'
                    } elseif ($status.RuntimeStatus -eq 'Error') {
                        $estado = 'Error'
                        if ($status -is [hashtable] -and $status.ContainsKey('Message') -and $status.Message) {
                            $mensaje = [string]$status.Message
                        } else {
                            $mensaje = 'Error'
                        }
                    } else {
                        $estado = 'Error'; $mensaje = $status.RuntimeStatus
                    }
                    break
                } catch {
                    $mensaje = $_.Exception.Message
                    if ($mensaje -match '401') { $estado = 'Error'; break }
                    $attempt++
                    if ($attempt -gt [int]$c.MaxRetries) { $estado = 'Error'; break }
                    Start-Sleep -Seconds 3
                }
            } while ($true)
            $fin = Get-Date
            [pscustomobject]@{
                Tipologia = $expected; Fichero = $name; Estado = $estado
                InstanceId = $instanceId; CorrelationId = $correlationId
                Inicio = $inicio.ToString('s'); Fin = $fin.ToString('s')
                DuracionSeg = [int]($fin - $inicio).TotalSeconds; Mensaje = $mensaje
                OutputPath = $outputPath
            }
        }

        # Construir tabla de la tipologia
        $docs = New-Object 'System.Collections.Generic.List[object]'
        $kpiInputs = New-Object 'System.Collections.Generic.List[object]'
        $openDocs = New-Object 'System.Collections.Generic.List[object]'
        foreach ($r in $procResults) {
            $jd = if ($r.OutputPath) { Open-EEJsonDocument $r.OutputPath } else { $null }
            if ($jd) {
                $docs.Add(@{ HasOutput = $true; Root = $jd.RootElement })
                $openDocs.Add($jd)
                $estadoCalidad = Get-EEPathValue $jd.RootElement 'Resultado.EstadoCalidad'
                $confRaw = Get-EEPathValue $jd.RootElement 'Resultado.ConfianzaGlobal'
                $conf = 0.0; $hasConf = [double]::TryParse($confRaw, [ref]$conf)
                $confVal = $null; if ($hasConf) { $confVal = $conf }
                $kpiInputs.Add(@{ Estado = $r.Estado; EstadoCalidad = $estadoCalidad; ConfianzaGlobal = $confVal })
            } else {
                $docs.Add(@{ HasOutput = $false; Root = [System.Text.Json.JsonElement]::new() })
                $kpiInputs.Add(@{ Estado = $r.Estado; EstadoCalidad = ''; ConfianzaGlobal = $null })
            }
            $allLog.Add($r)
        }
        $table = Build-EEExportTable $docs.ToArray()
        foreach ($jd in $openDocs) { $jd.Dispose() }

        $xlsxPath = Join-Path (Join-Path $resultsDir $item.FolderName) ($item.Tipologia + '.xlsx')
        Write-EEWorkbook -Path $xlsxPath -Sheets @(@{ Name = 'Resumen'; Headers = $table.Headers; Rows = $table.Rows })
        $kpiRows.Add((Get-EEKpiRow -Tipologia $item.Tipologia -Results $kpiInputs.ToArray()))
        $tipologiaTables.Add(@{ Name = $item.Tipologia; Headers = $table.Headers; Rows = $table.Rows })
        Write-Host "  -> $xlsxPath"
    }

    Write-EEResumenWorkbook -Path (Join-Path $resultsDir '_resumen.xlsx') -KpiRows $kpiRows.ToArray() -TipologiaTables $tipologiaTables.ToArray()
    Write-EELogCsv -Path (Join-Path $resultsDir '_log.csv') -LogRows $allLog.ToArray()
    Write-EERunJson -Path (Join-Path $resultsDir '_run.json') -Meta @{
        backendUrl = $cfg.BackendUrl; rootPath = $cfg.RootPath
        maxParallel = $cfg.MaxParallel; skipGdcUpload = $cfg.SkipGdcUpload
        tipologias = @($items | ForEach-Object { $_.Tipologia })
        totalDocumentos = $allLog.Count
        completados = @($allLog | Where-Object { $_.Estado -eq 'Completado' }).Count
    }
    Write-Host "Hecho. Salidas en $resultsDir"
}

if ($LibOnly) { return }

# --- cuerpo principal (Task 9) ---
Invoke-EEMain -Params $PSBoundParameters
