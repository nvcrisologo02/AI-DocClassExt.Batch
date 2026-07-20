# Batch Classification Lite

Herramienta de clasificacion masiva simplificada (hasta 100.000 documentos por ejecucion).

## Que hace

Recorre una carpeta (local o de red, con o sin subcarpetas), envia cada PDF a DocumentIA
para clasificacion (TDN1/TDN2) y guarda el resultado en una base SQLite local.

## Ejecucion desde codigo

```bash
dotnet run --project src/DocumentIA.Batch.ClassificationLite
```

## Publicacion

```bash
pwsh ./scripts/publish-classification-lite.ps1
```

Genera `artifacts/publish/classification-lite-win-x64/` con el ejecutable autocontenido
y un `config.json`. Rellenar la Function Key del entorno PRO antes de distribuir.

## Ficheros de estado

| Fichero | Ubicacion | Contenido |
|---------|-----------|-----------|
| `config.json` | Junto al ejecutable | Entornos, paralelismo, lotes, polling, opciones de clasificacion |
| `lite.db` | `%LocalAppData%\DocumentIA.BatchLite\` | Historico permanente de ejecuciones y documentos |

La base de datos NO se borra automaticamente: permite omitir documentos ya clasificados
con exito (clave: nombre + tamano + fecha de modificacion) y recuperar ejecuciones
interrumpidas por un cierre inesperado.

## Configuracion por defecto

| Opcion | Valor |
|--------|-------|
| Environment | PRO |
| Parallel Queries | 2 |
| Internal Batch Size | 1000 |
| Polling Interval | 60 s (adaptativo: 3/5/10/20/30 s y despues el intervalo) |
| Classification Level | TDN1_TDN2 |
| Provider / Model | auto / auto |
| Only Classification | true |
| Force Reprocess | false |
| Max Retries | 3 (estrategia Batch Completion) |
| Skip Already Processed | true |

## Recomendaciones operativas

- Mantener `Parallel Queries = 2` en horario laboral para no degradar PRO.
- Para volumenes grandes, lanzar la ejecucion al final de la jornada y dejar el equipo
  encendido: la aplicacion impide la suspension mientras procesa.
- Con `Skip Already Processed` activo se puede relanzar la misma carpeta cada dia:
  solo se procesan los documentos nuevos.

## Tests

```bash
dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests
```
