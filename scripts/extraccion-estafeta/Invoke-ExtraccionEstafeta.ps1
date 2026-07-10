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

if ($LibOnly) { return }

# --- cuerpo principal (Task 9) ---
Invoke-EEMain -Params $PSBoundParameters
