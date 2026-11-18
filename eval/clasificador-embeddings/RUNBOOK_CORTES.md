# Runbook: corte recurrente del clasificador por embeddings

Procedimiento único para incorporar un lote nuevo de GDC al clasificador por embeddings (Fase A, sombra en DEV). Sustituye a la spec y el plan por corte que se hicieron en v2-v4: desde v5, cada corte es **un PBI más su sección en `INFORME.md`**. Diseño en el repo core, `docs/superpowers/specs/2026-11-18-runbook-cortes-embeddings-design.md` (AB#101421). Los precedentes de cada regla están en INFORME §13-§16.

Lo que este runbook **no** cambia: la puerta (`puerta_calidad.py`), la tolerancia de cal (0,5 pp, `--tolerancia-cal 0.005`), el mínimo del holdout nuevo (80 % TDN1), la definición de la cal ampliada (§16) ni la exclusión de FOTO-05. Tampoco la promoción a PRE/PRO (AB#100781) ni la medición de la sombra, que van aparte.

Cadencia: un lote cada 7-10 días (decisión del usuario, 2026-11-18).

## 1. Disparador y preparación

1. Aparece una carpeta `LoteN` nueva en `\\srbsfsp01\VARIOS\DOCIA\para embdeddings\` (sí, `embdeddings`, con esa errata). Lote1 fue la raíz del share (v2, 2026-11-03); Lote2 → v3; Lote3 → v4.
2. PBI hijo de la Feature AB#99948 creado **antes** de empezar, con sí del usuario (plantilla en el apartado 7). Ramas `feature/<id>-reentreno-embeddings-v<V>` desde `main` (Batch) y desde `develop` (core).
3. Copiar el lote a local: `C:/temp/MVP/corpus-embeddings-v<V>`. Inventariar por carpeta: nombre, número de ficheros, extensiones (`.pdf`, `.PDF`, `.jpg`, otros) y tamaño. Los nombres de fichero no importan (el inventario etiqueta por carpeta), pero las extensiones sí (apartado 4).
4. Todos los comandos se lanzan desde el clon de Batch, `c:/temp/MVP/DocumentIA.Batch/eval/clasificador-embeddings`, con `.venv/Scripts/python`: allí están el entorno y `out/` (ignorado por git, con los artefactos de todas las versiones). En un worktree no existen.
5. Antes de reapuntar la fila de DEV: foto de la sombra de la versión vigente con `scripts/analysis/sombra-embeddings.sql` de core filtrado a su `VersionModelo`, **excluyendo el golden** (patrón `--` en `NombreArchivo` / `SubmittedBy` de format-verification). Si hay pocas ejecuciones reales, se dice el número y nada más (precedente: §15, sombra de v3).

## 2. Parámetros del corte

Con `N` el número de lote y `V = N + 1` la versión nueva:

| Parámetro | Regla | Lote4 |
|---|---|---|
| Versión nueva | `v<V>` | `v5` |
| Versión anterior | `v<N>` | `v4` |
| Holdout nuevo | `hold<V>` | `hold5` |
| Holdouts de la cal ampliada | `hold2` … `hold<N>` | `hold2`, `hold3`, `hold4` |
| Origen | `share-<AAAA-MM-DD>-lote<N>` (fecha en que GDC dejó el lote) | `share-<fecha>-lote4` |
| Corpus local | `C:/temp/MVP/corpus-embeddings-v<V>` | `corpus-embeddings-v5` |
| Inventario base | `CACHE/inventario_v<N>.csv` | `inventario_v4.csv` |
| Inventario salida | `CACHE/inventario_v<V>.csv` | `inventario_v5.csv` |
| Artefacto anterior / nuevo | `out/clasificador-embeddings-v<N>.json` / `-v<V>.json` | `-v4.json` / `-v5.json` |
| Script SQL de publicación | `0<V+1>-modeloconfig-embeddings-v<V>.sql` | `06-modeloconfig-embeddings-v5.sql` |
| Rollback | el script de la versión anterior | `05-modeloconfig-embeddings-v4.sql` |
| `--fuente-etiquetas` | la de la versión anterior con la fecha nueva añadida | `Documentos seleccionados y certificados por el equipo de GDC (share DOCIA, lotes 2026-11-03, 2026-11-04, 2026-11-18 y <fecha>)` |
| Sección del INFORME | la siguiente libre | §17 |

`CACHE` es `C:/temp/MVP/spike-embeddings-cache`.

## 3. Cadena de comandos

`PY=.venv/Scripts/python`. Las cachés de texto, DI y embeddings son reanudables por hash: **no se relanzan DI ni embeddings sobre lo ya cacheado** y no se borra nada de `CACHE`.

```text
# 3.1 Inventario (las filas base conservan partición y origen)
$PY inventario_manifiesto.py --corpus C:/temp/MVP/corpus-embeddings-v<V> \
    --base CACHE/inventario_v<N>.csv --salida CACHE/inventario_v<V>.csv \
    --origen share-<fecha>-lote<N> --holdout hold<V>

# 3.2 Texto desde BD (markdown ya persistido, por hash)
$PY texto.py --inventario CACHE/inventario_v<V>.csv

# 3.3 DI Layout: primero estimar; lanzar solo con sí del usuario (coste ~20 USD por lote)
$PY di_layout.py --corpus C:/temp/MVP/corpus-embeddings-v<V> --estimar
$PY di_layout.py --corpus C:/temp/MVP/corpus-embeddings-v<V> --lanzar --max-paginas 5

# 3.4 Refrescar texto_origen.csv con el markdown de DI (sin este paso, los embeddings leen el texto viejo)
$PY texto.py --sin-bd --inventario CACHE/inventario_v<V>.csv

# 3.5 Embeddings (sin argumentos: lee texto_origen.csv)
$PY embeddings.py

# 3.6 Entrenamiento
$PY exportar_modelo.py --inventario CACHE/inventario_v<V>.csv --tipologias out/tipologias-dev.json \
    --version v<V> --fuente-etiquetas "<fuente>"

# 3.7 Puerta de calidad: --cal-ampliada OBLIGATORIO desde v5
$PY puerta_calidad.py --anterior out/clasificador-embeddings-v<N>.json --nuevo out/clasificador-embeddings-v<V>.json \
    --inventario CACHE/inventario_v<V>.csv --holdout hold<V> --cal-ampliada --salida out/puerta-calidad-v<V>.json
```

`--holdout` es siempre el del corte **nuevo** (`hold<V>`). Si se quiere la cifra aislada de un holdout anterior como control (en §15, `hold3`), se lanza aparte con `--holdout hold<k> --salida out/puerta-calidad-v<V>-control-hold<k>.json`; en esa ejecución el `AVISO ... holdouts posteriores` es esperado y su veredicto no cuenta.

## 4. Comprobaciones de cada corte

Cada comprobación se hace siempre y su resultado va a la sección del INFORME, aunque sea "nada".

| Comprobación | Cuándo | Criterio | Acción |
|---|---|---|---|
| Revisión de `EXCLUIR_CARPETAS` (`inventario_manifiesto.py`) | tras 1.3 y otra vez tras 3.3 | Carpeta de imágenes (JPG/PNG/TIFF) o en la que DI devuelve vacío o menos de unos 200 caracteres en la mayoría de los documentos. Precedente FOTO-05 (§15): 27 de 28 fotos sin texto útil, pero los 10 con restos habrían dado vector. | Proponer la exclusión al usuario con el dato; con su sí, añadir la carpeta a `EXCLUIR_CARPETAS` con su test en `tests/test_inventario_manifiesto.py` y regenerar el inventario (3.1). Las ya excluidas (hoy `ESIN-AI`, `FOTO-05`) se repasan: si GDC vuelve a mandar la carpeta, sigue fuera. |
| Clases por debajo del mínimo | salida de 3.1 | `MIN_DOCS = 5`; con menos de `MIN_HOLDOUT = 10` la clase no aporta holdout | Las excluye el inventario (`bajo_minimo`); se anotan. |
| Duplicados y ya conocidos | salida de 3.1 | Mismo SHA256 dentro del lote o en cortes anteriores | El inventario se queda con una copia y descarta los ya presentes; se anotan. Un mismo fichero en **dos carpetas distintas** (etiquetas contradictorias) va completo a `EXCLUIR_FICHEROS` (precedente LIPR-06_3 = LIPR-13_31, §13). |
| Ficheros que no son documentos | salida de 3.1 | `Thumbs.db` y similares | Fuera; se anotan. |
| Ficheros truncados o con error de DI | salida de 3.3 | HTTP 400 de DI o PDF ilegible (precedente `Lote2/ESCR-06/ESCR-06_39.pdf`) | Se excluyen, se anota el fichero y se avisa a GDC. Los errores de restos antiguos sin fichero en el corpus nuevo (ESIN-34 de v1, ESCR-06_39) son esperados. |
| Códigos TDN2 del lote que no existen en DEV | antes de 3.6 | Todos los códigos de carpeta deben estar en `out/tipologias-dev.json` | Si falta alguno: alta en DEV como tipología solo de clasificación (precedente `fiav.06`, §13; con sí, es escritura en BD) y regenerar `out/tipologias-dev.json` con `sql/tipologias-pares-tdn.sql`. |
| Aviso de holdout | salida de 3.7 | La puerta **no** debe imprimir `AVISO: ... holdouts posteriores` | Si lo imprime, `--holdout` no es el del corte nuevo: corregir y repetir. |

## 5. Lectura de la puerta

- **Condición**: `PUERTA: PASA` exige TDN1 en la cal ampliada (`cal` ∪ `hold2` … `hold<N>`) no inferior al anterior en más de 0,5 pp, y TDN1 en `hold<V>` ≥ 80 %. Si `FALLA`, no se publica: análisis documento a documento de los que cambian (precedentes `analisis-cal-perdidos-v3.txt` y `-v4.txt` en core) y se lleva al usuario.
- **Serie de `cal` original** (no es condición, se vigila a mano por la dilución de §16): se añade el valor nuevo a la serie (v1 87,25 / v2 87,37 / v3 87,06 / v4 86,68 %) y el acumulado desde v2. Si la caída solo en `cal` de este corte pasa de 0,5 pp, o el acumulado sigue creciendo, se señala al usuario en la propuesta de publicación con el dato; la decisión es suya.
- **Clases flojas del holdout nuevo**: las TDN2 por debajo del 80 % en `hold<V>`, con su `n`, van a la sección del INFORME (precedente §15: CULC-03, PRYT-11, PRPE-07).
- **ECE**: se anota; `calibrado` sigue en `false` y el modo en `sombra`.

## 6. Publicación en DEV (con sí del usuario)

La mecánica está en el repo core, `scripts/migrations/clasificador-embeddings/README.md` (blob primero, script SQL después; la app recoge el cambio en 5 minutos o menos; las versiones anteriores se conservan en el blob). Lo que añade cada corte:

1. Script nuevo `0<V+1>-modeloconfig-embeddings-v<V>.sql` en core, copia del anterior con la ruta `v<V>` (mismo guardarraíl de servidor, backup e idempotencia), y su fila en la tabla del README.
2. Subida al blob con `--overwrite false` y comprobación de MD5:
   `az storage blob upload --auth-mode login --account-name srbstgdevdocai --container-name documentai --name modelos/clasificador-embeddings/v<V>/clasificador-embeddings-v<V>.json --file out/clasificador-embeddings-v<V>.json --overwrite false` (lo mismo con `paridad-v<V>.json`), y `az storage blob show ... --query properties.contentSettings.contentMd5 -o tsv`.
3. Script SQL con `scripts/database/query-sql.ps1 -Server srbsqldevdocai.database.windows.net -Query (Get-Content -Raw <script>)`. Esperado: backup `ModeloConfigs__bak_<fecha>` y una fila actualizada. Comprobación: `Artefacto.BlobPath` de la fila 1026 con la ruta `v<V>` y `Modo = sombra`.
4. Una ejecución de verificación en DEV pasados 5 minutos (sintético `tests/e2e-postdeploy/corpus/generico/escritura-compraventa-sintetica.pdf`) con `VersionModelo = v<V>` y sin error en el bloque Embeddings.
5. Rollback: ejecutar el script de la versión anterior.

## 7. Cierre del corte

**Sección del INFORME** (`## <n>. Reentreno v<V> con el Lote<N> del share DOCIA (<fecha>, AB#<id>)`), con la estructura de §15:

1. Corpus: ficheros, carpetas, extensiones, fecha del share, códigos nuevos frente al train anterior.
2. Inventario: comando, exclusiones con motivo (resultado de cada comprobación del apartado 4), documentos nuevos y reparto train / `hold<V>`.
3. Texto: reutilizados de BD, DI (páginas, coste estimado, vacíos, errores), embeddings (documentos, tokens).
4. Entrenamiento: documentos de train, familias y tipologías cubiertas, cortes del manifiesto, `fuenteEtiquetas`.
5. Puerta: tabla anterior/nuevo de `cal`, cal ampliada y `hold<V>`; veredicto; serie de `cal` original con el acumulado; clases flojas.
6. Foto de la sombra de la versión saliente (apartado 1.5).
7. Publicación: blobs con tamaño y MD5, script y backup, ejecución de verificación, rollback.
8. Reproducir: la cadena del apartado 3 con los valores del corte y SHA256 de todo lo que queda fuera de git (inventario, artefacto, paridad, JSON de la puerta).

Evidencias (salida de la puerta, JSON, análisis) en core, `docs/auxiliares/temps/<fecha>/`.

**PBI** (hijo de AB#99948), título `Reentreno v<V> del clasificador por embeddings con el Lote<N> del share DOCIA`. Criterios de aceptación tipo:

1. Artefacto `v<V>` publicado en DEV en sombra, con `<V>` cortes en el manifiesto y una ejecución de verificación con `VersionModelo = v<V>`.
2. Puerta con `--cal-ampliada`: `PASA`, sin `AVISO` de holdouts posteriores, con la serie de `cal` original anotada.
3. Comprobaciones del apartado 4 hechas y anotadas.
4. Sección del INFORME escrita con el apartado «Reproducir» y los SHA256.

Integración: merge de las ramas en `main` (Batch) y `develop` (core) con sí del usuario; push con sí aparte.

Si un corte trae un caso que este runbook no resuelve, se decide con el usuario y la regla se añade aquí en el mismo PBI.
