<#
.SYNOPSIS
    Genera un corpus JSONL con el markdown extraído de todos los PDFs de una carpeta.

.DESCRIPTION
    Recorre recursivamente <InputFolder> buscando ficheros .pdf.
    La subcarpeta relativa a la raíz se usa como "tipología" del documento.
    El resultado es un fichero JSONL donde cada línea tiene:
        { "tipologia", "file", "path", "pages", "chars", "skipped", "markdown" }

    El JSONL está pensado para pasarlo a un agente LLM que analice patrones de
    texto por tipología y proponga mejoras a las reglas RuleBasedTDN del clasificador.

.PARAMETER InputFolder
    Carpeta raíz que contiene subcarpetas con PDFs clasificados por tipología.
    Ejemplo: C:\PDFs\clasificados

.PARAMETER OutputFile
    Ruta del fichero JSONL de salida.
    Defecto: .\corpus_<timestamp>.jsonl en el directorio actual.

.PARAMETER Workers
    Número de PDFs procesados en paralelo.
    Defecto: 4. Aumentar en máquinas con más núcleos/memoria.

.PARAMETER StripPrefix
    Prefijo a eliminar del nombre de cada subcarpeta para obtener la tipología.
    Defecto: "AI-01-" (coincide con la convención de carpetas AI-01-XXXX-XX).
    Pasar string vacío para no eliminar ningún prefijo.

.PARAMETER NoBuild
    Si se especifica, omite el paso de build del extractor C# (útil tras primera ejecución).

.EXAMPLE
    .\generate-typology-corpus.ps1 -InputFolder C:\PDFs\clasificados

.EXAMPLE
    .\generate-typology-corpus.ps1 -InputFolder C:\PDFs\clasificados -OutputFile corpus.jsonl -Workers 8

.EXAMPLE
    # Sin recompilar (más rápido en ejecuciones sucesivas):
    .\generate-typology-corpus.ps1 -InputFolder C:\PDFs\clasificados -NoBuild

.EXAMPLE
    # Sin eliminación de prefijo:
    .\generate-typology-corpus.ps1 -InputFolder C:\PDFs\clasificados -StripPrefix ""
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, HelpMessage = "Carpeta raíz con PDFs clasificados por subcarpetas de tipología")]
    [string] $InputFolder,

    [string] $OutputFile = "",

    [int] $Workers = 4,

    [string] $StripPrefix = "AI-01-",

    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Rutas
# ---------------------------------------------------------------------------
$scriptDir   = $PSScriptRoot
$projectDir  = Join-Path $scriptDir "..\src\DocumentIA.Batch.MarkdownExtractor"
$projectFile = Join-Path $projectDir "DocumentIA.Batch.MarkdownExtractor.csproj"

if (-not (Test-Path $projectFile)) {
    Write-Error "No se encontró el proyecto extractor en: $projectDir"
    exit 1
}

if (-not (Test-Path $InputFolder)) {
    Write-Error "La carpeta de entrada no existe: $InputFolder"
    exit 1
}

if ($OutputFile -eq "") {
    $timestamp  = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputFile = ".\corpus_$timestamp.jsonl"
}

$outputPath = [System.IO.Path]::GetFullPath($OutputFile)

# ---------------------------------------------------------------------------
# Build (Release para velocidad; skip si -NoBuild)
# ---------------------------------------------------------------------------
if (-not $NoBuild) {
    Write-Host ""
    Write-Host "Building DocumentIA.Batch.MarkdownExtractor..." -ForegroundColor Cyan
    dotnet build $projectFile --configuration Release --nologo -q
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build fallido. Revisa los errores arriba."
        exit 1
    }
    Write-Host "Build OK." -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Ejecución del extractor
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Iniciando extracción de markdown..." -ForegroundColor Cyan
Write-Host "  Entrada  : $InputFolder"
Write-Host "  Salida   : $outputPath"
Write-Host "  Workers  : $Workers"
if ($StripPrefix -ne "") { Write-Host "  Prefix   : '$StripPrefix' (se eliminará de las carpetas)" }
Write-Host ""

dotnet run --project $projectFile --configuration Release --no-build `
    -- $InputFolder $outputPath $Workers $StripPrefix

$exitCode = $LASTEXITCODE

# ---------------------------------------------------------------------------
# Resumen post-ejecución
# ---------------------------------------------------------------------------
Write-Host ""
if ($exitCode -eq 0) {
    Write-Host "Corpus generado con éxito." -ForegroundColor Green
} elseif ($exitCode -eq 2) {
    Write-Host "Corpus generado con algunos errores (revisa la salida arriba)." -ForegroundColor Yellow
} else {
    Write-Host "La extracción finalizó con código de error: $exitCode" -ForegroundColor Red
    exit $exitCode
}

if (Test-Path $outputPath) {
    $lineCount = (Get-Content $outputPath | Measure-Object -Line).Lines
    $fileSizeKB = [math]::Round((Get-Item $outputPath).Length / 1KB, 1)
    Write-Host ""
    Write-Host "Fichero JSONL: $outputPath" -ForegroundColor White
    Write-Host "  Documentos : $lineCount"
    Write-Host "  Tamaño     : $fileSizeKB KB"
    Write-Host ""
    Write-Host "Siguiente paso: pasa el JSONL a tu agente LLM." -ForegroundColor Cyan
    Write-Host "  Ejemplo de prompt de contexto:"
    Write-Host '  "Analiza los siguientes documentos clasificados por tipología (campo `tipologia`).'
    Write-Host '   Para cada tipología, identifica las palabras, frases y patrones de texto más'
    Write-Host '   discriminativos que permitan distinguirla de otras tipologías.'
    Write-Host '   Propón reglas de clasificación basadas en texto para el campo `markdown`."'
    Write-Host ""
}
