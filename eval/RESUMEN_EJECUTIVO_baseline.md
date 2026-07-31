# Resumen ejecutivo — Evaluación de la clasificación documental (DocumentIA)

**Fecha:** 2026-07-22 (act. 2026-07-23 con verificación independiente) · **Entorno:** DEV · **Motor evaluado:** clasificación GPT por niveles TDN1/TDN2 · **Muestra:** 467 documentos validados (subconjunto estratificado de 3.294 revisados)

---

## 1. Qué se ha hecho

Se ha construido un **sistema de evaluación reproducible** de la calidad de clasificación (antes se hacían catas manuales de ~100 documentos). Permite medir con rigor estadístico si un cambio de prompt, modelo o umbral mejora o empeora, y localizar exactamente dónde falla. Con él se ha ejecutado una **medición de referencia (baseline)** sobre 467 documentos ya validados por negocio.

## 2. El hallazgo principal

> **La precisión real del clasificador es mejor de lo que dicen las cifras crudas.** La medición directa da 57% de acierto en familia (TDN1), pero al reclasificar de forma independiente los documentos "en disputa" se comprueba que **40 de esos "errores" eran en realidad etiquetas de referencia mal puestas** (el clasificador acertaba). Corregidas, la precisión **real de familia sube al 65%**.

De los 467 documentos, el 31% es correcto de forma plena. Del resto, **~24% (110 documentos) son problemas estructurales de taxonomía** —familias de procedencia y defectos de catálogo— **que ningún ajuste técnico puede resolver**, y otro bloque son etiquetas de referencia erróneas. El trabajo técnico real del clasificador, aunque existe, queda acotado y localizado documento a documento.

Todas las disputas (165 documentos) se han **verificado de forma independiente**: se reclasificaron a ciegas, una a una, leyendo el contenido sin conocer ni la etiqueta ni la decisión del clasificador (ver sección 4-bis).

## 3. Cómo se descomponen los resultados

| Situación | Docs | Naturaleza | A quién corresponde |
|---|---:|---|---|
| Correcto | 147 (31%) | — | — |
| **Familia definida por procedencia** | 72 (15%) | El tipo depende de quién generó el documento y del workflow, no de su contenido | **Negocio** |
| **Fallo real del clasificador** (familia) | 68 (15%) | El documento sí es de la familia etiquetada; el clasificador se equivocó | **Equipo técnico** |
| Etiqueta de referencia errónea (familia) | 40 (9%) | El documento está mal etiquetado; el clasificador acertaba | Revisión (negocio) |
| **Defecto de catálogo de subtipos** | 38 (8%) | Subtipos duplicados o "cajón de sastre" imposibles de distinguir | **Negocio** |
| Errores de subtipo (clasificador / etiqueta) | 53 (11%) | Familia correcta; falla o discrepa el subtipo | Mixto (confirmar) |
| Escaneados de subtipo / ambiguos | 26 (6%) | Requieren OCR o revisión puntual | Mixto |
| **Clasificador no responde** | 18 (4%) | Documento clasificable, pero el motor no devolvió nada (robustez) | **Equipo técnico** |
| Tercera tipología | 5 (1%) | Ni la etiqueta ni el clasificador acertaron | Revisión |

## 4. Los tres hallazgos estructurales (decisiones de negocio)

1. **Familias de procedencia** (72 docs). Cuatro familias —PRPI (propuesta interna), ACUI/ACUE (decisiones) y PRPE (propuesta de tercero)— se definen por su **origen y estado en el proceso**, no por lo que dice el documento. Dos documentos idénticos pueden pertenecer a familias distintas. *Ningún prompt lo resuelve desde el texto.*
2. **Subtipos duplicados** (calidad de catálogo). Hay subtipos con la **descripción literalmente idéntica** (p. ej. TASA-11 y TASA-09), imposibles de separar. Deben fusionarse o diferenciarse.
3. **Subtipos "cajón de sastre"** (28% de las confusiones de subtipo). Subtipos "Otro / no contemplado" (FACT-10, FOTO-04…) que absorben o pierden los específicos. Conviene revisar sus criterios de uso.

## 4-bis. Verificación independiente (segunda opinión ciega)

**Todos los documentos en disputa (165)** —aquellos donde la etiqueta y el clasificador no coincidían, o el clasificador no decidió— se han reclasificado desde cero leyendo su contenido, sin conocer ninguna decisión previa. De los 131 que discrepaban a nivel de familia:

- En el **66%** la segunda opinión coincide con la **etiqueta de referencia** (el clasificador se equivocó o no respondió).
- En el **31%** coincide con el **clasificador** (la etiqueta de referencia estaba mal).
- En un **4%** ni una ni otra eran correctas: apareció una **tercera** tipología más adecuada.

