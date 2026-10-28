# Diseño: IvaCedente, cliente de consola de DocumentIA (diseño corto)

Fecha: 2026-10-28 · Línea: batch (repo `DocumentIA.Batch`) · Work item: AB#101191 (PBI, `AI DocClassExt`)
Estado: aprobado por el usuario el 2026-10-28

## Intención

Dado un CSV `ID_SAREB;Object ID del Documento;TDN2`, pasar por DocumentIA en PRO cada documento relevante con un
prompt que determine, según la escritura del título previo, si en la adquisición por la cedente hubo IVA y, en su
caso, si consta su deducción. Salida: el mismo CSV ampliado con el dato por documento y un segundo CSV consolidado
por activo. Es un cliente de prueba de DocumentIA: ejercita el ingest por `ObjectIdGDC` y el prompt libre por petición.

## Objetivos, no objetivos, anti-objetivos

- Objetivos: consola sin interfaz; N colas en paralelo; avance visible; reanudación tras caída sin repetir trabajo;
  consolidación repetible sin volver a llamar a DocumentIA.
- No objetivos: interfaz gráfica, base de datos propia, cambios en el backend de DocumentIA, exportación Excel.
- Anti-objetivos: subir nada a GDC, reutilizar resultados antiguos por deduplicación, inventar respuestas cuando el
  documento no es el título previo.

## Restricciones

- Entorno PRO de DocumentIA (los Object ID viven en el GDC de PRO). Coste real de Layout y GPT por documento.
- La clave de intención de la deduplicación no incluye el prompt: hay que enviar `SkipDuplicateCheck = true`.
- `SkipGDCUpload = true` y `ClassificationOnly = true` (omite extracción, validación, AssetResolver e integración;
  el prompt sí se ejecuta en ese modo). `SubmittedBy = "iva-cedente"` para localizar las ejecuciones en PRO.
- Las escrituras superan a menudo el límite de páginas: `ForzarProcesadoSinLimitePaginas` configurable, por defecto
  activo para los códigos de título (coste proporcional, auditoría obligatoria en el backend).
- Sin coautores ni menciones de modelos en ningún artefacto. Español en todo.

## Enfoques considerados

1. Consola .NET 8 en el repo Batch (elegido): proyecto `src/DocumentIA.Batch.IvaCedente` en la solución, rama y
   worktree desde `main`; se copia y adapta el cliente HTTP del Batch (`DocumentIaBackendClient`, polling Durable,
   reintentos), probado contra PRO y con el casing correcto del output.
2. Script Python en el repo Batch: más rápido de escribir, pero cliente y proxy desde cero y único Python de producto.
3. Repo nuevo: limpio, pero sin reutilización y con alta de repo y pipeline para una pieza de prueba.
Decisión del usuario: 1. Tamaño: cambio pequeño, diseño corto.

## Cómo funciona (decidido)

- Dos comandos: `procesar --entrada <csv> --salida <csv> [--config <json>] [--reintentar-errores]` y
  `consolidar --entrada <csv por documento> --salida <csv por activo>`.
- Filtro por TDN2 en configuración, no en código, como lista de exclusión (decisión del usuario, 2026-10-28, que
  sustituye a la lista de inclusión inicial): `CodigosExcluidos = [TASA-11, SCXX-00]`. Todo lo demás, incluidos códigos
  no previstos, pasa a DocumentIA. Los excluidos quedan en la salida como `no aplica`.
- Dos caminos según el TDN2 de GDC:
  - Por defecto, una sola llamada con prompt. Con `Instrucciones.Prompt` informado DocumentIA ejecuta el prompt siempre,
    incluso sin tipología resuelta, así que no hay puerta posible en el servidor.
  - Códigos en `CodigosPreclasificar` (por defecto `[SCXX-00]`, hoy sin efecto porque SCXX-00 está excluido): dos
    llamadas. Primero clasificación sola con el límite de páginas normal; si la tipología devuelta no está excluida ni
    es a su vez un código a preclasificar, segunda llamada con prompt; si no, `Procesado = no es título` con la
    `Tipologia` devuelta. La exclusión gana sobre la preclasificación.
  - Mejora opcional, fuera de la primera versión: si la primera llamada devuelve `MarkdownCompleto = true`, bajar el
    markdown por `GET /api/documentos/{sha256}/markdown` y pasarlo en `Instrucciones.Markdown` para ahorrar Layout.
