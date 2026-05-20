# Copilot Instructions — Entorno Windows

## OS y Shell

- El entorno de desarrollo es **Windows 11**.
- El shell disponible es **PowerShell** (`pwsh`). **NUNCA** usar bash, sh, zsh ni comandos Unix.
- Usar siempre sintaxis PowerShell para rutas, variables de entorno y comandos de shell.

## Equivalencias de comandos obligatorias

| ❌ Linux/bash | ✅ PowerShell |
|---|---|
| `ls` / `ls -la` | `Get-ChildItem` / `Get-ChildItem -Force` |
| `cat <file>` | `Get-Content <file>` |
| `rm -rf <dir>` | `Remove-Item <dir> -Recurse -Force` |
| `cp -r <src> <dst>` | `Copy-Item <src> <dst> -Recurse` |
| `mv <src> <dst>` | `Move-Item <src> <dst>` |
| `mkdir -p <dir>` | `New-Item -ItemType Directory -Force <dir>` |
| `touch <file>` | `New-Item <file> -ItemType File` |
| `grep <pat> <file>` | `Select-String <pat> <file>` |
| `find . -name <pat>` | `Get-ChildItem -Recurse -Filter <pat>` |
| `export VAR=val` | `$env:VAR = "val"` |
| `echo $VAR` | `$env:VAR` o `Write-Output $env:VAR` |
| `chmod` / `chown` | No aplica en Windows |
| `which <cmd>` | `Get-Command <cmd>` |
| `&&` (encadenar) | `;` o bloques `if ($LASTEXITCODE -eq 0)` |

## Rutas

- Separador de rutas: `\` en PowerShell. También se acepta `/` en argumentos de `dotnet`, `git` y URLs.
- **NUNCA** usar rutas con `/home/`, `/usr/`, `/etc/`, `/var/`, `/opt/`.
- Rutas absolutas de ejemplo: `C:\temp\MVP\...`, `$env:USERPROFILE\...`.

## Variables de entorno en tareas VS Code

- Usar `${env:VARIABLE}` (tasks.json / launch.json).
- Usar `$env:VARIABLE` en scripts PowerShell.

## Tecnología del proyecto

- Runtime: **.NET 8 / .NET 9**, WPF (Windows), aplicaciones de escritorio y batch.
- Comandos dotnet: `dotnet build`, `dotnet run`, `dotnet test`, `dotnet publish`.
- Gestor de paquetes: `dotnet add package` (NuGet). Para Python: `pip` o `uv` dentro del entorno `.venv`.
