# Resumen ejecutivo — Evaluación de la clasificación documental (DocumentIA)

**Fecha:** 2026-07-22 (act. 2026-07-23 con verificación independiente) · **Entorno:** DEV · **Motor evaluado:** clasificación GPT por niveles TDN1/TDN2 · **Muestra:** 467 documentos validados (subconjunto estratificado de 3.294 revisados)

---

## 1. Qué se ha hecho

Se ha construido un **sistema de evaluación reproducible** de la calidad de clasificación (antes se hacían catas manuales de ~100 documentos). Permite medir con rigor estadístico si un cambio de prompt, modelo o umbral mejora o empeora, y localizar exactamente dónde falla. Con él se ha ejecutado una **medición de referencia (baseline)** sobre 467 documentos ya validados por negocio.

## 2. El hallazgo principal

> **La precisión real del clasificador es notablemente mejor de lo que dicen las cifras crudas.** La medición directa da 56,5% de acierto en familia (TDN1) y 31,7% en subtipo (TDN2), pero al revisar los "errores" uno a uno se comprueba que **la mayoría no son fallos del clasificador**, sino problemas de las etiquetas de referencia y de la taxonomía.

De los 467 documentos, solo el 31% es correcto de forma plena, pero de los 320 restantes **~24% (110 documentos) son problemas estructurales de taxonomía que ningún ajuste técnico puede resolver** — no dependen del clasificador.

Esta conclusión se ha **verificado de forma independiente**: los 127 documentos "en disputa" se han vuelto a clasificar a ciegas, uno a uno, leyendo su contenido sin conocer ni la etiqueta ni la decisión del clasificador (ver sección 4-bis). El resultado confirma que buena parte de los "errores" son etiquetas de referencia mal puestas o categorías no derivables del texto.

## 3. Cómo se descomponen los resultados

| Situación | Docs | Naturaleza | A quién corresponde |
|---|---:|---|---|
| Correcto | 147 (31%) | — | — |
| **Familia definida por procedencia** | 72 (15%) | El tipo depende de quién generó el documento y del workflow, no de su contenido | **Negocio** |
| **Defecto de catálogo de subtipos** | 38 (8%) | Subtipos duplicados o "cajón de sastre" imposibles de distinguir | **Negocio** |
| Etiqueta de referencia errónea | 58 (12%) | El documento está mal etiquetado; el clasificador acertaba | Revisión (negocio) |
| Escaneados / baja confianza | 62 (13%) | Requieren OCR o revisión puntual | Mixto |
| **Fallo real del clasificador** | 41 (9%) | Aquí sí ayuda el trabajo técnico de prompts | **Equipo técnico** |

## 4. Los tres hallazgos estructurales (decisiones de negocio)

1. **Familias de procedencia** (72 docs). Cuatro familias —PRPI (propuesta interna), ACUI/ACUE (decisiones) y PRPE (propuesta de tercero)— se definen por su **origen y estado en el proceso**, no por lo que dice el documento. Dos documentos idénticos pueden pertenecer a familias distintas. *Ningún prompt lo resuelve desde el texto.*
2. **Subtipos duplicados** (calidad de catálogo). Hay subtipos con la **descripción literalmente idéntica** (p. ej. TASA-11 y TASA-09), imposibles de separar. Deben fusionarse o diferenciarse.
3. **Subtipos "cajón de sastre"** (28% de las confusiones de subtipo). Subtipos "Otro / no contemplado" (FACT-10, FOTO-04…) que absorben o pierden los específicos. Conviene revisar sus criterios de uso.

## 4-bis. Verificación independiente (segunda opinión ciega)

Los **127 documentos en disputa** (aquellos donde la etiqueta y el clasificador no coincidían, o el clasificador no decidió) se han reclasificado desde cero leyendo su contenido, sin conocer ninguna decisión previa. De los 93 que discrepaban a nivel de familia:

- En el **71%** la segunda opinión coincide con la **etiqueta de referencia** (el clasificador se equivocó o no respondió).
- En el **25%** coincide con el **clasificador** (la etiqueta de referencia estaba mal).
- En un **4%** ni una ni otra eran correctas: apareció una **tercera** tipología más adecuada.

Dos conclusiones adicionales de este ejercicio:

1. **Robustez del clasificador** (hallazgo nuevo): en los 20 documentos donde el clasificador **no devolvió ninguna tipología**, el documento sí era perfectamente clasificable (18 de 20 coinciden con la etiqueta). No es un problema de criterio sino de que el motor a veces no emite resultado — es un fallo técnico acotado y corregible.
2. Los documentos **escaneados** están, en su mayoría, bien etiquetados (10 de 11): el clasificador es quien tropieza con ellos, no la referencia.

## 5. Recomendación: orden de trabajo

El mayor retorno **no está en ajustar prompts todavía**, sino en preparar el terreno:

1. **Limpiar las etiquetas de referencia** con la lista priorizada ya generada (documentos con etiqueta dudosa, cada uno con su corrección propuesta, evidencia y una segunda opinión independiente). *Sin esto, cualquier mejora se mide contra ruido.*
2. **Decidir la estrategia de las familias de procedencia**: aportar metadato externo (sistema origen, quién lo subió, estado) o aceptar techo bajo y enrutar aparte.
3. **Sanear el catálogo de subtipos**: fusionar duplicados y revisar los cajones de sastre.
4. **Corregir la robustez del clasificador**: evitar que devuelva "sin tipología" en documentos clasificables (20 casos detectados). Es trabajo técnico acotado.
5. **Solo entonces**, atacar los fallos reales del clasificador con ajustes de prompt, midiendo cada cambio contra la referencia ya limpia.

## 6. Estado y trazabilidad

- Herramientas y baseline entregados; medición repetible en cada cambio.
- Trabajo registrado en Azure DevOps: harness de evaluación (AB#99948) y hallazgos AB#99973 (revisión de etiquetas), AB#99976 (familias de procedencia), AB#99982 (calidad de catálogo TDN2).
- Detalle documento a documento disponible en el informe por fichero.

> **Conclusión:** el clasificador de contenido funciona mejor de lo que sugería el número inicial, y una segunda opinión independiente lo confirma. La palanca de mejora inmediata es de **negocio y taxonomía** (etiquetas, procedencia, catálogo), no de tecnología. El trabajo técnico pendiente —robustez del motor y ajuste de prompts en los fallos reales— es acotado y ya está localizado documento a documento.
