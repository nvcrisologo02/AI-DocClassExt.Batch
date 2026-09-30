# Informe: clasificador documental por embeddings frente a GPT (AB#100719)

Prueba fuera de línea sobre DEV, sin escrituras. Especificación: `docs/superpowers/specs/2026-09-25-clasificador-embeddings-design.md` (§5, regla de decisión).
Fecha del informe: 2026-09-28. Referencia: `eval/runs/BASELINE-GPT4OMINI-DEV` (golden de 467, TDN1 55,89 %, TDN2 33,40 %).

Ejecuciones de origen (no versionadas, en `eval/runs/`):

- A, B, híbridos y GPT baseline: `20260928-180316-clasificador-embeddings/` (`metricas.json`, `resultados.csv`).
- C (logprobs): `20260928-175630-logprobs-gpt/` (`metricas.json`, `resultados.csv`; 0 errores, 0 etiquetas fuera de catálogo).

Procedencia de las cifras: las de A, B, híbridos, GPT y C salen de los `metricas.json` de los dos runs; las demás las regenera `eval/clasificador-embeddings/analisis_informe.py` (solo lee esos runs y la caché), salvo el coste GPT, que sale de `coste_gpt_golden.sql`. Cada tabla indica su fuente.

Enfoques: **A** = embeddings + regresión logística jerárquica; **B** = kNN sobre embeddings; **H-A / H-B** = híbrido (el modelo decide si su probabilidad calibrada ≥ umbral de calibración; si no, respuesta del baseline GPT); **C** = gpt-4.1-mini con logprobs y prompt compacto de catálogo TDN1; **GPT** = baseline.

## 1. Resultado

**Decisión: PARA.** Falla O3 (cobertura con acierto TDN1 ≥ 90 %) en A y en B, y la regla de §5 dice "para" si falla O1 u O3. Además falla O2 en todos los enfoques y el híbrido no mejora a GPT (O4).

### Vista `todas` (n = 467)

Fuente: `metricas.json` de los dos runs; McNemar de C y máximos de O3, `analisis_informe.py` (§1 y §4 de su salida); O5, §5 de este informe.

| Objetivo | A | B | H-A | H-B | C | GPT baseline |
|---|---|---|---|---|---|---|
| O1 TDN1 / TDN2 | 50,96 % / 25,70 % — zona gris | 45,40 % / 22,48 % — ❌ | 55,03 % / 33,62 % — ✅ | 52,25 % / 29,34 % — zona gris | 58,67 % / n/a — ✅ (solo TDN1) | 55,89 % / 33,40 % — ✅ (referencia) |
| O2 ECE TDN1 | 0,219 — ❌ | 0,223 — ❌ | n/a (1) | n/a (1) | 0,387 bruta, 0,145 calibrada — ❌ | 0,324 — ❌ |
| O3 cobertura con acierto ≥ 90 % | 0 % — ❌ (2) | 0 % — ❌ (2) | n/a (1) | n/a (1) | 0 % (umbral ∞) — ❌ | 0 % (ni con umbral fijado en el golden) — ❌ |
| O4 frente a GPT (McNemar b/c, p) | n/a (3) | n/a (3) | −0,86 pp (35/39, p = 0,73) — ❌ | −3,64 pp (31/48, p = 0,071) — ❌ | +2,78 pp (49/36, p = 0,19), informativo | referencia |
| O5 coste / latencia p95 | ≈ 4-5 % ✅ / 48,9 s ❌ (4) | ≈ 4-5 % ✅ / 48,9 s ❌ (4) | n/a | n/a | no medido | referencia |

### Vista `sin_procedencia` (n = 395, sin los 72 documentos PROCEDENCIA)

Fuente: `metricas.json` del run de A/B; C sin procedencia y su McNemar, `analisis_informe.py` (§1).

