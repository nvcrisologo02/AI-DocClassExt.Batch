# DocumentIA.Batch.Classification

Aplicacion WPF .NET 8 para clasificacion documental por lotes basada en el batch actual de DocumentIA.

## Alcance

- Carga de PDFs por lote.
- Clasificacion contra el backend existente reutilizando el cliente HTTP del batch actual.
- Soporte de `Limit pages` cuando `Classification Only` esta activo (`0` = sin limite).
- Toggle opcional `Generate Markdown before ingest` para enviar `documento.content.markdown` preprocesado.
- Flujo en cola por fichero: primero se aplica `Limit pages` (si corresponde) y despues se genera markdown.
- Exportacion minima a CSV y Excel.
- Columnas de salida:
  - Identificacion del documento.
  - Tipologia identificada.

## Reutilizacion del batch actual

Esta app reutiliza del proyecto `DocumentIA.Batch`:

- `DocumentIaBackendClient` para tipologias, ingest y polling durable.
- `SettingsService` para configuracion local.
- `BatchRunStorageService` para guardar el JSON bruto de salida por documento.
- `BatchOutputAuditExtractor` para leer la identificacion y la tipologia desde la salida.
- Estilos WPF compartidos desde `Resources/Styles.xaml`.

Tambien reutiliza del proyecto `DocumentIA.Batch.Markdown`:

- `PdfPigMarkdownGenerator` (basado en `PdfPig`) para extraccion de markdown por paginas de forma reusable.

## Limpieza de temporales

- El pipeline de preparacion de documentos contempla limpieza explicita de artefactos temporales al finalizar cada elemento de cola (exito, error o cancelacion).
- La implementacion actual de markdown no requiere ficheros temporales para funcionar, por lo que no deja huerfanos.

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

- La exportacion esta reducida a dos campos funcionales para mantener el contrato estable.
- El JSON bruto sigue guardandose por lote para trazabilidad y depuracion.
- La app esta pensada como base limpia para evolucionar sin arrastrar la UI completa del batch original.
