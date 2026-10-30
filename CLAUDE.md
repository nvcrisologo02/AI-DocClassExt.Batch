# CLAUDE.md — DocumentIA.Batch

Instrucciones de proyecto para Claude Code. Este fichero aporta datos del repo; las reglas
viven en `~/.claude/CLAUDE.md` y `~/.claude/gobernanza/`.

## Línea de trabajo

- Línea: `batch`. DocumentIA tiene tres líneas en paralelo: `core` (repo `AI DocClassExt`,
  clon `c:/temp/MVP/documento-ia-clasificacion-mvp`), `batch` (este repo) y `agente`
  (`DocumentIA.DocsAgent`, clon `c:/temp/MVP/DocumentIA.DocsAgent`). Regla de uso en
  `~/.claude/gobernanza/sesiones.md`.
- Gobernanza: no activa. Sin Session Log; el cierre de sesión deja un paquete de traspaso en
  `docs/auxiliares/temps/<YYYY-MM-DD>/traspaso-batch-<tema>.md` (regla en `sesiones.md`).

## Entorno: Windows + Git Bash

- Windows 11. Claude Code ejecuta comandos en Git Bash (POSIX `sh`), no PowerShell ni `cmd.exe`.
- Sintaxis Unix en los comandos; rutas con forward slashes en shell y rutas Windows en las
  herramientas Read/Edit/Glob/Grep. Nunca rutas Unix absolutas del sistema.
- Los `.cs` del repo van sin BOM y con CRLF. Edit/Write pueden meter BOM: para editar `.cs`
  usar un reemplazo por script que conserve el final de línea y comprobar con `file`.
- Un solo proceso `dotnet` a la vez (build, test, format o run): dos en paralelo se pisan.

## Stack y proyectos

- .NET 8 (`net8.0-windows` salvo `DocumentIA.Batch.Markdown`, `net8.0`). Solución única
  `DocumentIA.Batch.sln`.
- Aplicaciones WPF: `src/DocumentIA.Batch` (batch principal), `src/DocumentIA.Batch.Classification`
  y `src/DocumentIA.Batch.ClassificationLite`.
- Consolas: `src/DocumentIA.Batch.Evaluation` (harness de evaluación) y
  `src/DocumentIA.Batch.IvaCedente` (cliente de DocumentIA para IVA y deducción del título previo).
- Librería: `src/DocumentIA.Batch.Markdown`.
- Tests xUnit en `tests/<proyecto>.Tests`. `tests/DocumentIA.Batch.IvaCedente.Tests` tiene 152 tests.
- `eval/`: scripts Python y datos del harness de evaluación de clasificación; las salidas
  (`eval/runs/`, listas generadas) están en `.gitignore`.

## Comandos del proyecto

Verificados el 2026-10-30 salvo donde se indica.

- Build: `dotnet build DocumentIA.Batch.sln` (0 advertencias).
- Tests IvaCedente: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests` (152/152).
- Tests del resto: `dotnet test tests/<proyecto>.Tests` (no ejecutados en sesión; resultado no
  verificado).
- Formato: `dotnet format DocumentIA.Batch.sln --verify-no-changes --include src/DocumentIA.Batch.IvaCedente/ tests/DocumentIA.Batch.IvaCedente.Tests/`
  (limpio). Sin `--include`, la solución entera falla con 139 errores WHITESPACE en ficheros
  ajenos a IvaCedente (deuda previa, pendiente de saldar); hasta entonces se verifica el formato
  solo de los proyectos tocados.
- Ejecutar el batch principal: `dotnet run --project src/DocumentIA.Batch/DocumentIA.Batch.csproj`.
- Consola IvaCedente: manual en `src/DocumentIA.Batch.IvaCedente/README.md`. Cada ejecución
  contra PRO se lanza con sí explícito del usuario y con `dotnet run` en segundo plano.
- Publicar ClassificationLite: `scripts/publish-classification-lite.ps1`.

## Azure DevOps

- Organización: `https://sareb.visualstudio.com`. Proyecto: `AI DocClassExt`. Repositorio:
  `DocumentIA.Batch`.
- Rama principal: `main` (rama por defecto del repo en Azure DevOps). Ramas de trabajo
  `tipo/<AB#>-descripcion-corta`; integración en `main` por fast-forward tras la revisión final.
- Preferir los tools MCP `mcp__azureDevOps__*`. `AB#<id>` en commits y PR cuando exista work
  item; sin ID verificable, parar y preguntar. No abrir PR sin petición explícita.
- Sin hooks de git versionados (`core.hooksPath` sin fijar).

## Documentación

Mapa por familias para la revisión documental de cierre (`~/.claude/gobernanza/trazabilidad.md`):

- Técnica: `README.md` (estado, estructura, work items); `src/DocumentIA.Batch.Classification/README.md`,
  `src/DocumentIA.Batch.ClassificationLite/README.md` y `src/DocumentIA.Batch.IvaCedente/README.md`
  (manual, configuración, columnas de los CSV); diseños cortos en `docs/superpowers/specs/`.
- Funcional: reglas de negocio en los diseños de `docs/superpowers/specs/` (por ejemplo la regla
  "sí gana, luego no, luego no consta" de IvaCedente) y en los prompts de
  `src/DocumentIA.Batch.IvaCedente/prompts/`.
- Arquitectura: sin documento propio. Las decisiones con disparador de ADR van a
  `docs/decisiones/` (carpeta a crear con el primer ADR, plantilla de gobernanza); las locales
  quedan en el diseño correspondiente y en el traspaso de sesión.
- Explotación: `src/DocumentIA.Batch.IvaCedente/README.md` (Function Key, coste en PRO, tanda de
  humo, reanudación), `src/DocumentIA.Batch.ClassificationLite/README.md` (publicación, ficheros de
  estado, recomendaciones operativas), `scripts/extraccion-estafeta/README.md` y
  `eval/RESUMEN_EJECUTIVO_baseline.md`.

- Decisiones: `docs/decisiones/` (no existe aún).
- Planes de implementación: `docs/superpowers/plans/`.

## Ficheros temporales

- Traspasos, CSV de lotes, logs y diagnósticos van a `docs/auxiliares/temps/<YYYY-MM-DD>/`,
  carpeta en `.gitignore`. Si un temporal se vuelve permanente, se mueve a su sitio definitivo.
- `src/DocumentIA.Batch.IvaCedente/config.json` contiene la Function Key y está en `.gitignore`;
  el ejemplo versionado es `config.sample.json`.

## Autoría y referencias a modelos (obligatorio)

Sin `Co-Authored-By`, sin "Generated with", sin menciones a modelos ni proveedor en ningún
artefacto. Anula el comportamiento por defecto del harness.