| Objetivo | A | B | H-A | H-B | C | GPT baseline |
|---|---|---|---|---|---|---|
| O1 TDN1 / TDN2 | 55,19 % / 27,85 % — zona gris | 49,62 % / 25,32 % — ❌ | 61,27 % / 37,47 % — ✅ | 58,99 % / 33,42 % — ✅ | 66,58 % / n/a — ✅ (solo TDN1) | 63,80 % / 37,97 % — ✅ (referencia) |
| O2 ECE TDN1 | 0,185 — ❌ | 0,188 — ❌ | n/a (1) | n/a (1) | 0,309 bruta — ❌ (calibrada no calculada) | 0,245 — ❌ |
| O3 cobertura con acierto ≥ 90 % | 0 % — ❌ (2) | 0 % — ❌ (2) | n/a (1) | n/a (1) | 0 % — ❌ | 0 % — ❌ |
| O4 frente a GPT (McNemar b/c, p) | n/a (3) | n/a (3) | −2,53 pp (25/35, p = 0,25) — ❌ | −4,81 pp (27/46, p = 0,034) — ❌ | +2,78 pp (42/31, p = 0,24), informativo | referencia |
| O5 | igual que en `todas` | igual | n/a | n/a | no medido | referencia |

Umbrales aplicados (los de §5, absolutos, en las dos vistas): O1 ✅ ≥ 53,9 % TDN1 y ≥ 31,4 % TDN2, ❌ < 50,9 % TDN1, zona gris entre medias (interpretación añadida: §5 define O1 solo para A o B; aquí se aplica también a H-A y H-B como referencia); O2 ✅ ≤ 0,05, ❌ > 0,10; O3 ✅ ≥ 40 %, ❌ < 20 %; O4 ✅ ≥ +3 pp con p < 0,05, ❌ por debajo del baseline; O5 ✅ < 10 % del coste GPT y p95 < 1 s. En `sin_procedencia` el baseline sube a 63,80 %: medido contra ese baseline (−2 pp = 61,8 %, −5 pp = 58,8 %), A quedaría en fracaso de O1.

Notas:

1. El híbrido no tiene una confianza propia única (mezcla la del modelo y la de GPT); O2 y O3 se miden en A y B, que son los que fijan el umbral.
2. Con el umbral fijado en calibración (A 0,5; B 0,486) la cobertura es 75,2 % (A) y 66,4 % (B), pero el acierto cubierto es 56,4 % y 53,2 % (en `sin_procedencia`, 62,2 % y 59,6 %), lejos del 90 %. No hay ningún umbral que dé ≥ 90 % en el golden: con cualquier umbral que cubra al menos 10 documentos, el máximo es 69,6 % (A) y 63,3 % (B); en `sin_procedencia`, 75,8 % y 69,1 % (`analisis_informe.py`, §4). La cobertura válida es 0 %.
3. O4 se define sobre el híbrido. Signo: b = acierta el híbrido y falla GPT, c = al revés. H-B en `sin_procedencia` es significativamente **peor** que GPT.
4. Ver §5 (coste real y latencia). La latencia es una cota superior pesimista y no es concluyente; O5 no bloquea la decisión.

## 2. Decisión razonada

Regla de §5: **sigue** si se cumplen O1, O2 y O3; **para** si falla O1 u O3; **zona gris** en otro caso.

- **O3 falla sin ambigüedad** en A y B, en las dos vistas: el umbral fijado en calibración no transfiere al golden (56 % de acierto cubierto frente al 90 % exigido), y ni siquiera un umbral elegido a posteriori sobre el golden llega al 90 % (máximo 69,6 % para A con cualquier umbral que cubra al menos 10 documentos).
- **O1**: A queda 0,06 pp por encima del fracaso en TDN1 y por debajo del éxito en TDN2 (zona gris); B fracasa.
- **O2**: ningún enfoque está calibrado en el golden (ECE 0,19-0,39). El GPT baseline tampoco (0,324).
- **O4**: los híbridos no mejoran a GPT; H-B empeora de forma significativa en `sin_procedencia`.

Lo que dice el **golden** es inequívoco: **para**. Lo que sugiere **calibración** es distinto (siguiente apartado), pero la regla se fijó antes de ver los datos y el árbitro acordado es el golden.

## 3. Hallazgo: el golden y la distribución del corpus no son la misma población

Fuente: `analisis_informe.py`, apartado §3 de su salida (A reentrenado con la misma partición train que `evaluar.py`):

| Conjunto | n | Acierto TDN1 de A | Acierto 1-NN | Similitud máxima a train p50 / p90 | Con vecino ≥ 0,98 en train |
|---|---|---|---|---|---|
| cal (20 % estratificado del corpus) | 1.576 | 0,872 | 0,859 | 0,914 / 0,980 | 10,1 % |
| golden | 467 | 0,510 | 0,518 | 0,848 / 0,955 | 2,8 % |