Esta cobertura es **completa** (no una muestra): cada disputa de familia tiene ya una segunda opinión independiente registrada en el informe por fichero.

Dos conclusiones adicionales de este ejercicio:

1. **Robustez del clasificador** (hallazgo nuevo): en los 20 documentos donde el clasificador **no devolvió ninguna tipología**, el documento sí era perfectamente clasificable (18 de 20 coinciden con la etiqueta). No es un problema de criterio sino de que el motor a veces no emite resultado — es un fallo técnico acotado y corregible.
2. Los documentos **escaneados** están, en su mayoría, bien etiquetados (10 de 11): el clasificador es quien tropieza con ellos, no la referencia.

## 4-ter. Resumen por familia (dónde se concentra cada problema)

El detalle por familia (informe `resumen_tdn1.csv`) muestra que cada tipo de problema se concentra en familias distintas, lo que permite priorizar:

- **Fallos reales del clasificador** (trabajo de prompt): se concentran en **COMU, CORR, CUAD, NOTS, CERA** (5-6 fallos cada una).
- **Etiquetas mal puestas** (re-etiquetado): sobre todo **INRG** (10 de 15) y **NOVA** (6 de 11).
- **Familias de procedencia** (negocio): **PRPI** (15 de 15, el 100%), **DOCA** (10) y **ACUI/ACUE**.
- **Problema solo de subtipo** (familia bien detectada): **DOCN, PBLO, PRES, DOCJ** — el motor acierta la familia y solo falla el subtipo.
- **Familias que ya funcionan bien**: ACTR, DEAC, CERT, LIPR, DECL, CEDU, ESCR (mayoría correctas).

## 4-quater. Primera mejora de calidad medida (reglas de contraste)

Como prueba del ciclo de mejora medible, se afinaron las descripciones de catálogo de 5 familias con fallos reales del clasificador (NOTS, CUAD, CERA, COMU, CORR) y se midió el antes/después sobre los mismos documentos:

| Familia | Antes → Después |
|---|---|
| NOTS | 9/15 → 13/15 |
| CUAD | 3/9 → 5/9 |
| CERA | 8/15 → 9/15 |
| COMU | 5/15 → 6/15 |
| CORR | 4/15 → 5/15 |
| **Total 5 familias (TDN1)** | **42% → 55%** |

Comparación pareada (McNemar): 11 documentos mejoran, 2 empeoran, **p = 0,022 → mejora estadísticamente significativa**, sin regresión en ninguna familia. Requirió una iteración (la primera versión de la regla COMU/CORR mejoraba en neto pero degradaba CORR; la segunda, asimétrica, lo corrigió). Es la primera mejora de calidad verificada y demuestra que el trabajo de contraste sobre familias clasificables por contenido sí mueve la aguja de forma medible.

## 5. Recomendación: orden de trabajo

El mayor retorno **no está en ajustar prompts todavía**, sino en preparar el terreno:

1. **Limpiar las etiquetas de referencia** (~66 documentos con etiqueta dudosa: 40 de familia + 26 de subtipo), cada uno con corrección propuesta, evidencia y segunda opinión independiente ya generadas. *Sin esto, cualquier mejora se mide contra ruido.*
2. **Decidir la estrategia de las familias de procedencia** (72 docs): aportar metadato externo (sistema origen, quién lo subió, estado) o aceptar techo bajo y enrutar aparte.
3. **Sanear el catálogo de subtipos** (38 docs): fusionar duplicados y revisar los cajones de sastre.
4. **Corregir la robustez del clasificador**: evitar que devuelva "sin tipología" en documentos clasificables (18 casos detectados). Trabajo técnico acotado.
5. **Atacar los fallos reales del clasificador con ajustes de prompt** (68 de familia + 27 de subtipo, ya localizados documento a documento), midiendo cada cambio contra la referencia ya limpia.

## 6. Estado y trazabilidad

- Herramientas y baseline entregados; medición repetible en cada cambio.
- Trabajo registrado en Azure DevOps: harness de evaluación (AB#99948) y hallazgos AB#99973 (revisión de etiquetas), AB#99976 (familias de procedencia), AB#99982 (calidad de catálogo TDN2).
- Detalle documento a documento disponible en el informe por fichero.

> **Conclusión:** el clasificador de contenido funciona mejor de lo que sugería el número inicial, y una segunda opinión independiente lo confirma. La palanca de mejora inmediata es de **negocio y taxonomía** (etiquetas, procedencia, catálogo), no de tecnología. El trabajo técnico pendiente —robustez del motor y ajuste de prompts en los fallos reales— es acotado y ya está localizado documento a documento.
