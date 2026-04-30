# DocumentIA.Batch

Aplicacion Windows WPF (.NET 8) para procesado masivo de Notas Simples contra el backend existente de DocumentIA.

## Estado actual

Fecha de corte: 2026-04-30.

El Sprint 1 queda iniciado y con la base funcional creada en este repositorio dedicado. El codigo de la aplicacion vive solo aqui, no en el monorepo `documento-ia-clasificacion-mvp`.

Completado:

- Solucion `DocumentIA.Batch.sln` y proyecto WPF `src/DocumentIA.Batch/DocumentIA.Batch.csproj`.
- Shell principal con panel de configuracion, seleccion de tipologia, flags de ejecucion y zona de carga de PDFs.
- Drag and drop y selector de ficheros PDF, con filtrado de duplicados.
- Persistencia local de configuracion en `config.json` mediante `SettingsService`.
- Editor de prompts por tipologia con override local de `SystemPrompt` y `UserPromptTemplate`.
- Cliente HTTP contra backend DocumentIA para tipologias, ingest y polling Durable.
- Ejecucion paralela controlada por `NumeroColas`.
- Cancelacion de ejecucion en curso y resumen final del lote.
- Trazabilidad por fichero con `CorrelationId`, `InstanceId`, estado Durable, calidad, confianza, duracion y error.
- Persistencia del JSON bruto de salida por fichero en `runs/<yyyyMMdd-HHmmss>/`.
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
- `AB#99240`: completado. Integracion backend, seguimiento por fichero, paralelismo y cancelacion.
- `AB#99241`: completado. Scheduler concurrente por numero de colas.
- `AB#99244`: en curso. Trazabilidad por fichero con correlacion de ejecucion.

## Siguiente bloque sugerido

Continuar con la explotacion de resultados del lote:

- Exportacion CSV con esquema MVP.
- Modal de resumen post-proceso con KPIs agregados.
- Reintento controlado de ficheros fallidos.

Antes de empezar el siguiente bloque, revisar el siguiente work item pendiente y moverlo a `In Progress`.