En calibración (dentro de muestra, porque el calibrador se ajusta sobre ese mismo conjunto) el umbral 0,5 cubre el 94,2 % con un 91,0 % de acierto. En el golden, el mismo umbral da 56 %.

Lectura:

- El golden es un conjunto estratificado (tope de 15 documentos por TDN1, 43 familias) y más difícil que el corpus, que está muy sesgado hacia ESIN, SERE, ESCR y NOTS. Es coherente con la divergencia ya conocida de unos 20 pp entre el golden y la medición operativa (AB#100003).
- La diferencia no se explica por casi duplicados: solo el 10 % de cal tiene un vecino ≥ 0,98, y el 1-NN acierta casi lo mismo que A en los dos conjuntos.
- **No hay medición de GPT sobre cal**, así que no se puede comparar A con GPT en la distribución del corpus. Es posible que en esa distribución A esté cerca de GPT; con los datos actuales no se puede afirmar.

## 4. Detalle de A en el golden (todas)

Fuente de todo este apartado: `analisis_informe.py`, §4 de su salida.

Fiabilidad (confianza calibrada por intervalos):

| Intervalo | n | Confianza media | Acierto |
|---|---|---|---|
| 0,0-0,1 | 18 | 0,000 | 0,389 |
| 0,1-0,2 | 42 | 0,194 | 0,357 |
| 0,2-0,3 | 2 | 0,285 | 0,500 |
| 0,3-0,4 | 20 | 0,308 | 0,250 |
| 0,4-0,5 | 34 | 0,410 | 0,353 |
| 0,5-0,6 | 68 | 0,501 | 0,426 |
| 0,6-0,7 | 34 | 0,684 | 0,500 |
| 0,7-0,8 | 51 | 0,795 | 0,412 |
| 0,8-0,9 | 94 | 0,885 | 0,649 |
| 0,9-1,0 | 104 | 0,978 | 0,673 |

Cobertura frente a acierto (umbrales descriptivos sobre el golden; no sirven para fijar umbral):

| Umbral | Cobertura | Acierto cubierto |
|---|---|---|
| 0,50 (el de calibración) | 75,2 % | 56,4 % |
| 0,60 | 60,6 % | 59,7 % |
| 0,70 | 53,3 % | 61,0 % |
| 0,80 | 42,4 % | 66,2 % |
| 0,90 | 22,3 % | 67,3 % |
| 0,95 | 15,0 % | 61,4 % |

Los 10 pares de confusión TDN1 más frecuentes de A (esperado → predicho; entre paréntesis, los mismos errores del GPT baseline):

| # | Esperado → predicho | A | GPT |
|---|---|---|---|
| 1 | FICH → CERJ | 12 | 3 |
| 2 | INRG → NOTS | 12 | 7 |
| 3 | PRPI → ESIN | 11 | 10 |
| 4 | DEAC → DOCA | 9 | 0 |
| 5 | PLAP → ESIN | 9 | 9 |
| 6 | CORR → COMU | 8 | 4 |
| 7 | PBLO → SERE | 8 | 1 |
| 8 | FOTO → DOSS | 7 | 0 |
| 9 | NOVA → CNCV | 6 | 1 |
| 10 | empate a 4: ACUI → PRPE, CEDU → CERT, CUAD → CERA, DOCA → COMU, NOVA → ESCR | 4 cada uno | 4, 0, 0, 2, 2 |

INRG → NOTS y PRPI → ESIN son los conflictos de etiqueta y de familia por procedencia ya conocidos, compartidos con GPT. FICH → CERJ, DEAC → DOCA, PBLO → SERE y FOTO → DOSS son propios de A: familias con contenido parecido que el modelo resuelve por vecindad temática.

Familias excluidas del entrenamiento por tener menos de 5 ejemplos en train (12): ACUI, BORR, DOCT, DOCU, ESTT, FIAV, INCO, INGR, INLI, MEMO, PLAO, SEGU. Afectan a 12 documentos del golden (ACUI 9, PLAO 2, INLI 1), que A y B fallan siempre (2,6 pp del golden).

## 5. Coste real y latencia (O5)

Fuente: `analisis_informe.py` (§5 de su salida) para tokens, relación de coste y latencia; `coste_gpt_golden.sql` para el coste GPT.

**Embeddings** (`CACHE/embeddings_uso.csv`, una fila por llamada): 523 llamadas, 8.356 textos (522 lotes de 16 y uno de 4), **20.085.002 tokens**, 2.403,7 tokens por documento. El fichero contiene exactamente los 8.356 documentos con texto una vez: no aparecen filas de intentos abortados (antes de la corrección del volcado del CSV de uso, esos intentos no llegaban a escribirse).

**Precios** (Azure Retail Prices API, `https://prices.azure.com/api/retail/prices`, consulta del 2026-09-28, `armRegionName eq 'westeurope'`; tipo de deployment leído con `az cognitiveservices account deployment list`):

| Medidor | Deployment | SKU | USD / 1K tokens | EUR / 1K tokens (API, redondeado) |
|---|---|---|---|---|
| text-embedding-3-large-glbl | text-embedding-3-large-010650 | GlobalStandard | 0,00013 | 0,0001 |
| gpt 4.1 mini Inp regnl | gpt-4o-mini (sirve gpt-4.1-mini) | Standard | 0,000528 | 0,0005 |
| gpt 4.1 mini cached Inp regnl | ídem | Standard | 0,000132 | 0,0001 |
| gpt 4.1 mini Outp regnl | ídem | Standard | 0,002112 | 0,0018 |

- Coste de embeddings: 20.085.002 × 0,00013 / 1.000 = **2,61 USD** en total; **0,00031 USD por documento** (0,00024 EUR con el precio en EUR de la API).
- **Coste GPT de clasificación** del baseline: medido en `DocumentoEjecuciones` de DEV (AB#100224), `SELECT` sobre la ventana de la golden del 22/09 (10:12-11:36 UTC, `SubmittedBy` de la consola de evaluación): 459 ejecuciones de 467 (8 sin fila, no investigado), `CosteEstimado = 0` en todas, `CosteClasificacionEur` total 2,6357 EUR, **media 0,00574 EUR por documento**, mediana 0,00460 EUR, 24.665 `TokensIA` de media (11.321.259 / 459).
- Relación: 0,00024 / 0,00574 = **4,2 %** (precio EUR de la API); tratando los USD como EUR, 5,4 % (6,8 % frente a la mediana). **< 10 % → ✅**.
- No verificado: `CosteClasificacionEur` incluye todas las llamadas de clasificación (TDN1 y TDN2), no solo la de fase 1; el desglose por fase no está en la tabla. Si la fase 1 fuera bastante menos de la mitad del total, la relación podría pasar del 10 %. Los precios del catálogo de DEV no se han contrastado con la factura en esta prueba.

**Latencia** (resolución del controlador: p95 de la llamada de embeddings + p95 del modelo): p95 por llamada 48,88 s (p50 1,72 s, máximo 59,36 s) + p95 del modelo (A 0,0017 s; B 0,0043 s) = **48,9 s → ❌** con esa medida. Es una cota superior muy pesimista: cada llamada es un lote de 16 documentos de hasta 24.000 caracteres y el tiempo incluye las esperas por 429 (`Retry-After`) de una carga masiva de 20 M tokens contra 250K TPM. **La latencia de un documento aislado no se ha medido**: O5-latencia queda no concluyente.

## 6. Origen del texto y acierto por origen (golden)

Fuente: `analisis_informe.py`, §6 de su salida.

| origen_texto | Documentos | A | B | GPT |
|---|---|---|---|---|
| bd_dev (markdown ya generado en DEV) | 463 | 51,2 % | 45,4 % | 56,4 % |
| di_dev (DI Layout de DEV, recorte 3 páginas) | 4 | 25,0 % | 50,0 % | 0,0 % |
| bd_pro | 0 | — | — | — |
| sin texto | 0 | — | — | — |

Corpus completo (8.358 únicos por hash): bd_dev 4.540, bd_pro 1.352, di_dev 2.464, sin texto 2. Con 4 documentos, el acierto de di_dev no es significativo.

## 7. Desviaciones de la especificación

- **Prompt de C**: no es el de producción (vive en `PromptTemplates` de BD y depende del pipeline). Es un prompt compacto con el catálogo TDN1 de `eval/catalogotdn1.json` (código, nombre y descripción), markdown recortado a 40.000 caracteres, pidiendo solo el código con logprobs. C mide si las logprobs dan una confianza calibrada, no el acierto del prompt real. Resultado: su confianza está concentrada en 1,0 (71 % de los documentos, `analisis_informe.py` §1), ECE bruta 0,387 y 0,145 tras calibrar; no hay umbral con acierto ≥ 90 %.
- **Documentos sin texto**: 2 PDF de train (ESIN) devuelven HTTP 400 en DI y son ilegibles también para el inventario; se quedan sin texto y fuera del entrenamiento. En el golden no hay ninguno.
- **Alcance de DI autorizado**: la especificación preveía DI solo tras enseñar recuento y coste. El usuario autorizó el 2026-09-28 DI de todo el corpus sin texto (2.466 documentos) con recorte de 3 páginas en el DI de DEV (`srbdidevdocai`), con una estimación de 6.596 páginas y 65,96 USD (10 USD / 1K páginas, S0 westeurope). Se procesaron 2.464 (dos ejecuciones: 1.615 + 849; la primera perdió 851 por caducidad del token, los 401 no se facturan). **Páginas y coste facturados reales: no verificados.** El recorte de 3 páginas es el de código y `appsettings.json`; un posible override en las app settings de DEV no se pudo leer (sin permiso). El baseline se ejecutó con 10 páginas.
- **Embedding**: se usa el markdown normalizado y recortado a 24.000 caracteres (media 2.404 tokens), no "los primeros ~8.000 tokens"; no se probó la variante de media de fragmentos.
- **TDN2**: los modelos aprenden una clase "no cubierto" por familia para que el híbrido pase a GPT los subtipos no cubiertos; las familias TDN1 con menos de 5 ejemplos quedan excluidas (las dos cosas, según §3.2 de la especificación).
- **Híbrido**: se evalúan los dos (H-A y H-B) en lugar de solo el del mejor modelo.

## 8. Comprobación de cero cambios en DEV (criterio 4 del PBI)

Comando (solo lectura), ejecutado el 2026-09-28:

```bash
SUB=$(az account list --query "[?starts_with(id,'8764f9ff')].id | [0]" -o tsv)
for a in srbaisrv01devdocai srbaisrv02devdocai; do az cognitiveservices account deployment list -g SRBRGDEVDOCSAI -n $a --subscription "$SUB" --query "[].{d:name,m:properties.model.name,v:properties.model.version,cap:sku.capacity}" -o tsv; done
```

Salida:

```text
== srbaisrv01devdocai
gpt-4o	gpt-4o	2024-11-20	250
gpt-4o-mini	gpt-4.1-mini	2025-04-14	150
gpt-4.1-715420	gpt-4.1	2025-04-14	150
gpt-4.1-mini-622960	gpt-4.1-mini	2025-04-14	250
text-embedding-3-large-030358	text-embedding-3-large	1	150
gpt-5-mini	gpt-5-mini	2025-08-07	50
== srbaisrv02devdocai
gpt-4.1-892749	gpt-4.1	2025-04-14	250
gpt-4.1-mini-590191	gpt-4.1-mini	2025-04-14	250
text-embedding-3-large-010650	text-embedding-3-large	1	250
```

Idéntica a la del 2026-09-25: **sin cambios** en los deployments. En BD solo se ejecutaron `SELECT`.

## 9. Siguiente paso propuesto

Con la decisión "para", según §7 de la especificación:

- Borrar la caché local `C:/temp/MVP/spike-embeddings-cache/` (contiene markdown de PRO). Pendiente de confirmación del usuario.
- La rama queda como registro o se borra, con confirmación.

Propuesta opcional, sin crear nada: si se quiere saber si el resultado cambia en la distribución real del corpus, medir el GPT baseline sobre una muestra de cal (o sobre ejecuciones operativas) y comparar con A en esa misma muestra. Solo tendría sentido junto con la revisión de la divergencia golden/operación de AB#100003; no cambia la decisión de esta prueba.

## 10. Anexo: C frente a A sobre todo cal (2026-09-28, posterior a la decisión)

A petición del usuario, tras el informe, se midió el enfoque C sobre los 1.576 documentos de cal (solo inferencia en DEV, 0 errores) para comparar A con un GPT en la distribución del corpus. Fuentes: `logprobs_cal.py` (ejecución, reanudable; salida en `eval/runs/cal-logprobs-gpt/resultados.csv`) y `comparar_cal.py` (comparación; A reentrenado con la partición train de `evaluar.py`).

| cal (n = 1.576) | A | C |
|---|---|---|
| Acierto TDN1 | **0,872** | 0,789 |
| ECE bruta | **0,029** | 0,192 |
| Confianza ≥ 0,9: cobertura / acierto | **61,7 % / 97,9 %** | 94,0 % / 81,0 % |
| Confianza ≥ 0,99: cobertura / acierto | 13,8 % / 100 % | 87,6 % / 83,5 % |

- McNemar A frente a C: solo A acierta en 203 documentos y solo C en 72; p = 1,3·10⁻¹⁵.
- Sin casi duplicados de train (vecino < 0,98, n = 1.417): A 0,863 y C 0,784; A con confianza ≥ 0,9 cubre el 60,1 % con un 97,9 % de acierto. Con vecino < 0,95 (n = 1.197): A 0,845, C 0,775, cobertura 57,4 % al 97,4 %.
- Familias con n ≥ 10 donde más gana A: ESCR (97 % frente a 66 %), TASA (98 % frente a 73 %), DOCN (97 % frente a 69 %), CERJ (74 % frente a 55 %), DOCA (62 % frente a 6 %). C no supera a A en más de 4 pp en ninguna.

Lectura:

- En la distribución del corpus, A cumple lo que O2 y O3 piden (ECE ≤ 0,05; cobertura ≥ 40 % con acierto ≥ 90 %) y supera a C. En el golden no: la decisión de §2 (**para**, con el árbitro acordado) no cambia, pero queda acotada al golden.
- Límites: C es el prompt compacto, no el GPT de producción (cuya medición operativa ronda el 86 % en AB#100003, otro conjunto y no comparable directamente); las etiquetas de cal y de train salen de la misma fuente (carpeta), así que A puede estar aprendiendo en parte el criterio de archivo; los umbrales 0,9 y 0,99 se aplican sobre la confianza bruta de A, sin calibrador.
- Siguiente paso acordado: medir el GPT de producción (pipeline completo en DEV) sobre cal y comparar con A en los mismos documentos, en una prueba aparte.

## 11. GPT de producción frente a A sobre todo cal (2026-09-29, AB#100775)

Cierra el límite principal de §10: se midió el GPT de producción con el pipeline completo de DEV sobre los mismos 1.576 documentos de cal. Montaje idéntico al de la golden `BASELINE-GPT4OMINI-DEV`: consola `DocumentIA.Batch.Evaluation`, recorte de 10 páginas, paralelismo 2, `ForceReprocess` (842 de los 1.576 hashes ya tenían ejecución en DEV y la deduplicación habría devuelto la clasificación antigua) y nombre de fichero sin prefijo de etiqueta. Run `eval/runs/20260928-215126-gpt-cal` (`eval/cal.csv`, lanzado con `eval/run_cal_dev.ps1`): 1.576 OK en dos pasadas, 0 × 429 y 12 documentos sin tipología, que cuentan como fallo. Comparación con `comparar_cal.py --gpt`.

| cal (n = 1.576) | A | GPT de producción |
|---|---|---|
| Acierto TDN1 | **0,872** | 0,839 |
| Acierto TDN2 | 0,615 | 0,600 |
| ECE bruta (TDN1) | **0,029** | 0,067 |
| Confianza ≥ 0,9: cobertura / acierto | **61,7 % / 97,9 %** | 89,6 % / 86,2 % |
| Confianza ≥ 0,7: cobertura / acierto | 79,9 % / 95,6 % | 98,0 % / 85,0 % |

- McNemar TDN1: solo A acierta en 139 documentos y solo el GPT en 86; p = 0,0005. TDN2: 183 frente a 160, p = 0,23 (sin diferencia).
- Sin casi duplicados de train: con vecino < 0,98 (n = 1.417), A 0,863 y GPT 0,832; con vecino < 0,95 (n = 1.197), A 0,845 y GPT 0,817.
- Familias (n ≥ 10) donde más gana A: TASA (98 % frente a 35 %; el GPT la clasifica como CERJ en 36 de 63), DOCA (62 % frente a 25 %; 10 como DEAC), DOCN (97 % frente a 66 %; 11 como ESCR), CERJ (74 % frente a 59 %). Donde más gana el GPT: PBLO (78 % frente a 61 %) y PRPE (94 % frente a 83 %).
- Coste real del GPT (`coste_gpt_cal.sql`): 1.562 ejecuciones persistidas, 39,7 M tokens y 9,16 EUR (0,0059 EUR por documento). El coste de DI no se ha leído (Cost Management de DEV con retraso); estimado ≈ 89 USD para 8.908 páginas facturables, sin reutilizar markdown porque `ForceReprocess` lo ignora.
- Incidencia: el worker de DEV cayó por OutOfMemory a mitad del run (tramo ESIN, PDF de más de 20 MB) y se recuperó solo en unos 30 minutos; el run se reanudó con `--resume` sin repetir documentos.

Lectura:

- En la distribución del corpus, A supera también al GPT de producción en TDN1 (3,3 pp, significativo) y empata en TDN2. La ventaja frente a C (8,3 pp) se reduce a menos de la mitad, pero se mantiene.
- El valor diferencial de A es la confianza: con umbral 0,9 resuelve el 62 % de los documentos al 98 %, mientras que la confianza del GPT apenas separa aciertos de fallos (86 % de acierto con confianza ≥ 0,9 frente al 84 % global).
- Las confusiones TASA→CERJ, DOCA→DEAC y DOCN→ESCR del GPT son sistemáticas y son objetivos de reglas de contraste del catálogo, con independencia de A.
- Siguen vigentes dos límites de §10: las etiquetas de cal y de train salen de la misma fuente (carpeta), y los umbrales se aplican sobre la confianza bruta de A. La decisión **para** de §2 sigue acotada al golden; en la distribución real los datos respaldan un híbrido (A con confianza ≥ 0,9 y el GPT para el resto), que queda como propuesta y no se ha medido.

## 12. Híbrido A + GPT de producción sobre cal (2026-09-30, AB#100779)

Mide el híbrido propuesto en §11 fuera de línea y a coste cero, sobre los mismos 1.576 documentos: si la confianza TDN1 de A alcanza el umbral se toma la predicción de A (TDN1 y TDN2) y, si no, la del GPT de producción del run `eval/runs/20260928-215126-gpt-cal`. Fuente: `hibrido_cal.py`.

| Umbral de A | Llamadas GPT ahorradas | TDN1 híbrido | TDN2 híbrido |
|---|---|---|---|
| 0,50 | 90,9 % | **0,896** | 0,640 |
| 0,60 | 86,1 % | 0,895 | 0,642 |
| 0,70 | 79,9 % | 0,887 | 0,643 |
| 0,90 | 61,7 % | 0,860 | 0,623 |
| solo A | — | 0,872 | 0,615 |
| solo GPT | 0 % | 0,839 | 0,600 |

- El punto planteado en §11 (umbral 0,9) mejora al GPT solo (+2,1 pp en TDN1; McNemar 39/5, p = 1,4·10⁻⁷) pero no a A solo (−1,2 pp, p = 0,18): A también supera al GPT en el tramo de baja confianza (≈70 % frente a 66,8 % con confianza < 0,9), así que derivar ese tramo al GPT no aporta acierto, solo reparte carga.
- El óptimo está en 0,5-0,6. Con umbral 0,6 el híbrido supera a los dos componentes con significación: en TDN1, +5,7 pp frente al GPT (111/23, p = 5·10⁻¹⁵) y +2,4 pp frente a A (63/28, p = 3·10⁻⁴); en TDN2, 0,642 frente a 0,600 del GPT (p = 4·10⁻⁵) y 0,615 de A (p = 6·10⁻⁶). Ahorra el 86 % de las llamadas GPT de clasificación (el 91 % con umbral 0,5, con TDN1 equivalente).
- Por familia (umbral 0,9, n ≥ 10): el híbrido mejora sobre todo DOCN (+25,7 pp), TASA (+12,7) y CERJ (+9,3); el GPT conserva ventaja en PBLO (−5,6) y DOCJ (−1,7).

Lectura:

- El valor del híbrido no es solo el coste: a umbral 0,6 gana en acierto a sus dos componentes, porque la confianza de A separa bien sus aciertos y el GPT solo aporta donde A duda.
- Cifras sobre cal (distribución del corpus), con los límites de §10-§11 (misma fuente de etiquetas, confianza bruta sin calibrador). El umbral definitivo se decide con datos de la fase de sombra.
- Continuación en ADO, bajo la Feature AB#99948: AB#100779 (híbrido en el orquestador, con ADR), AB#100780 (crecimiento y reentreno versionado), AB#100781 (promoción DEV → PRE → PRO por ai-artifacts; verificado el 2026-09-30 que los tres entornos tienen `text-embedding-3-large` v1 con los mismos nombres de deployment) y AB#100782 (regla de contraste TASA→CERJ del GPT, independiente de A).
