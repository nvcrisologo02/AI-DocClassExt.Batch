# Prueba de clasificador probabilístico por embeddings (AB#100719)

- Fecha: 2026-09-25
- Work item: PBI AB#100719, hijo de la Feature AB#99948 (harness de evaluación de clasificación)
- Rama: `feature/100719-clasificador-embeddings` (desde `main` en `e3ffa30`)
- Tipo: prueba de viabilidad fuera de línea. El resultado es una recomendación "sigue / para", no código de producción.

## 1. Pregunta

¿Puede un clasificador basado en embeddings, apoyado o no en las probabilidades de los tokens de GPT, dar probabilidades **calibradas** de TDN1/TDN2 que permitan resolver con seguridad una parte de los documentos sin llamar al prompt de clasificación, sin perder calidad frente al clasificador actual?

La motivación son los modelos de decisión tipada (p. ej. Jev, de TypeSafe AI). Esos no se pueden usar en SAREB por la residencia de los datos, así que se reproduce la idea con recursos propios de Azure.

## 2. Alcance y restricciones

- **Fuera de línea.** No se tocan las Functions, la BD, la configuración ni los despliegues de ningún entorno.
- **DEV**: solo lecturas en BD e inferencia (embeddings, DI Layout, gpt-4.1-mini). **PRO**: solo `SELECT` sobre `Documentos` para reutilizar markdown ya extraído.
- Sin recursos nuevos: se usan deployments que ya existen en DEV (verificado el 2026-09-25):
  - `srbaisrv02devdocai` / `text-embedding-3-large-010650` (GlobalStandard, cap. 250). Se elige para no competir con CU, que usa la 01.
  - `srbaisrv01devdocai` / deployment `gpt-4o-mini`, que en realidad es **gpt-4.1-mini 2025-04-14** (modelo sin razonamiento, admite `logprobs`).
  - `srbdidevdocai` (DI Layout) solo para los documentos sin markdown en BD.
- Clasificación **solo por contenido**, sin metadatos (restricción de negocio del 2026-07-23). El nombre del fichero no entra en ningún modelo.

## 3. Datos

### 3.1 Corpus

`H:\Documentia\ParaNacho\Class` (recuento del 2026-09-25):

- 8.536 PDF en 53 carpetas TDN1 (incluida una carpeta `NO S` que hay que revisar en el inventario). 7.301 llevan la etiqueta TDN2 en el nombre (patrón `XXXX-NN--`) y 1.235 solo el TDN1 de la carpeta.
- 437 TDN2 distintos: 174 con 5 ejemplos o más y 66 con 20 o más.
- Distribución sesgada: ESIN, SERE, ESCR y NOTS suman más de la mitad.

### 3.2 Particiones

1. **Deduplicación** por SHA256 del fichero. Si un hash aparece con etiquetas distintas, se excluye y se cuenta como conflicto.
2. **Evaluación**: el golden de `eval/golden.csv` (467). Se saca del entrenamiento **por hash**, no por nombre.
3. **Entrenamiento y calibración**: el resto, con partición estratificada por TDN1: 80 % para entrenar y 20 % para calibrar y fijar umbrales. Nunca se ajusta sobre el golden.
4. **Clases**:
   - TDN1: todas las familias con 5 ejemplos o más.
   - TDN2: solo los subtipos con 5 ejemplos o más en entrenamiento. El resto se marca como "no cubierto" y en el híbrido se deja a GPT.
   - Los 1.235 ficheros sin TDN2 solo entrenan TDN1.

### 3.3 Texto de cada documento

Por orden, cruzando por SHA256 (el dedup por hash de la BD conserva nombres antiguos, así que el nombre no sirve para cruzar):

