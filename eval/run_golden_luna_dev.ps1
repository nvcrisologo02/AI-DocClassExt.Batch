# Lanza las dos pasadas de la golden (467 docs) contra DEV para la prueba de gpt-5.6-luna
# (AB#101373): primero la de control (modelo por defecto, config-dev.json) y despues la
# candidata (config-dev-luna.json). Nunca las dos a la vez (cuota de 50K TPM del deployment
# Data Zone y ParallelQueries 2 por pasada). Cada pasada usa --resume sobre su directorio,
# con hasta -Passes intentos, asi que una interrupcion se recupera relanzando con los mismos
# -ControlDir / -LunaDir.
#
# Uso (desde cualquier directorio):
#   pwsh eval/run_golden_luna_dev.ps1 -Detached                       # ambas pasadas en segundo plano
#   pwsh eval/run_golden_luna_dev.ps1 -Phase luna -Detached           # solo la candidata
#   pwsh eval/run_golden_luna_dev.ps1 -ControlDir <d> -LunaDir <d> -Detached  # reanudar
#   pwsh eval/run_golden_luna_dev.ps1 -Phase luna -OnlyFiles eval/golden-muestra-cache.txt -Detached
#       # solo los ficheros de la lista (un nombre por linea; muestra fija de 50 de cache, AB#101413)
#
# Requisitos: harness compilado, eval/runs/config-dev.json y eval/runs/config-dev-luna.json
# (gitignored; la segunda es copia de la primera con Model = classification.gpt56-luna) y el
# corpus golden local. No usar contra PRE ni PRO.

param(
    [ValidateSet("both", "control", "luna")]
    [string]$Phase = "both",
    [string]$ControlDir = "",
    [string]$LunaDir = "",
    [int]$Passes = 3,
    [int]$Parallel = 2,
    [int]$MaxPages = 10,
    [string]$Corpus = "C:\temp\MVP\corpus-golden",
    [string]$OnlyFiles = "",
    [switch]$Detached,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$evalDir = $PSScriptRoot
$repo = Split-Path -Parent $evalDir
$exe = Join-Path $repo "src\DocumentIA.Batch.Evaluation\bin\Debug\net8.0-windows\DocumentIA.Batch.Evaluation.exe"
$configControl = Join-Path $evalDir "runs\config-dev.json"
$configLuna = Join-Path $evalDir "runs\config-dev-luna.json"

foreach ($p in @($exe, $configControl, $configLuna)) {
    if (-not (Test-Path $p)) { throw "No existe: $p" }
}
if (-not (Test-Path $Corpus)) { throw "No se ve el corpus: $Corpus" }
if (-not [string]::IsNullOrWhiteSpace($OnlyFiles)) {
    if (-not (Test-Path $OnlyFiles)) { throw "No existe la lista -OnlyFiles: $OnlyFiles" }
    $OnlyFiles = (Resolve-Path $OnlyFiles).Path
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
if ($Phase -ne "luna" -and [string]::IsNullOrWhiteSpace($ControlDir)) {
    $ControlDir = Join-Path $evalDir ("runs\" + $stamp + "-gpt4omini-control")
}
if ($Phase -ne "control" -and [string]::IsNullOrWhiteSpace($LunaDir)) {
    $LunaDir = Join-Path $evalDir ("runs\" + $stamp + "-gpt56-luna-golden")
}
foreach ($d in @($ControlDir, $LunaDir)) {
    if (-not [string]::IsNullOrWhiteSpace($d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
}
if ($ControlDir) { $ControlDir = (Resolve-Path $ControlDir).Path }
if ($LunaDir) { $LunaDir = (Resolve-Path $LunaDir).Path }
$masterLog = Join-Path $evalDir ("runs\golden-luna-" + $stamp + ".log")

if ($WhatIf) {
    Write-Output "Phase=$Phase Passes=$Passes Parallel=$Parallel MaxPages=$MaxPages Corpus=$Corpus"
    Write-Output "Control: $ControlDir (config $configControl, label gpt4omini-control)"
    Write-Output "Luna:    $LunaDir (config $configLuna, label gpt56-luna-golden)"
    if ($OnlyFiles) { Write-Output "Solo ficheros de: $OnlyFiles" }
    Write-Output "Log maestro: $masterLog"
    return
}

if ($Detached) {
    $self = $MyInvocation.MyCommand.Path
    $procArgs = @("-NoProfile", "-File", $self, "-Phase", $Phase, "-Passes", $Passes, "-Parallel", $Parallel, "-MaxPages", $MaxPages, "-Corpus", $Corpus)
    if ($ControlDir) { $procArgs += @("-ControlDir", $ControlDir) }
    if ($LunaDir) { $procArgs += @("-LunaDir", $LunaDir) }
    if ($OnlyFiles) { $procArgs += @("-OnlyFiles", $OnlyFiles) }
    $proc = Start-Process -FilePath "pwsh" -ArgumentList $procArgs -WindowStyle Hidden -PassThru
    Write-Output "Lanzado PID $($proc.Id). Control: $ControlDir  Luna: $LunaDir"
    Write-Output "Seguimiento: Get-Content '$masterLog' -Tail 5 -Wait"
    return
}

function Invoke-Pasada([string]$runDir, [string]$config, [string]$label) {
    $log = Join-Path $runDir "run.log"
    "=== $(Get-Date -Format s) inicio $label (pasadas=$Passes paralelismo=$Parallel maxPages=$MaxPages) dir=$runDir" | Tee-Object -FilePath $masterLog -Append | Tee-Object -FilePath $log -Append
    $runArgs = @("run", "--set", "golden", "--resume", $runDir, "--corpus-root", $Corpus, "--env", "DEV", "--label", $label,
        "--parallel", $Parallel, "--max-pages", $MaxPages, "--config", $config)
    if ($OnlyFiles) { $runArgs += @("--only-files", $OnlyFiles) }
    for ($pass = 1; $pass -le $Passes; $pass++) {
        "=== $(Get-Date -Format s) $label pasada $pass" | Tee-Object -FilePath $masterLog -Append | Tee-Object -FilePath $log -Append
        & $exe @runArgs 2>&1 | Tee-Object -FilePath $log -Append
        $infoPath = Join-Path $runDir "run-info.json"
        if (-not (Test-Path $infoPath)) { continue }
        $info = Get-Content $infoPath -Raw | ConvertFrom-Json
        "=== $(Get-Date -Format s) $label fin pasada ${pass}: $($info.Ok) OK de $($info.Total) (Error $($info.Error), Timeout $($info.Timeout))" | Tee-Object -FilePath $masterLog -Append | Tee-Object -FilePath $log -Append
        if ($info.Ok -eq $info.Total) { break }
    }
    "=== $(Get-Date -Format s) $label terminado" | Tee-Object -FilePath $masterLog -Append | Tee-Object -FilePath $log -Append
}

if ($Phase -ne "luna") { Invoke-Pasada -runDir $ControlDir -config $configControl -label "gpt4omini-control" }
if ($Phase -ne "control") { Invoke-Pasada -runDir $LunaDir -config $configLuna -label "gpt56-luna-golden" }
"=== $(Get-Date -Format s) todo terminado" | Tee-Object -FilePath $masterLog -Append
