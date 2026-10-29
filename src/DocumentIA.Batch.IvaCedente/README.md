# DocumentIA.Batch.IvaCedente

Consola .NET 8 sin interfaz que, a partir de un CSV de activos y documentos, llama a DocumentIA y determina, según la escritura del título previo de cada activo, si en la adquisición por la cedente hubo IVA y, en su caso, si consta su deducción. Es un cliente de prueba de DocumentIA: ejercita el ingest por `ObjectIdGDC` y el prompt libre por petición. Work item: `AB#101191`.

Hace dos cosas, cada una con su comando:

- `procesar`: un CSV de entrada a un CSV por documento (el CSV de salida es también el estado de la reanudación).
- `consolidar`: el CSV por documento a un CSV por activo, sin volver a llamar a DocumentIA.

## Qué envía a DocumentIA

Cada petición lleva `ClassificationOnly = true`, `SkipDuplicateCheck = true` (la clave de deduplicación no incluye el prompt), `SkipGdcUpload = true`, el `ObjectIdGDC` del documento, el `IdActivo` en la trazabilidad y el prompt de `prompts/system.md` y `prompts/user.md`. El prompt de sistema fija quién es la cedente (Sareb nunca lo es: si Sareb transmite o adquiere en el documento, no es el título previo), pide el tipo y la cuota de IVA y una confianza razonada por tramos; se ajustó tras la tanda de humo en PRO del 2026-10-29. Si `TipologiaForzada` está informada, la petición con prompt lleva además `ExpectedType` con ese código y deja de ser de solo clasificación (DocumentIA rechaza las dos cosas a la vez): DocumentIA no clasifica, no extrae ni sube a GDC, y ejecuta el prompt. No se envía contenido del documento (ni base64 ni ruta de blob): DocumentIA lo recupera de GDC por su Object ID. Las ejecuciones quedan con un `SubmittedBy` propio de este programa para localizarlas en PRO.

## Compilar

Desde la raíz del repo:

```bash
dotnet build DocumentIA.Batch.sln
```

## Configuración

Se parte de `config.sample.json` (se versiona sin claves). Se copia a `config.json`, junto al ejecutable, o se indica otra ruta con `--config`. `config.json` y cualquier fichero con la Function Key no se versionan.

| Clave | Por defecto | Significado |
|---|---|---|
| `Entorno` | `PRO` | Nombre del entorno de `Environments` que se usa. |
| `Environments` | DEV y PRO | Lista de entornos con `Name`, `BackendUrl` y `FunctionKey`. |
| `Colas` | `3` | Documentos en paralelo. Se puede cambiar con `--colas`. PRO tiene SQL S0 y antecedentes de workers colgados: se mantiene bajo. |
| `CodigosExcluidos` | `["TASA-11", "SCXX-00"]` | TDN2 que no se envían a DocumentIA. Quedan como `no aplica`. |
| `CodigosPreclasificar` | `["SCXX-00"]` | TDN2 que siguen el camino de dos llamadas (ver más abajo). |
| `ForzarSinLimitePaginas` | `true` | Procesa el documento completo aunque supere el límite de páginas (las escrituras suelen superarlo; coste proporcional). |
| `ModelKey` | `null` | Modelo a usar; `null` deja el que decida DocumentIA. |
| `TipologiaForzada` | `null` | Código de tipología que se envía como `ExpectedType` en la llamada con prompt. DocumentIA rechaza `ExpectedType` junto con solo clasificación (400), así que esa llamada va por el camino completo sin `ClassificationOnly`: no clasifica (se ahorra esa llamada), no extrae (`resumen.documental` no tiene extracción), no sube a GDC y ejecuta el prompt siempre. Se puede fijar por ejecución con `--tipologia`. No afecta a la llamada de preclasificación. |
| `PromptSystemPath`, `PromptUserPath` | `prompts/system.md`, `prompts/user.md` | Prompts, relativos al ejecutable salvo ruta absoluta. |
| `PollingIntervalSeconds` | `30` | Intervalo base de consulta del estado de la orquestación. |
| `TimeoutMinutos` | `30` | Espera máxima por llamada antes de marcar `timeout`. |
| `MaxReintentos` | `2` | Reintentos por paso ante fallo transitorio. |
| `MaxPaginasClasificacion` | `10` | Límite de páginas de la llamada de preclasificación. |

### Function Key

La Function Key no se escribe en el repo. Se toma de la variable de entorno `DOCIA_FUNCTION_KEY`; si no está definida, de `FunctionKey` del entorno en `config.json`. Si no hay ninguna, el programa se detiene con un error. Ejemplo en Git Bash:

