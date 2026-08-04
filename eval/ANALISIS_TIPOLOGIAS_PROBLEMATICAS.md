# Análisis de tipologías problemáticas y roadmap de mejora

**Work item:** AB#99390 · **Fecha:** 2026-08-04

**Fuentes de medición (dos, independientes):**

1. **Revisión operativa** — `docs/auxiliares/Bloques_Revision.xlsx`: **7.079 documentos** clasificados en operación y revisados uno a uno por negocio, organizados en 6 bloques. Es la muestra grande y ponderada por volumen real.
2. **Golden dataset** — run `BASELINE-FINAL-CONSOLIDADO` del harness AB#99948: **467 documentos** validados, muestra **estratificada** (~15 por familia). Es la medición reproducible y con evidencia documento a documento.

---

## 1. Resumen ejecutivo

### 1.1 El sistema rinde mucho mejor de lo que decía el baseline del harness

| Medición | TDN1 (familia) | TDN2 (subtipo) | Documentos |
|---|---:|---:|---:|
| Golden dataset (estratificado) | 59,1% | 32,8% | 467 |
| Revisión operativa (todos los bloques) | **73,9%** | **57,2%** | 7.079 |
| **Revisión operativa — Bloque 6 (estado actual)** | **85,9%** | **80,1%** | 4.028 |

El dato relevante para negocio es el último: sobre los 4.028 documentos del bloque más reciente y más grande, **el sistema acierta la familia en el 86% de los casos y el subtipo en el 80%**. Los bloques anteriores, con 36-67% de acierto, corresponden a estados del sistema ya superados.

### 1.2 Los dos grandes fallos de los bloques antiguos ya están resueltos

- **ESIN → ACTT**: era el error más frecuente de todo el corpus (309 documentos, el 4,4%). En el Bloque 6 aparece **0 veces de 4.028**. La familia ACTT llegó a predecirse 125 veces en un solo bloque con 0 aciertos; en el Bloque 6 se predice 7 veces con 5 aciertos.
- **Salidas "DESCONOCIDO" / "DESC"**: 111 documentos en los bloques 1-5, **0 en el Bloque 6**.

No procede abrir trabajo sobre ninguno de los dos.

### 1.3 Lo que sí sigue fallando (Bloque 6)

Cuatro tipologías por debajo o al borde del 60%, que concentran **384 documentos (9,5% del bloque)**:

| Tipología | N | Acierto | Se confunde con | ¿También en el golden? |
|---|---:|---:|---|---|
| **CORR** | 49 | **14%** | COMU (18), ESIN (9) | Sí — 47% |
| **DOCA** | 64 | **16%** | DEAC (45) | Sí — 20%, misma confusión |
| **TASA** | 208 | **43%** | CERJ (102), ESIN (15) | No — en golden da 73% |
| **CNCV** | 63 | **62%** | DOCJ (12) | No — en golden da 80% |

Y cuatro familias que actúan como **sumidero** (absorben documentos ajenos), medidas por su baja precisión: **DEAC 41%**, **CERJ 56%**, **PBLO 57%**, **COMU 66%**.

**TASA es la prioridad por volumen** (208 documentos, 102 de ellos perdidos hacia CERJ). **DOCA→DEAC y CORR→COMU son las prioridades por fiabilidad del diagnóstico**: son las dos únicas confusiones confirmadas de forma independiente por las dos mediciones.

### 1.4 Advertencia metodológica: las dos mediciones no cuadran

El golden dice 59,1% y la revisión operativa reciente dice 85,9%. **La diferencia no se explica por el muestreo**: al reponderar el golden por el volumen real de cada familia en los 7.079 documentos, sube solo de 59,1% a **62,2%**. Quedan **más de 20 puntos sin explicar**.

Las causas plausibles son que ambas mediciones usan referencias distintas —el golden compara contra la etiqueta de carpeta validada, la revisión operativa contra el criterio de un revisor que marca "OK" o corrige— y que puedan corresponder a estados distintos del catálogo. El propio informe de julio ya detectó que una parte de los "errores" del golden eran etiquetas de referencia mal puestas.

**Mientras esa divergencia no se cierre, no se puede fijar un objetivo de calidad con confianza**, porque no hay acuerdo sobre cuál es el número de partida. Es el primer trabajo del roadmap.

---

## 2. Evolución por bloques

