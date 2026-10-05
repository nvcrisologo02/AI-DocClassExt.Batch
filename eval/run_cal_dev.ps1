# Lanza la consola de evaluacion sobre la particion cal (eval/cal.csv) contra DEV de forma
# desacoplada y reanudable (AB#100775). Cada pasada usa --resume sobre el mismo directorio,
# asi que una interrupcion (cierre de sesion, reciclado, red) se recupera relanzando el script
# con -RunDir. Sin -RunDir crea eval/runs/<fecha>-gpt-cal.
#
# Uso (desde cualquier directorio):
#   pwsh eval/run_cal_dev.ps1 -Detached              # lanza en segundo plano y vuelve
#   pwsh eval/run_cal_dev.ps1 -RunDir <dir> -Detached # reanuda un run existente
#   pwsh eval/run_cal_dev.ps1                         # en primer plano (para probar)
#
# Requisitos: el harness compilado (dotnet build src/DocumentIA.Batch.Evaluation) y
# eval/runs/config-dev.json con la Function Key de DEV (gitignored; se genera desde
# config.sample.json). No usar contra PRO.

param(
    [string]$RunDir = "",
    [string]$List = "",
    [int]$Passes = 3,
    [int]$Parallel = 2,
    [int]$MaxPages = 10,
    [string]$Corpus = "\\srbsfsp01\Usuarios\UBF0238\Documentia\ParaNacho\Class",
    [switch]$Detached
)

$ErrorActionPreference = "Stop"
$evalDir = $PSScriptRoot
$repo = Split-Path -Parent $evalDir
$exe = Join-Path $repo "src\DocumentIA.Batch.Evaluation\bin\Debug\net8.0-windows\DocumentIA.Batch.Evaluation.exe"
$list = if ([string]::IsNullOrWhiteSpace($List)) { Join-Path $evalDir "cal.csv" } else { (Resolve-Path $List).Path }
$config = Join-Path $evalDir "runs\config-dev.json"

foreach ($p in @($exe, $list, $config)) {
    if (-not (Test-Path $p)) { throw "No existe: $p" }
}
if (-not (Test-Path $Corpus)) { throw "No se ve el corpus: $Corpus" }

if ([string]::IsNullOrWhiteSpace($RunDir)) {
    $RunDir = Join-Path $evalDir ("runs\" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-gpt-cal")
}
New-Item -ItemType Directory -Force -Path $RunDir | Out-Null
$RunDir = (Resolve-Path $RunDir).Path
$log = Join-Path $RunDir "run.log"

if ($Detached) {
    $self = $MyInvocation.MyCommand.Path
    $args = @("-NoProfile", "-File", $self, "-RunDir", $RunDir, "-List", $list, "-Passes", $Passes, "-Parallel", $Parallel, "-MaxPages", $MaxPages, "-Corpus", $Corpus)
    $proc = Start-Process -FilePath "pwsh" -ArgumentList $args -WindowStyle Hidden -PassThru
    Write-Output "Lanzado PID $($proc.Id). Run: $RunDir. Log: $log"
    Write-Output "Seguimiento: Get-Content '$log' -Tail 5 -Wait"
    return
}

"=== $(Get-Date -Format s) inicio (pasadas=$Passes paralelismo=$Parallel maxPages=$MaxPages)" | Tee-Object -FilePath $log -Append

for ($pass = 1; $pass -le $Passes; $pass++) {
    "=== $(Get-Date -Format s) pasada $pass" | Tee-Object -FilePath $log -Append
    & $exe run --set list --list $list --resume $RunDir --corpus-root $Corpus --env DEV `
        --parallel $Parallel --max-pages $MaxPages --config $config 2>&1 | Tee-Object -FilePath $log -Append
    $infoPath = Join-Path $RunDir "run-info.json"
    if (-not (Test-Path $infoPath)) { continue }
    $info = Get-Content $infoPath -Raw | ConvertFrom-Json
    "=== $(Get-Date -Format s) fin pasada ${pass}: $($info.Ok) OK de $($info.Total)" | Tee-Object -FilePath $log -Append
    if ($info.Ok -eq $info.Total) { break }
}

"=== $(Get-Date -Format s) terminado" | Tee-Object -FilePath $log -Append
