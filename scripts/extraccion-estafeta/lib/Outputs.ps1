Set-StrictMode -Version Latest

function Get-EEKpiRow {
    param([Parameter(Mandatory)][string]$Tipologia, [object[]]$Results)
    $total = $Results.Count
    $completados = @($Results | Where-Object { $_.Estado -eq 'Completado' }).Count
    $errores = @($Results | Where-Object { $_.Estado -eq 'Error' -or $_.Estado -eq 'Timeout' }).Count
    $revision = @($Results | Where-Object { $_.EstadoCalidad -eq 'Revision' }).Count
    $confianzas = @($Results | Where-Object { $null -ne $_.ConfianzaGlobal } | ForEach-Object { [double]$_.ConfianzaGlobal })
    $media = if ($confianzas.Count -gt 0) { ($confianzas | Measure-Object -Average).Average } else { $null }
    return [pscustomobject]@{
        Tipologia = $Tipologia; Total = $total; Completados = $completados
        Error = $errores; Revision = $revision; ConfianzaMedia = $media
    }
}

function Write-EELogCsv {
    param([Parameter(Mandatory)][string]$Path, [object[]]$LogRows)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $columns = @('Tipologia', 'Fichero', 'Estado', 'InstanceId', 'CorrelationId', 'Inicio', 'Fin', 'DuracionSeg', 'Mensaje')
    if (-not $LogRows -or $LogRows.Count -eq 0) {
        $header = ($columns | ForEach-Object { '"{0}"' -f $_ }) -join ','
        Set-Content -Path $Path -Value $header -Encoding UTF8
        return
    }
    $LogRows | ForEach-Object { [pscustomobject]$_ } |
        Select-Object $columns |
        Export-Csv -Path $Path -NoTypeInformation -Encoding UTF8
}

function Write-EERunJson {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][hashtable]$Meta)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    ($Meta | ConvertTo-Json -Depth 8) | Set-Content -Path $Path -Encoding UTF8
}

function Write-EEResumenWorkbook {
    param(
        [Parameter(Mandatory)][string]$Path,
        [object[]]$KpiRows,
        [object[]]$TipologiaTables
    )
    $kpiHeaders = @('Tipologia','Total','Completados','Error','Revision','ConfianzaMedia')
    $kpiRowsArr = New-Object 'System.Collections.Generic.List[object]'
    foreach ($k in $KpiRows) {
        $cm = if ($null -ne $k.ConfianzaMedia) { [string]([math]::Round([double]$k.ConfianzaMedia, 4)) } else { '' }
        $kpiRowsArr.Add([string[]]@([string]$k.Tipologia,[string]$k.Total,[string]$k.Completados,[string]$k.Error,[string]$k.Revision,$cm))
    }
    $sheets = New-Object 'System.Collections.Generic.List[object]'
    $sheets.Add(@{ Name = 'KPIs'; Headers = $kpiHeaders; Rows = $kpiRowsArr.ToArray() })
    foreach ($t in $TipologiaTables) {
        $name = $t.Name
        if ($name.Length -gt 31) { $name = $name.Substring(0, 31) }
        $sheets.Add(@{ Name = $name; Headers = $t.Headers; Rows = $t.Rows })
    }
    Write-EEWorkbook -Path $Path -Sheets $sheets.ToArray()
}
