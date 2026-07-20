<#
.SYNOPSIS
    Publica Batch Classification Lite como ejecutable autocontenido single-file (win-x64).

.EXAMPLE
    pwsh ./scripts/publish-classification-lite.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputPath = "artifacts/publish/classification-lite-win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/DocumentIA.Batch.ClassificationLite/DocumentIA.Batch.ClassificationLite.csproj"
$output = Join-Path $repoRoot $OutputPath

Write-Host "Publicando $project -> $output" -ForegroundColor Cyan

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "El publish ha fallado con codigo $LASTEXITCODE"
}

$configPath = Join-Path $output "config.json"
if (-not (Test-Path $configPath)) {
    @{
        SelectedEnvironment = "PRO"
        Environments = @(
            @{
                Name = "PRO"
                BackendUrl = "https://srbappprodocai.azurewebsites.net"
                FunctionKey = ""
            }
        )
        ParallelQueries = 2
        InternalBatchSize = 1000
        PollingIntervalSeconds = 60
        ClassificationLevel = "TDN1_TDN2"
        Provider = "auto"
        Model = "auto"
        OnlyClassification = $true
        ForceReprocess = $false
        MaxRetries = 3
        SkipAlreadyProcessed = $true
    } | ConvertTo-Json -Depth 5 | Set-Content -Path $configPath -Encoding UTF8
    Write-Host "config.json inicial generado (Function Key vacia: rellenar antes de distribuir)." -ForegroundColor Yellow
}

Write-Host "Publicacion completada en $output" -ForegroundColor Green