1. `Documentos` de DEV: columna binaria `NormalizacionMarkdownGzip`, con respaldo en `NormalizacionMarkdownCompressed` (misma lectura que `audit_notext_db.py`, AB#100170).
2. `Documentos` de PRO, con la misma lectura y en solo lectura.
3. DI Layout de DEV sobre el PDF. **No se lanza sin antes enseñar el recuento de documentos y páginas y el coste estimado, y recibir un sí.**

Por cada documento se guarda `origen_texto` (`bd_dev` | `bd_pro` | `di_dev`) y el número de caracteres. Los documentos sin texto se excluyen del entrenamiento; en el golden cuentan como fallo del clasificador nuevo, igual que cuentan en el baseline.

**Recorte**: el markdown de BD es el que ya generó producción. Para DI se usa el recorte de páginas que tenga DEV configurado; se lee de la configuración y se anota en el informe (hoy sin verificar: el runner usa 5 páginas y el backend 3).

### 3.4 Caché local

`C:/temp/MVP/spike-embeddings-cache/`, fuera de los dos repos: markdown comprimido, vectores e índice por hash. Contiene texto sensible, así que **no se versiona**. Se borra al cerrar la prueba si la decisión es "para".

## 4. Enfoques

| Id | Enfoque | Resumen |
|---|---|---|
| A | Embeddings + regresión logística jerárquica | Un modelo TDN1 y, por familia, un modelo TDN2 entre sus subtipos cubiertos. Probabilidades calibradas (Platt o isotónica, lo que mejor ECE dé en calibración). |
| B | kNN sobre embeddings | Similitud coseno con k vecinos de entrenamiento. La confianza es el voto ponderado, calibrado igual que A. Devuelve los vecinos como explicación. |
| C | gpt-4.1-mini con logprobs | Prompt compacto con el catálogo TDN1 de `eval/catalogotdn1.json` (código, nombre y descripción), pidiendo solo el código con `logprobs`. No reproduce el prompt de producción (vive en `PromptTemplates` y depende del pipeline): mide si las logprobs dan una confianza calibrada, no el acierto del prompt real. Umbral fijado con 300 documentos de calibración; evaluado en el golden. |
| H | Híbrido | A (o B, el mejor de los dos) decide si su probabilidad es ≥ t. Si no, se toma la respuesta del baseline GPT (`BASELINE-GPT4OMINI-DEV/results.csv`). Se simula sin llamadas nuevas. |

Embedding de un documento: los primeros ~8.000 tokens del markdown, que es el límite de entrada del modelo. Variante opcional si sobra tiempo: la media de los fragmentos.

## 5. Objetivos y criterios de éxito

Referencia: `eval/runs/BASELINE-GPT4OMINI-DEV` (TDN1 55,89 %, TDN2 33,40 %, golden de 467, IA propia de DEV). Su columna `confianza` sirve de referencia para O2 y O3 del propio GPT.

| # | Objetivo | Métrica sobre el golden | Éxito | Fracaso |
|---|---|---|---|---|
| O1 | No perder calidad | Acierto top-1 de TDN1 y TDN2 de A o B | ≥ 53,9 % TDN1 y ≥ 31,4 % TDN2 (baseline − 2 pp) | < 50,9 % TDN1 (baseline − 5 pp) |
| O2 | Confianza fiable | ECE de TDN1 con 10 intervalos | ≤ 0,05 | > 0,10 |
| O3 | Automatizar con seguridad | Cobertura máxima con acierto TDN1 ≥ 90 %, con el umbral fijado en calibración y no en el golden | ≥ 40 % | < 20 % |
| O4 | El híbrido mejora a GPT solo | Acierto TDN1 de H frente al baseline, McNemar | ≥ +3 pp con p < 0,05 | por debajo del baseline |
| O5 | Coste y velocidad | Coste y latencia p95 por documento del clasificador, sin contar la extracción | < 10 % del coste de la llamada GPT de fase 1 y p95 < 1 s | — |

**Regla de decisión:**

- **Sigue**: se cumplen O1, O2 y O3. O4 y O5 dan forma a la integración, pero no bloquean.
- **Para**: falla O1 u O3.
- **Zona gris**: cualquier otro caso. Se decide con el usuario a la vista del informe.

Todas las métricas se dan dos veces: con la etiqueta tal cual y quitando los documentos marcados como PROCEDENCIA en `informe_por_fichero` (techo de en torno al 85 % por contenido, AB#99976). Para comparar, el informe incluye también O2 y O3 calculados con la confianza que declara el GPT del baseline.

## 6. Entregables

En `eval/clasificador-embeddings/` de la rama:

- Scripts Python (mismo estilo que `eval/`): inventario y dedup, obtención de texto, embeddings, entrenamiento y calibración, evaluación e informe.
- `INFORME.md`: métricas O1-O5 en las dos vistas, curva de fiabilidad, cobertura frente a acierto, matriz de confusión TDN1, errores más frecuentes, coste real y la recomendación.
- Los resultados por fichero (CSV) van a `eval/runs/`, como el resto de runs (no versionado).

## 7. Vuelta atrás y cierre

- DEV y PRO: nada que restaurar porque no hay escrituras. Al cerrar se comprueba que la lista de deployments de las dos cuentas de DEV es la misma que el 2026-09-25 (criterio 4 del PBI).
- Si la decisión es "para": se borra la caché local. La rama puede quedarse como registro o borrarse, con confirmación.
- Si la decisión es "sigue": la integración en el pipeline (modo sombra, persistencia de la probabilidad, umbrales por familia) es **otra especificación y otro PBI**.

## 8. Riesgos

- **Ruido de etiqueta**: en torno al 24 % de los fallos del golden son de taxonomía o etiqueta y no del clasificador. Afecta a todos los enfoques por igual; de ahí la doble vista de métricas.
- **Texto de PRO frente a DEV**: el markdown puede venir de configuraciones de extracción distintas. Se controla con `origen_texto` y se desglosa el acierto por origen.
- **Clases pequeñas**: 263 TDN2 tienen menos de 5 ejemplos. En TDN2 el clasificador solo cubre una parte y el resto depende de GPT.
- **Datos sensibles en local**: el markdown de PRO se guarda en disco local. Se limita a la caché indicada y se borra al cerrar.