| Bloque | Docs | TDN1 | TDN2 | Confianza media | % con confianza < 0,5 |
|---|---:|---:|---:|---:|---:|
| Bloque 1 | 300 | 36,3% | 20,0% | 0,40 | 63% |
| Bloque 2 | 300 | 52,7% | 20,3% | 0,44 | 60% |
| Bloque 3 | 450 | 61,6% | 23,6% | 0,45 | 58% |
| Bloque 4 | 1.001 | 66,9% | 38,9% | 0,54 | 48% |
| Bloque 5 | 1.000 | 55,7% | 20,6% | 0,50 | 53% |
| **Bloque 6** | **4.028** | **85,9%** | **80,1%** | **0,90** | **3%** |

El salto del Bloque 6 es cualitativo, no gradual: el acierto de subtipo pasa de ~20-39% a 80%, y la confianza media de ~0,5 a 0,90. Es coherente con un cambio de versión del pipeline o del catálogo entre el bloque 5 y el 6.

**Limitación de trazabilidad:** el fichero de revisión no registra la fecha ni la versión del pipeline de cada bloque, así que la correspondencia entre bloques y cambios concretos (reglas de contraste, ajustes de catálogo) es una inferencia, no un dato. Conviene incorporar ese metadato en futuras revisiones.

---

## 3. Tipologías problemáticas

### 3.1 Sobre el corpus operativo completo (7.079)

Tipologías con al menos 20 documentos y acierto inferior al 60%: **6 de 31**, que suman **819 documentos (12% del corpus)**.

| Tipología | N | Aciertos | Acierto | Precisión |
|---|---:|---:|---:|---:|
| "Documento inválido" | 22 | 0 | 0% | — |
| CORR | 64 | 7 | 11% | 39% |
| DOCA | 95 | 29 | 31% | 43% |
| PBLO | 107 | 51 | 48% | 57% |
| FICH | 31 | 16 | 52% | 80% |
| CERJ | 500 | 278 | 56% | 52% |

### 3.2 Sobre el estado actual (Bloque 6)

Ya recogidas en 1.3. El contraste entre ambas tablas confirma que **CORR, DOCA y CERJ son problemas persistentes**, mientras que PBLO y FICH mejoran (PBLO pasa de 48% a 86% de acierto, aunque su precisión sigue en 57%).

### 3.3 Subtipos (TDN2) en el estado actual

Aun con la familia correcta, hay subtipos que fallan de forma sistemática en el Bloque 6:

| Familia | Subtipo correcto | Acierto |
|---|---:|---:|
| DOCN | 4 / 44 | 9% |
| DOCA | 9 / 64 | 14% |
| CORR | 7 / 49 | 14% |
| TASA | 89 / 208 | 43% |
| CNCV | 30 / 63 | 48% |
| CERA | 73 / 111 | 66% |
| CERJ | 166 / 250 | 66% |

En el golden, la auditoría de subtipos atribuye el **30% de las discrepancias a defectos del propio catálogo**: 25% subtipos "cajón de sastre" ("otro / no contemplado") y 5% subtipos con descripción prácticamente idéntica (p. ej. TASA-11 y TASA-09). Esto conecta directamente con el 43% de TASA.

---

## 4. Análisis de causa raíz

### 4.1 Efecto sumidero: la causa dominante

Las confusiones no son simétricas ni aleatorias: unas pocas familias absorben documentos de muchas otras. Se detecta comparando precisión (de lo que el motor asigna a esa familia, cuánto lo es de verdad) con acierto.

En el Bloque 6: **DEAC** tiene 100% de acierto pero solo **41% de precisión** — nunca pierde los suyos, pero se lleva los ajenos (45 documentos de DOCA). El mismo patrón, más suave, en CERJ (56%), PBLO (57%) y COMU (66%).

La causa es una descripción de catálogo demasiado inclusiva, que el modelo usa como opción por defecto ante la duda. Es corregible sin tocar código: en julio se aplicó exactamente esa corrección (secciones de "desempate" en la descripción de 5 familias) y el acierto de ese grupo subió del 42% al 55%, con significación estadística (McNemar, p = 0,022) y sin regresiones.

### 4.2 CORR: posible sobrecorrección de una regla anterior

CORR está en 14% de acierto con **100% de precisión**: cuando el motor dice CORR acierta siempre, pero deja escapar 42 de 49 documentos, sobre todo hacia COMU. Es el perfil inverso al sumidero — una familia excesivamente restringida.

La regla de contraste COMU/CORR aplicada en julio buscaba precisamente sesgar los correos de gestión hacia CORR. Los datos sugieren que **el efecto neto es el contrario del buscado** o que la regla no está activa en el entorno que produjo estos datos. Debe verificarse antes de añadir ninguna regla nueva sobre estas dos familias.