```bash
export DOCIA_FUNCTION_KEY='<clave del entorno>'
```

En PowerShell: `$env:DOCIA_FUNCTION_KEY = '<clave del entorno>'`.

## Comando `procesar`

```bash
dotnet run --project src/DocumentIA.Batch.IvaCedente -- procesar --entrada activos.csv --salida resultado.csv
dotnet run --project src/DocumentIA.Batch.IvaCedente -- procesar --entrada activos.csv --salida resultado.csv --config config.json --colas 2
dotnet run --project src/DocumentIA.Batch.IvaCedente -- procesar --entrada activos.csv --salida resultado.csv --reintentar-errores
dotnet run --project src/DocumentIA.Batch.IvaCedente -- procesar --entrada activos.csv --salida resultado.csv --tipologia resumen.documental
```

Opciones: `--entrada` y `--salida` (obligatorias), `--config` (por defecto `config.json` junto al ejecutable), `--colas` (sustituye a `Colas`), `--tipologia <codigo>` (sustituye a `TipologiaForzada`; vacío equivale a no forzar) y `--reintentar-errores` (ver Reanudación). Con tipología forzada la cabecera de consola muestra `Tipología forzada: <codigo>`; la columna `Tipologia` del CSV conserva el TDN2 de entrada (DocumentIA no devuelve TDN2 para `resumen.documental`), así que las filas procesadas así se distinguen por su `Fecha`, no por la tipología. En PRO (2026-10-29) este camino tardó unos 12 s por documento frente a 30-40 s con clasificación.

El CSV de entrada usa `;` y tiene las cabeceras `ID_SAREB`, `Object ID del Documento` y `TDN2`. Los códigos TDN2 se pasan a mayúsculas; las filas repetidas (mismo activo y mismo Object ID) se descartan; las filas completamente vacías se saltan. Un Object ID vacío, una fila con menos columnas de las necesarias o una comilla sin cerrar detienen la lectura indicando el número de fila.

Un mismo Object ID que aparece en varios activos se envía una sola vez a DocumentIA y su resultado se copia a cada activo.

En consola se muestra el entorno, la URL, las colas y las filas pendientes, y cada 10 documentos una línea de avance: `[hechos/total] OK=… noAplica=… noTitulo=… errores=… media=… s`.

No abras la salida en Excel mientras corre; abre una copia. Si Excel (u otro programa) tiene el fichero bloqueado, el programa avisa (`No se puede escribir en <ruta>: ciérralo en Excel. Reintentando...`) y reintenta 6 veces con 10 segundos de espera. Si se agotan, conserva el resultado en memoria, detiene la pasada para no facturar más documentos y, al final, intenta la escritura completa con los mismos reintentos; si también falla, termina con un mensaje claro y código `1`.

Códigos de salida:

| Código | Significado |
|---|---|
| `0` | Pasada completa sin errores ni timeouts. |
| `1` | Error de uso, de configuración o no se pudo escribir la salida. |
| `2` | Pasada completa con algún `error` o `timeout`. |
| `3` | Pasada interrumpida (Ctrl+C o imposibilidad de escribir la salida); tiene prioridad sobre el `2`. Lo pendiente se retoma relanzando. |

### Filtro por TDN2 y caminos

- Si el TDN2 está en `CodigosExcluidos`, la fila queda como `no aplica` y no se llama a DocumentIA. Todo código no excluido, incluidos los no previstos y el TDN2 vacío, pasa a DocumentIA.
- Camino por defecto: una sola llamada con el prompt.
- Si el TDN2 está en `CodigosPreclasificar`, dos llamadas: primero clasificación sola; si la tipología devuelta no está excluida ni es a su vez un código a preclasificar, segunda llamada con el prompt; si no, la fila queda como `no es título` con la tipología devuelta en `Tipologia`.
- La exclusión gana sobre la preclasificación: con la configuración por defecto, `SCXX-00` está excluido y por tanto la preclasificación no tiene efecto hasta que se quite de `CodigosExcluidos`.

### Reintentos y cancelación

- Hay dos niveles de reintento. El reintento de consulta: si falla una consulta del estado de la orquestación, se repite solo la consulta (no se reenvía el documento) hasta 5 fallos seguidos o hasta `TimeoutMinutos`. El reintento de paso: se reenvía el paso completo (hasta `MaxReintentos` veces) ante una excepción del ingest, incluido un timeout de HTTP, ante una orquestación `Failed` o `Terminated`, ante cinco fallos seguidos de consulta y ante los estados `ERROR` y `PENDIENTE_REINTENTO` del contrato de DocumentIA. `MaxReintentos = 2` son 3 intentos en total.
- Entre intentos del paso se espera `PollingIntervalSeconds * (intento + 1)` segundos.
- Si se agota la espera de `TimeoutMinutos`, la fila queda como `timeout`.
- Ctrl+C no cuenta como error: se espera a escribir lo ya procesado, el documento en vuelo queda pendiente, el código de salida es `3` y el mismo comando continúa donde se dejó. La orquestación que estaba en vuelo sigue ejecutándose en PRO y se volverá a facturar al relanzar ese documento.

