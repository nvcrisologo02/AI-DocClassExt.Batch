# DocumentIA.Batch.Classification

Aplicacion WPF .NET 8 para clasificacion documental por lotes basada en el batch actual de DocumentIA.

## Alcance

- Carga de PDFs por lote.
- Clasificacion contra el backend existente reutilizando el cliente HTTP del batch actual.
- Exportacion extendida a CSV y Excel con metadatos de clasificacion, trazabilidad y proveedores.
- Panel de resumen por documento seleccionado con estado, decision de clasificacion, justificacion y timeline de actividades.
- Opcion de forzar resumen por defecto (`forzarResumenPorDefecto`) para pruebas de casuistica de resumen.

## Reutilizacion del batch actual

Esta app reutiliza del proyecto `DocumentIA.Batch`:

- `DocumentIaBackendClient` para tipologias, ingest y polling durable.
- `SettingsService` para configuracion local.
- `BatchRunStorageService` para guardar el JSON bruto de salida por documento.
- `BatchOutputAuditExtractor` para leer la identificacion y la tipologia desde la salida.
- Estilos WPF compartidos desde `Resources/Styles.xaml`.

## Ejecucion

Desde la raiz de la solucion:

```powershell
dotnet run --project src/DocumentIA.Batch.Classification/DocumentIA.Batch.Classification.csproj
```

## Build

```powershell
dotnet build DocumentIA.Batch.sln
```

## Notas

- La exportacion incluye estado, tipologia, familia/version, TDN1/TDN2, matricula, clasificador,
  confianza, fallback, duracion, detalle por proveedores y marca de reutilizacion por duplicado.
- El JSON bruto sigue guardandose por lote para trazabilidad y depuracion.
- La app esta pensada como base limpia para evolucionar sin arrastrar la UI completa del batch original.