### 4.3 Datos insuficientes y OCR degradado: descartado

El motor de extracción de texto funciona en el **99,6%** de los casos (465 de 467 documentos del golden se clasificaron desde el markdown de layout). Los 35 veredictos iniciales de "sin texto" resultaron ser un artefacto de la auditoría, que leía el PDF en lugar del texto ya extraído: al re-auditarlos contra el markdown persistido en base de datos, **27 de 35 tenían texto** (entre 187 y 35.127 caracteres).

**Ni la calidad de OCR ni la falta de datos son palancas de mejora.** No procede invertir en extracción para subir la clasificación.

### 4.4 Familias definidas por procedencia

Cuatro familias —PRPI, ACUI, ACUE y en parte PRPE— se definen por **quién generó el documento y en qué punto del workflow**, no por su contenido. Dos documentos idénticos pueden pertenecer a familias distintas, así que ningún ajuste de prompt las resuelve desde el texto.

En el golden suponen 31 documentos (6,6% del corpus). Es un techo estructural que requiere decisión de negocio, no trabajo técnico. **Nota:** el informe de julio cifraba este bloque en 72 documentos; la regla de auditoría marcaba "procedencia" si *cualquiera* de las dos familias (esperada o predicha) lo era. Restringiéndola a la familia esperada, 41 de esos 72 casos resultan ser efecto sumidero, es decir, **corregibles técnicamente**.

### 4.5 La confianza discrimina, pero ya está bien calibrada en el estado actual

En el corpus completo la señal es fuerte: el tramo por debajo de 0,5 (25% del volumen) acierta el 47%, frente al 83% del tramo automático. Pero **en el Bloque 6 ese tramo bajo es solo el 3%** del volumen (105 documentos), porque la confianza media subió a 0,90.

Subir el umbral a 0,9 aislaría el 17% del volumen con 77% de acierto, frente al 88% del resto: una ganancia de 11 puntos a costa de revisar manualmente uno de cada seis documentos. **La relación coste/beneficio ya no es evidente**, a diferencia de lo que sugerían los bloques antiguos.

El mecanismo de baja confianza **ya existe y está en producción** (`BAJA_CONFIANZA_CLASIFICACION`, 143 documentos marcados, con 51% de acierto frente al 74,5% del resto): funciona, identifica correctamente casos dudosos, y no requiere desarrollo nuevo — a lo sumo, recalibración.

---

## 5. Quick wins

| # | Quick win | Esfuerzo | Efecto estimado | Evidencia |
|---|---|---|---|---|
| **QW-1** | **Regla de contraste TASA / CERJ.** 102 documentos de TASA se clasifican como CERJ en el Bloque 6. | Bajo (catálogo) | Hasta +2,5 pp sobre el bloque | 102 docs medidos; método validado (p=0,022) |
| **QW-2** | **Regla de contraste DOCA / DEAC.** DEAC absorbe 45 documentos de DOCA; DEAC tiene 41% de precisión. | Bajo (catálogo) | Hasta +1,1 pp | Confirmado en **ambas** mediciones |
| **QW-3** | **Verificar el estado de la regla COMU/CORR** antes de tocarla. CORR está en 14% de acierto con 100% de precisión. | Bajo (verificación) | Hasta +1,0 pp | 42 docs perdidos de 49 |
| **QW-4** | **Fusionar subtipos duplicados de TASA** (TASA-09 / TASA-11 y equivalentes), causa directa del 43% de subtipo en TASA. | Bajo (negocio) | Parte de los 119 subtipos fallados de TASA | Auditoría TDN2: 6 duplicados, 31 cajón de sastre |
| **QW-5** | **Registrar fecha y versión de pipeline en las revisiones por bloque.** | Muy bajo | Trazabilidad; sin él no se puede atribuir ninguna mejora | Limitación detectada en este análisis |

Los quick wins QW-1 a QW-3 atacan **189 documentos** del Bloque 6 (4,7% del bloque) y se apoyan en un método ya validado estadísticamente. Todos deben medirse con el harness y comparación pareada antes y después.

---

## 6. Roadmap de iteración 2

### Fase 0 — Reconciliar las dos mediciones (bloqueante)

Golden y revisión operativa difieren en más de 20 puntos que el muestreo no explica. Sin cerrar esto no hay línea base fiable ni objetivo defendible.