- Petición por documento: `Documento.ObjectIdGDC`, `Trazabilidad.IdActivo = ID_SAREB`, `Instrucciones.Prompt`
  (SystemPrompt y UserPromptTemplate desde dos ficheros de texto en `prompts/`, `ModelKey` configurable).
- El prompt pide JSON con: `esTituloPrevio`, `huboIVA` (sí/no/no consta), `deduccionIVA` (sí/no/no consta/no aplica),
  `cedente`, `fechaAdquisicion`, `evidencia` (cita literal corta), `confianza` (0-1). Si la respuesta no es JSON
  válido, la fila queda en `Procesado = respuesta no parseable` con el texto bruto en `Evidencia`.
- Polling del `statusQueryUri` con el lector de estado Durable del Batch; `DatosExtraidos.ResultadoPrompt` del
  output es la respuesta; `Resultado.Estado` distinto de OK se anota en `Error`.
- Reanudación: el CSV de salida es el estado. Cada fila se escribe (reescritura atómica del fichero completo tras cada
  resultado, con `;` y UTF-8 BOM como el Batch) al llegar su resultado; al relanzar se saltan las filas con
  `Procesado` en OK, no aplica, no es título o sin contenido. Los errores se repiten solo con `--reintentar-errores`.
- Colas: `Colas` en configuración (por defecto 3; PRO tiene SQL S0 y antecedentes de workers colgados).
- Avance en consola: procesados/total, OK, errores, en curso, tiempo medio por documento.
- Columnas de salida por documento: las tres de entrada más `Procesado`, `EsTituloPrevio`, `HuboIVA`, `DeduccionIVA`,
  `Cedente`, `FechaAdquisicion`, `Evidencia`, `Confianza`, `Tipologia` (la que devuelva DocumentIA), `InstanceId`,
  `CorrelationId`, `Error`, `Fecha`.
- Consolidación por activo: `ID_SAREB`, `Documentos`, `Leidos`, `ConRespuesta`, `HuboIVA`, `DeduccionIVA`,
  `ObjectIdsSoporte`, `Revisar`. Regla: algún "sí" gana; si no hay "sí" y hay algún "no", no; si todo "no consta",
  no consta. `Revisar = sí` cuando hay "sí" y "no" en el mismo activo. Misma regla para la deducción.
- Configuración: `config.json` (BackendUrl, FunctionKey o variable `DOCIA_FUNCTION_KEY`, Colas, CodigosExcluidos,
  CodigosPreclasificar, ForzarSinLimitePaginas, ModelKey, rutas de prompts). `config.json` real en `.gitignore`;
  se versiona `config.example.json` sin claves.
- Tests xUnit (`tests/DocumentIA.Batch.IvaCedente.Tests`): lectura y filtro del CSV, reanudación, parseo de la
  respuesta JSON, regla de consolidación. Sin tests de red.

## Lo que queda al constructor

- Redacción final del prompt, probada en una tanda de humo de 3 a 5 documentos en PRO (una escritura con IVA
  conocido, una sin IVA, una tasación mal clasificada) antes de lanzar el CSV completo.
- Comprobar en esa tanda: que el prompt se ejecuta también cuando la clasificación devuelve Desconocido o una
  tipología sin prompt propio; que `ForzarProcesadoSinLimitePaginas` alcanza al markdown que usa el prompt; que el
  proxy de la red SAREB no exige configuración extra en el `HttpClient` (el Batch ya funciona desde esa red).
- Reintentos ante 429 y 5xx con la política del Batch (`RetryPolicy`).

## Riesgos

- Coste en PRO: la tanda de humo da el coste unitario antes de decidir el total.
- Carga en PRO durante horas: colas bajas por defecto y posibilidad de parar y reanudar.
- Documentos que no son títulos pese a su TDN2: el campo `esTituloPrevio` del prompt los descarta sin inventar.

## Trazabilidad

Work item AB#101191; rama `feature/101191-iva-cedente` desde `main` del repo Batch en worktree
propio; commits con `AB#`. Impacto documental: README del Batch (técnica) y manual de uso de la herramienta.