## CSV por documento

Separador `;`, UTF-8 con BOM, una fila por par activo y documento. Columnas:

| Columna | Contenido |
|---|---|
| `ID_SAREB`, `Object ID del Documento`, `TDN2` | Copia de la entrada. |
| `Procesado` | Estado de la fila (valores abajo). |
| `EsTituloPrevio` | `sí`, `no` o vacío si no se llegó a determinar. Si el modelo dice que es título previo pero nombra a Sareb como cedente, transmitente o adquirente, el programa lo corrige a `no` (Sareb nunca es parte del título previo: el documento es una operación posterior de Sareb o el traspaso a Sareb) y deja en `Cedente` el nombre con el que apareció Sareb para que se vea el motivo. |
| `HuboIVA` | `sí`, `no`, `no consta` o `no es título`. |
| `DeduccionIVA` | `sí`, `no`, `no consta` o `no aplica`. |
| `Cedente`, `FechaAdquisicion` | Datos que devuelve el prompt cuando el documento es un título previo. |
| `TipoIVA`, `CuotaIVA` | Porcentaje y cuota en euros del IVA de la adquisición por la cedente, como números con punto decimal, cuando el prompt los encuentra. Vacíos si no hubo IVA, no constan o el documento no es título previo. |
| `Evidencia` | Cita literal corta del documento (ver truncado más abajo). Con `respuesta no parseable`, el texto bruto devuelto. |
| `Confianza` | Número entre 0 y 1, con punto decimal. |
| `Tipologia` | Tipología que devuelve DocumentIA. |
| `InstanceId`, `CorrelationId` | Identificadores de la última llamada, para localizarla en PRO. |
| `Error` | Mensaje cuando `Procesado` es `error` o `timeout`. Si el estado del contrato de DocumentIA no es `OK`, se anota también aunque la fila acabe `OK` (por ejemplo `Estado del contrato: REVISION`). |
| `Fecha` | Instante de procesado en UTC (ISO 8601). |

Valores de `Procesado`:

| Valor | Significado | ¿Se reintenta al relanzar? |
|---|---|---|
| `pendiente` | Aún sin procesar. | Sí |
| `OK` | Respuesta del prompt leída. | No |
| `no aplica` | TDN2 excluido. | No |
| `no es título` | La preclasificación dijo que no es un título previo. | No |
| `sin contenido` | DocumentIA devolvió el estado `SIN_CONTENIDO_DOCUMENTO` (se acepta también `SIN_CONTENIDO`): no pudo obtener contenido del documento. | No |
| `respuesta no parseable` | La respuesta del prompt no era JSON válido. | No |
| `error` | Fallo tras agotar reintentos. | Solo con `--reintentar-errores` |
| `timeout` | Espera agotada. | Solo con `--reintentar-errores` |

Dos detalles del fichero:

- Antiformula: los campos de texto que empiezan por `=`, `+`, `-`, `@`, tabulador, retorno de carro o `'` se escriben con un apóstrofo delante para que Excel no los ejecute como fórmula. `consolidar` y la reanudación lo quitan al leer, así que el apóstrofo solo se ve al abrir el CSV en Excel.
- `Evidencia` se corta a 4000 caracteres y se le añade el sufijo ` [truncado]`.

## Reanudación

El CSV de salida es el estado. Sus columnas se leen por nombre de cabecera, así que una salida escrita por una versión anterior sin alguna columna (por ejemplo `TipoIVA` y `CuotaIVA`) se reanuda igual. Cada resultado se anexa al fichero en cuanto llega, y al terminar la pasada se reescribe completo (de forma atómica, vía un fichero `.tmp`) en el orden de la entrada. Si se relanza con otra entrada, las filas previas cuyo activo y Object ID ya no están en ella se conservan al final del fichero. Al relanzar `procesar` con la misma entrada y la misma salida se saltan las filas ya resueltas (`OK`, `no aplica`, `no es título`, `sin contenido` y `respuesta no parseable`); las que fallaron por `error` o `timeout` se dejan como están salvo que se pase `--reintentar-errores`. Las filas `pendiente` y las que faltan en la salida se procesan siempre. Para volver a procesar una fila ya resuelta hay que quitarla del CSV de salida.