- Tomar una muestra común (p. ej. 100 documentos presentes en ambos conjuntos) y comparar veredicto a veredicto.
- Determinar si la diferencia viene del criterio de referencia, del estado del catálogo o de ambos.
- Fijar cuál de las dos es la métrica oficial de calidad y documentarlo.

### Fase 1 — Corregir las confusiones vivas (mayor retorno inmediato)

- QW-1 (TASA/CERJ), QW-2 (DOCA/DEAC), QW-3 (verificación COMU/CORR).
- Revisar las descripciones de las familias sumidero con menor precisión: DEAC (41%), CERJ (56%), PBLO (57%).
- Medir cada cambio de forma aislada con el harness; revertir el que no mejore, como se hizo con la primera versión de la regla COMU/CORR.

### Fase 2 — Subtipos

- Saneamiento del catálogo TDN2: fusionar duplicados y redefinir los "cajón de sastre". *Cubierto por AB#99982.*
- Prioridad por volumen y gravedad: TASA (119 subtipos fallados), CERJ (84), CNCV (33), DOCN (40), DOCA (55).

### Fase 3 — Decisiones de negocio

- Familias por procedencia (PRPI, ACUI, ACUE): aportar metadato externo de origen o aceptar techo bajo y enrutarlas aparte. *Cubierto por AB#99976.*
- Limpieza de etiquetas de referencia dudosas. *Cubierto por AB#99973.*

### Fase 4 — Consolidación

- Ampliar el golden en las familias hoy con muestra insuficiente.
- Re-medir y publicar comparación contra la línea base que se fije en la Fase 0.

### Objetivos propuestos

Deliberadamente **no se fija un objetivo numérico hasta cerrar la Fase 0**. Como referencia provisional, sobre el criterio de la revisión operativa y partiendo del Bloque 6:

| Métrica | Estado actual (Bloque 6) | Objetivo iteración 2 |
|---|---:|---:|
| Acierto de familia (TDN1) | 85,9% | ≥ 90% |
| Acierto de subtipo (TDN2) | 80,1% | ≥ 85% |
| Tipologías con acierto < 60% (n ≥ 20) | 3 | 0 |
| Familias con precisión < 60% | 4 | ≤ 1 |

---

## 7. Backlog de mejoras

### Ya implementado — no requiere trabajo nuevo

| Capacidad | Dónde | Evidencia |
|---|---|---|
| Reintento de `PENDIENTE_REINTENTO` y `ERROR` | AB#99956 (Batch Classification Lite) | Implementado con tests |
| Marcado de baja confianza | AB#99947 | 143 documentos marcados en el corpus operativo |
| Reintento ante 429 en clasificación | `AzureOpenAIResilienceExecutor` | Códigos 429/500/502/503/504 |
| Corrección ESIN→ACTT y salidas "DESCONOCIDO" | — | 0 casos en el Bloque 6 |

### Ya cubierto por work items abiertos

| Work item | Cubre |
|---|---|
| AB#99973 | Revisión de ground-truth del golden dataset |
| AB#99976 | Familias de clasificación por procedencia/workflow |
| AB#99982 | Calidad del catálogo de subtipos TDN2 |

### Nuevo — se propone crear

| Propuesta | Contenido |
|---|---|
| **Reconciliar golden y revisión operativa** | Cerrar la divergencia de más de 20 puntos y fijar la métrica oficial de calidad (Fase 0, bloqueante) |
| **Reglas de contraste para las confusiones vivas** | TASA/CERJ, DOCA/DEAC y verificación de COMU/CORR, medidas con el harness y comparación pareada (QW-1 a QW-3) |

---

## 8. Trazabilidad

- **Revisión operativa:** `docs/auxiliares/Bloques_Revision.xlsx` (hojas `Resultados`, 7.079 filas, y `Resumen`), repositorio `documento-ia-clasificacion-mvp`.
- **Golden dataset:** `eval/runs/BASELINE-FINAL-CONSOLIDADO/` (`results.csv`, `resumen_tdn1.csv`, `audit_groundtruth.csv`, `audit_tdn2.csv`, `audit_notext_db.csv`, `informe_por_fichero.csv`) y `eval/golden.csv`.
- **Método de contraste validado:** `eval/apply_contrast_v2.py`.
- **Informe previo:** `eval/RESUMEN_EJECUTIVO_baseline.md` (incluye la verificación ciega humana de julio).
- **Reproducción:** consola `DocumentIA.Batch.Evaluation` (`run` / `report` / `compare`).
