# DocumentIA.Batch

Aplicacion Windows WPF (.NET 8) para procesado masivo de Notas Simples contra el backend existente de DocumentIA.

## Estado actual

Fecha de corte: 2026-04-29.

El Sprint 1 queda iniciado y con la base funcional creada en este repositorio dedicado. El codigo de la aplicacion vive solo aqui, no en el monorepo `documento-ia-clasificacion-mvp`.

Completado:

- Solucion `DocumentIA.Batch.sln` y proyecto WPF `src/DocumentIA.Batch/DocumentIA.Batch.csproj`.
- Shell principal con panel de configuracion, seleccion de tipologia, flags de ejecucion y zona de carga de PDFs.
- Drag and drop y selector de ficheros PDF, con filtrado de duplicados.
- Persistencia local de configuracion en `config.json` mediante `SettingsService`.
- Editor de prompts por tipologia con override local de `SystemPrompt` y `UserPromptTemplate`.
- `.gitignore` para excluir `bin/`, `obj/`, `.vs/`, artefactos de publicacion y configuracion local.

Validacion realizada:

```powershell
dotnet build DocumentIA.Batch.sln
```

Resultado: compilacion correcta.

## Como compilar

Desde la raiz del repo:

```powershell
dotnet build DocumentIA.Batch.sln
```

## Como ejecutar

Desde la raiz del repo:

```powershell
dotnet run --project src/DocumentIA.Batch/DocumentIA.Batch.csproj
```

## Estructura

```text
src/DocumentIA.Batch/
	Models/        Modelos de configuracion y filas de fichero
	Services/      Persistencia local de settings
	ViewModels/    Estado y comandos de la UI
	Views/         Dialogos secundarios
	Resources/     Estilos WPF
```

## Work items relacionados

- `AB#99237`: completado. Base Sprint 1 y editor funcional de prompts por tipologia.

## Siguiente bloque sugerido

Continuar con la integracion funcional del procesamiento batch:

- Cliente HTTP contra backend DocumentIA (`/api/tipologias`, `/api/ingest` y polling Durable).
- Modelo de ejecucion por fichero con estados `Pending`, `Queued`, `Running`, `Completed`, `Revision` y `Error`.
- Control de paralelismo usando `NumeroColas`.
- Progreso por fila y resumen de ejecucion.

Antes de empezar el siguiente bloque, revisar si existe work item especifico para la integracion HTTP/procesamiento y moverlo a `In Progress`.