## Comando `consolidar`

```bash
dotnet run --project src/DocumentIA.Batch.IvaCedente -- consolidar --entrada resultado.csv --salida activos-consolidado.csv
```

Lee el CSV por documento y escribe una fila por activo. No necesita configuración ni red, y se puede repetir las veces que haga falta. Imprime cuántos activos se consolidaron, cuántos quedan para revisar y cuántas filas siguen en `pendiente`, `error` o `timeout`. Columnas:

| Columna | Contenido |
|---|---|
| `ID_SAREB` | Activo. |
| `Documentos` | Documentos del activo en el CSV por documento. |
| `Leidos` | Documentos con `Procesado` `OK` o `respuesta no parseable`. |
| `ConRespuesta` | Documentos `OK` que son título previo (`EsTituloPrevio = sí`); solo estos cuentan para la decisión. |
| `HuboIVA` | `sí`, `no`, `no consta` o `sin título` (ningún documento del activo es un título previo con respuesta). |
| `DeduccionIVA` | `sí`, `no`, `no consta`, `no aplica` (ningún título indica deducción aplicable) o `sin título`. |
| `DocIVA` | Object ID del título elegido que sostiene `HuboIVA` cuando vale `sí` o `no`; vacío en `no consta` y `sin título`. |
| `TipoIVA`, `CuotaIVA` | Tipo (porcentaje) y cuota en euros del título elegido, solo cuando `HuboIVA = sí`; números con punto decimal, como en el CSV por documento. |
| `Cedente`, `FechaAdquisicion` | Del título elegido. |
| `DocDeduccion` | Object ID del título elegido que sostiene `DeduccionIVA` cuando vale `sí` o `no`; vacío en otro caso. |
| `Sustento` | Frase legible: `IVA sí 21% 64.050,00 € (doc 12345, cedente BANCO X, 2008-03-12); deducción sí (doc 12345)`. Variantes: `IVA no (doc …)`, `IVA no consta; deducción no consta`, `sin título previo`. Tipo y cuota en formato español; se omite lo que falte. |
| `MotivoRevisar` | Motivos de `Revisar`, separados por `;` y en este orden: `filas sin resolver (n)`, `IVA contradictorio`, `deducción contradictoria`, `cuotas de IVA distintas`. Vacío cuando `Revisar = no`. |
| `ObjectIdsSoporte` | Object IDs, separados por coma, de todos los documentos que sostienen el valor cuando `HuboIVA` es `sí` o `no`. |
| `Revisar` | `sí` si hay algún motivo en `MotivoRevisar`: títulos con `sí` y `no` a la vez (en IVA o en deducción), filas en `pendiente`, `error` o `timeout` (el proceso está incompleto y puede haber un título sin leer) o cuotas de IVA distintas entre los títulos con IVA; `no` en otro caso. |

Regla de agregación, igual para IVA y para deducción: gana el `sí` si algún título lo dice; si no hay `sí` y hay algún `no`, `no`; si no, `no consta`. Los valores `no aplica` de la deducción no cuentan en la agregación.

Elección del título de sustento: entre los títulos cuyo `HuboIVA` coincide con el valor consolidado, el que tenga `CuotaIVA`; a igualdad, el que tenga `TipoIVA`; a igualdad, mayor `Confianza`; a igualdad, el primero en el orden del fichero. Para la deducción se prefiere el mismo documento que `DocIVA` si la sostiene; si no, la misma regla sobre los títulos cuya `DeduccionIVA` coincide con el valor consolidado.

## Coste en PRO y tanda de humo

Las llamadas se ejecutan contra PRO y tienen coste real de Layout y de modelo de lenguaje por documento. El diseño (`docs/superpowers/specs/2026-10-28-iva-cedente-design.md`) no fija una cifra de coste unitario: está pendiente de medir en la tanda de humo, que da el coste unitario antes de decidir lanzar el total.

Antes de lanzar el CSV completo se hace una tanda de humo de 3 a 5 documentos en PRO: una escritura con IVA conocido, una sin IVA y una tasación mal clasificada, con `--colas 1`. En ella se comprueba:

- Que el prompt se ejecuta también cuando la clasificación devuelve Desconocido o una tipología sin prompt propio.
- Que `ForzarSinLimitePaginas` alcanza al contenido que usa el prompt.
- Que el proxy de la red SAREB no exige configuración extra en el cliente HTTP.
- El coste unitario por documento, a partir del cual se estima el total.

Como PRO tiene un SQL S0 y antecedentes de workers colgados, se mantienen pocas colas y se puede parar con Ctrl+C y reanudar.
