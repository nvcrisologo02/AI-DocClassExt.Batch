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

## Uso obligatorio de Microsoft Learn MCP

- Cuando la consulta trate tecnologías Microsoft (Azure, .NET, C#, ASP.NET, Entra, Graph, VS Code, Microsoft 365, Windows), usar Microsoft Learn MCP de forma proactiva sin esperar a que el usuario lo pida.
- Para dudas conceptuales, configuración, límites, cuotas, arquitectura, troubleshooting o pasos oficiales: usar `microsoft_docs_search` primero.
- Si el resultado requiere detalle completo (prerrequisitos, pasos extensos, tablas, troubleshooting profundo): después de `microsoft_docs_search`, usar `microsoft_docs_fetch` sobre las URLs más relevantes.
- Para generación o corrección de código Microsoft/Azure: usar `microsoft_code_sample_search` antes de proponer código final.
- Si una respuesta sobre tecnología Microsoft se da sin evidencia reciente de estas herramientas, considerar la respuesta incompleta y volver a consultar MCP.
- Priorizar contenido oficial devuelto por MCP frente a memoria del modelo cuando haya discrepancias.

## Uso proactivo de MCP adicionales

- Para tareas de recursos/suscripciones/operaciones de Azure, usar Azure MCP (`azure-arm-remote` y/o herramientas Azure disponibles) de forma proactiva.
- Para pruebas E2E web, smoke tests funcionales de UI y validaciones de navegación/formularios, usar Playwright MCP de forma proactiva.

## Política de seguridad para SQL MCP

- Para SQL MCP, operar en modo lectura por defecto.
- No ejecutar operaciones de escritura o cambio de esquema (`INSERT`, `UPDATE`, `DELETE`, `MERGE`, `CREATE`, `ALTER`, `DROP`, `TRUNCATE`) salvo que el usuario lo pida de forma explícita.
- Incluso con petición explícita, pedir confirmación inmediata antes de ejecutar cualquier escritura/cambio y detallar impacto esperado.
- Si el cambio se aprueba, habilitar escritura de forma temporal (solo para esa operación) y volver a modo solo lectura al finalizar.
- Si no hay confirmación explícita, limitarse a consultas de lectura y análisis.

## Generación de ficheros temporales

- Cuando se generen ficheros temporales (scripts de análisis, reportes, validaciones, pruebas puntales) que **no son parte del código permanente del repositorio**, guardarlos siempre en: `docs\auxiliares\temps\<FECHA>\<archivo>`
- Formato de fecha: `YYYY-MM-DD` (ej: `2026-06-02`)
- Ejemplos de ficheros temporales: análisis CSV, scripts de diagnóstico puntuales, reportes de validación, logs de pruebas, ficheros de configuración de test.
- La carpeta `docs\auxiliares\temps\` está en `.gitignore`, por lo que no se committearán ficheros ahí automáticamente.
- Beneficios: organización clara, evita ensuciamiento del repo, trazabilidad por fecha, facilita limpieza posterior.
- Si un fichero temporal se vuelve permanente o reutilizable, moverlo explícitamente a su ubicación definitiva en el repo.
