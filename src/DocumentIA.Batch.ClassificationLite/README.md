# Batch Classification Lite

Herramienta de clasificacion masiva simplificada (hasta 100.000 documentos por ejecucion).

## Que hace

Recorre una carpeta (local o de red, con o sin subcarpetas), envia cada PDF a DocumentIA
para clasificacion (TDN1/TDN2), captura el resumen del documento y guarda el resultado en
una base SQLite local. Resultados y resumen se exportan a CSV/Excel y se ven en el detalle.

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

## Function Key ofuscada

La Function Key de cada entorno se guarda ofuscada (`enc:...`) dentro de `config.json`, no
en texto plano: se cifra al Guardar y se descifra al Cargar, y el dialogo de Configuracion
nunca muestra la key ya guardada (solo indica "configurada" / "sin configurar"; dejar el
campo en blanco al guardar conserva la key actual).

Para preparar un `config.json` distribuible con la Function Key ya puesta: arranca la app
una vez, abre Configuracion, introduce la key del entorno y pulsa Guardar; el `config.json`
resultante queda con la key ofuscada y se puede distribuir tal cual junto al ejecutable.

Aviso: esto es ofuscacion portable para evitar texto plano en disco, **no** es proteccion
criptografica fuerte (la clave de cifrado esta embebida en la propia app).

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
| Generar resumen | true (captura `DatosExtraidos.Resumen`; se exporta y se ve en el detalle) |

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
