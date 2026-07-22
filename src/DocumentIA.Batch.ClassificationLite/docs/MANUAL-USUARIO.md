# Manual de usuario — Batch Classification Lite

Aplicación de escritorio (Windows) para **clasificar documentos PDF de forma masiva**
contra DocumentIA. Recorre una carpeta (local o de red), envía cada PDF al backend,
recupera la tipología detectada (**TDN1 / TDN2**) y guarda todo en una base de datos
local para consulta, exportación y reanudación.

> Capacidad orientativa: hasta ~100.000 documentos por ejecución.

---

## Índice

1. [Requisitos e instalación](#1-requisitos-e-instalación)
2. [Primer arranque y configuración inicial](#2-primer-arranque-y-configuración-inicial)
3. [La ventana principal de un vistazo](#3-la-ventana-principal-de-un-vistazo)
4. [Flujo de trabajo paso a paso](#4-flujo-de-trabajo-paso-a-paso)
5. [Configuración detallada](#5-configuración-detallada)
6. [Estados de un documento](#6-estados-de-un-documento)
7. [Columnas del grid](#7-columnas-del-grid)
8. [Filtros y búsqueda](#8-filtros-y-búsqueda)
9. [Detalle de un documento](#9-detalle-de-un-documento)
10. [Exportación (Excel / CSV)](#10-exportación-excel--csv)
11. [Ficheros de estado y dónde se guardan](#11-ficheros-de-estado-y-dónde-se-guardan)
12. [Reanudar una ejecución interrumpida](#12-reanudar-una-ejecución-interrumpida)
13. [Omisión de documentos ya procesados (históricos)](#13-omisión-de-documentos-ya-procesados-históricos)
14. [Recomendaciones operativas](#14-recomendaciones-operativas)
15. [Resolución de problemas](#15-resolución-de-problemas)
16. [Glosario](#16-glosario)

---

## 1. Requisitos e instalación

**Sistema:** Windows 10/11 de 64 bits (x64).

Existen dos formatos de entrega. Usa el que te hayan proporcionado:

| Formato | Qué es | Requisitos |
|---|---|---|
| **Autónomo (un solo `.exe`)** | Un único fichero `DocumentIA.Batch.ClassificationLite.exe` (~160 MB) que incluye el runtime dentro. | Ninguno. Se ejecuta tal cual. |
| **Dependiente del framework** | Una carpeta con el `.exe` (~150 KB) y sus librerías. | Requiere **.NET 8 Desktop Runtime (x64)** instalado. Si falta, Windows mostrará un aviso con el enlace de descarga. |

**Instalación:** no requiere instalador. Copia el ejecutable (o la carpeta completa, en el
formato dependiente) a una ubicación con permisos de escritura, por ejemplo
`C:\Apps\BatchClassificationLite\`, y haz doble clic para abrirlo.

> Al primer arranque, si tu antivirus corporativo bloquea el ejecutable descargado,
> puede que tengas que autorizarlo (clic derecho → Propiedades → *Desbloquear*).

---

## 2. Primer arranque y configuración inicial

La aplicación funciona contra un **entorno** de DocumentIA. Por defecto viene
preconfigurada apuntando a **PRO**, pero **sin la clave de acceso**, así que lo primero
es rellenarla:

1. Abre la aplicación.
2. Pulsa **Configuracion**.
3. En el grupo **Procesamiento**, con el entorno **PRO** seleccionado, pega la
   **Function Key** que te haya facilitado el equipo de la plataforma.
4. (Opcional) Revisa **Backend URL** si te han indicado una distinta.
5. Pulsa **Guardar**.

La configuración se guarda en un fichero `config.json` **junto al ejecutable**, de modo
que no hay que volver a introducirla en cada arranque. La Function Key se guarda
**ofuscada** (nunca en texto plano). Ver
[Ficheros de estado](#11-ficheros-de-estado-y-dónde-se-guardan).

> **Preparar un paquete con la clave ya puesta:** si distribuyes la aplicación a varios
> equipos, monta la app una vez, introduce la Function Key en Configuracion, pulsa
> **Guardar** y reparte ese `config.json` (con la clave ya ofuscada) junto al ejecutable.
> La ofuscación es portable: funciona en cualquier equipo sin tener que reintroducir la
> clave. *(Es ofuscación para evitar el texto plano, no cifrado de grado criptográfico.)*

> **Importante:** sin una Function Key válida, el backend rechazará las peticiones con un
> error de autorización (401) y los documentos terminarán en error. Ver
> [Resolución de problemas](#15-resolución-de-problemas).

---

## 3. La ventana principal de un vistazo

La ventana se divide, de arriba abajo, en cinco zonas:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ [Seleccionar carpeta] ☑Incluir subcarpetas [Configuracion] [Ejecutar]        │ ← Barra de acciones
│ [Pausar] [Reanudar] [Cancelar]   [Exportar Excel] [Exportar CSV]             │
├─────────────────────────────────────────────────────────────────────────────┤
│ Arrastra ficheros o carpetas aqui, o pulsa Seleccionar carpeta.              │ ← Ruta / selección
├─────────────────────────────────────────────────────────────────────────────┤
│ Total encontrados  Pendientes  En ejecucion  Procesados OK  Errores  Omitidos│ ← Indicadores
│        6                0            0             6            0        0     │
├─────────────────────────────────────────────────────────────────────────────┤
│ Filtro: [______]   Estado: [Todos ▼]                                         │
│ ┌───────────┬────────┬───────┬─────┬──────┬────────┬──────────┬───────────┐  │ ← Grid de resultados
│ │ FileName  │ Status │ Pages…│Pages│ TDN1 │ TDN2   │Confidence│ProcessDate│  │
│ └───────────┴────────┴───────┴─────┴──────┴────────┴──────────┴───────────┘  │
├─────────────────────────────────────────────────────────────────────────────┤
│ Ejecucion finalizada.                                                        │ ← Barra de estado
└─────────────────────────────────────────────────────────────────────────────┘
```

- **Barra de acciones:** todos los botones de control (selección, configuración,
  ejecución, exportación). Pausar / Reanudar / Cancelar solo se activan mientras hay un
  proceso en curso.
- **Ruta / selección:** muestra la carpeta o ficheros seleccionados actualmente.
- **Indicadores:** seis contadores en vivo del progreso (ver
  [Estados de un documento](#6-estados-de-un-documento)).
- **Grid de resultados:** una fila por documento, con su estado y clasificación.
- **Barra de estado:** mensaje de la operación actual ("Escaneando…", "Procesando…",
  "Ejecucion finalizada.", etc.).

---

## 4. Flujo de trabajo paso a paso

### Paso 1 — Selecciona los documentos

Tienes dos formas equivalentes:

- **Arrastrar y soltar:** arrastra una o varias **carpetas** o **ficheros PDF** desde el
  Explorador de Windows y suéltalos en cualquier parte de la ventana.
- **Botón:** pulsa **Seleccionar carpeta** y elige una carpeta.

En cuanto sueltas o eliges, la aplicación **escanea inmediatamente** y **muestra los PDF
encontrados en el grid** con estado `Pending` (previsualización). La barra de estado
indica cuántos se han encontrado, p. ej. *"124 documentos encontrados. Pulsa Ejecutar
para procesar."* Todavía **no se ha enviado nada** al backend.

> Esta previsualización te permite comprobar **qué se va a procesar** antes de lanzar
> nada. Si sueltas otra selección distinta, la anterior se descarta y el grid se
> actualiza con la nueva.

### Paso 2 — Ajusta "Incluir subcarpetas"

La casilla **Incluir subcarpetas** (activada por defecto) controla si el escaneo entra en
las subcarpetas de lo seleccionado. Si la cambias, **vuelve a arrastrar o seleccionar** la
carpeta para que el escaneo se rehaga con el nuevo criterio.

> Solo se tienen en cuenta ficheros con extensión **`.pdf`**. El resto se ignora.

### Paso 3 — Revisa la configuración (opcional)

Si es la primera vez, o vas a cambiar de entorno, umbral de paralelismo, etc., pulsa
**Configuracion**. Ver [Configuración detallada](#5-configuración-detallada).

### Paso 4 — Ejecuta

Pulsa **Ejecutar**. La aplicación procesa las filas `Pending`:

- Envía cada documento al backend según el paralelismo configurado.
- Los indicadores y el grid se **refrescan en vivo** (aprox. cada segundo).
- Cada documento pasa de `Pending` → `InFlight` → `Succeeded` (o a un estado de error).

Mientras dura el proceso, la aplicación **impide que el equipo se suspenda**, para que un
lote largo no se corte al bloquearse la pantalla.

### Paso 5 — Controla el proceso

Con una ejecución en marcha se habilitan:

- **Pausar:** deja de enviar nuevos documentos. Los que ya están `InFlight` terminan.
- **Reanudar:** continúa enviando los pendientes tras una pausa.
- **Cancelar:** detiene la ejecución en cuanto es posible. Lo ya procesado se conserva.

### Paso 6 — Consulta resultados

- Usa el **Filtro** de texto y el desplegable de **Estado** para acotar la lista.
- **Doble clic** en una fila abre el [detalle del documento](#9-detalle-de-un-documento)
  (petición y respuesta completas).

### Paso 7 — Exporta

Pulsa **Exportar Excel** o **Exportar CSV** para volcar los resultados a fichero. Ver
[Exportación](#10-exportación-excel--csv).

---

## 5. Configuración detallada

Se accede con el botón **Configuracion**. Los cambios se aplican al pulsar **Guardar** y
se conservan entre sesiones. Los valores fuera de rango se **ajustan automáticamente** al
límite más cercano.

### Grupo "Procesamiento"

| Opción | Descripción | Por defecto | Rango / notas |
|---|---|---|---|
| **Environment** | Entorno de DocumentIA contra el que se trabaja. Puedes tener varios y cambiar entre ellos. Botones **Añadir** / **Eliminar** para gestionarlos. | `PRO` | — |
| **Backend URL** | URL base del backend del entorno seleccionado. | `https://srbappprodocai.azurewebsites.net` | Cámbiala solo si te lo indican. |
| **Function Key** | Clave de acceso al backend del entorno. **Obligatoria.** El campo aparece **enmascarado** y **nunca muestra** la clave ya guardada: déjalo en blanco para conservar la actual, o escribe una nueva para reemplazarla. Un rótulo indica "configurada" / "sin configurar". | *(vacía)* | Imprescindible para PRO. Se guarda **ofuscada** en `config.json` (nunca en texto plano). |
| **Parallel Queries** | Nº de documentos que se envían en paralelo. | `2` | **1–10**. Subirlo acelera pero carga más el backend. |
| **Internal Batch Size** | Tamaño de lote interno de trabajo. | `1000` | **50–10.000**. |
| **Polling Interval** | Cada cuánto se consulta el estado de un documento en curso. | `60` s | **10–600** s. Al principio consulta más rápido (3, 5, 10, 20, 30 s) y luego se estabiliza en este intervalo. |

### Grupo "Clasificacion"

| Opción | Descripción | Por defecto | Valores |
|---|---|---|---|
| **Classification Level** | Nivel de clasificación solicitado. | `TDN1_TDN2` | `TDN1_TDN2` (familia + subtipo), `TDN1` (solo familia), `DEFAULT`. |
| **Provider** | Motor de clasificación. Déjalo en `auto` salvo indicación. | `auto` | `auto`, `hybrid`, `hybrid-rules-gpt-di`, `hybrid-rules-di-gpt`, `hybrid-tdn`, `rules`, `gpt`, `di`. |
| **Model** | Modelo concreto a usar (avanzado). | `auto` | Texto libre; `auto` deja decidir al backend. |
| **Only Classification** | Si está activo, **solo clasifica**: omite extracción de datos, validación, integración y subida a GDC. Es lo habitual para un lote de clasificación masiva. | Activado | — |
| **Generar resumen** | Si está activo, el backend genera un **resumen** del documento (objetivo, datos clave, alertas y contenido) que se guarda en el histórico y se incluye en la exportación y en el detalle. Añade una llamada extra por documento (algo más de tiempo/coste). Puedes desactivarlo en lotes muy grandes donde no lo necesites. | Activado | — |

### Grupo "Reprocesado"

| Opción | Descripción | Por defecto | Rango / notas |
|---|---|---|---|
| **Force Reprocess** | Ignora el histórico local **y** fuerza el reproceso en el backend, aunque el documento ya se hubiera clasificado. | Desactivado | — |
| **Max Retries** | Reintentos automáticos ante fallos transitorios antes de marcar un documento como error definitivo. | `3` | **0–10**. |
| **Retry Strategy** | Estrategia de reintento. Es fija: *Batch Completion* (los reintentos se agrupan al cierre del lote). | *Batch Completion* | Fija (informativa). |

### Grupo "Gestion de historicos"

| Opción | Descripción | Por defecto |
|---|---|---|
| **Skip Already Processed** | Omite los documentos que ya se clasificaron con éxito en ejecuciones anteriores. Ver [Históricos](#13-omisión-de-documentos-ya-procesados-históricos). | Activado |

---

## 6. Estados de un documento

Cada documento pasa por uno de estos estados, reflejados en la columna **Status** del grid
y agregados en los seis **indicadores** superiores:

| Estado | Indicador | Significado |
|---|---|---|
| `Pending` | **Pendientes** | Escaneado y en cola, aún no enviado. |
| `InFlight` | **En ejecucion** | Enviado al backend, procesándose. |
| `Succeeded` | **Procesados OK** | Clasificado correctamente. |
| `Error` | *(transitorio)* | Fallo recuperable; se reintentará según **Max Retries**. |
| `DefinitiveError` | **Errores definitivos** | Fallo tras agotar los reintentos. Requiere revisión. |
| `SkippedHistory` | **Omitidos por historico** | No se reprocesó porque ya estaba clasificado (ver Skip Already Processed). |
| `Cancelled` | *(no cuenta como OK ni error)* | Interrumpido por el usuario antes de completarse. |

El contador **Total encontrados** es el número de PDF detectados en el escaneo.

---

## 7. Columnas del grid

| Columna | Contenido |
|---|---|
| **FileName** | Nombre del fichero PDF. |
| **Status** | Estado actual (tabla anterior). |
| **PagesIncluded** | Páginas efectivamente incluidas en el análisis (puede ser un rango, p. ej. `1-10`, si se aplicó recorte). |
| **Pages** | Nº total de páginas del documento. |
| **TDN1** | **Familia** de la tipología (nivel 1), p. ej. `COMU`, `DOCJ`, `CULC`. |
| **TDN2** | **Subtipo** de la tipología (nivel 2), p. ej. `COMU-69`, `DOCJ-03`. |
| **Confidence** | Confianza del **clasificador**, de `0` a `1` (p. ej. `0.95`). Es la confianza real del modelo, aunque el backend haya rechazado el resultado por baja confianza (ver **Resultado**). |
| **Resultado** | Estado de calidad de la clasificación. Vacío cuando es correcta (`OK`); muestra **"Baja confianza"** cuando el backend la marcó como dudosa (`BAJA_CONFIANZA_CLASIFICACION`), es decir, la confianza del clasificador no alcanzó el umbral de aceptación y conviene **revisar** ese documento. |
| **ProcessDate** | Fecha/hora de proceso (UTC, ISO 8601). |
| **TotalDurationMs** | Duración total del proceso del documento, en milisegundos. |

> **Sobre TDN1 y TDN2:** TDN1 es la familia y TDN2 el subtipo dentro de esa familia. La
> familia (TDN1) siempre es el prefijo del subtipo (`COMU-69` → familia `COMU`), por lo
> que ambas columnas son coherentes entre sí.

> **Baja confianza:** un documento con **Resultado = "Baja confianza"** recibió una
> tipología tentativa (TDN1/TDN2) pero el clasificador no estaba lo bastante seguro, así
> que el backend no la dio por buena. Revisa esos documentos: la clasificación puede ser
> incorrecta. Usa el filtro **"Solo baja confianza"** para localizarlos (ver
> [Filtros](#8-filtros-y-búsqueda)).

---

## 8. Filtros y búsqueda

Sobre el grid dispones de filtros combinables:

- **Filtro (texto):** escribe parte del nombre de fichero para mostrar solo las
  coincidencias. Se aplica con un pequeño retardo mientras escribes.
- **Estado:** desplegable para mostrar solo un estado concreto (`Pending`, `InFlight`,
  `Succeeded`, `Error`, `DefinitiveError`, `SkippedHistory`, `Cancelled`) o **Todos**.
- **Solo baja confianza:** casilla que deja a la vista únicamente los documentos marcados
  como dudosos (Resultado distinto de `OK`), para revisarlos de un vistazo. Se combina con
  los otros dos filtros.

Los filtros afectan también a lo que se **exporta**: si hay un filtro activo, la
exportación incluye únicamente las filas visibles. Ver
[Exportación](#10-exportación-excel--csv).

---

## 9. Detalle de un documento

Haz **doble clic** en una fila para abrir el diálogo **Detalle del documento**. Muestra:

- Una cabecera con **FileName**, **Status**, **TDN1**, **TDN2**, **Confidence**,
  **ProcessDate**, **DurationMs** y, si hubo error, el **mensaje de error**.
- Un cuadro **Resumen** (solo lectura) con la síntesis del documento generada por el
  backend, si estaba activada la opción **Generar resumen** al procesarlo.
- Dos pestañas:
  - **Request enviada:** el JSON de la petición que se mandó al backend (el contenido del
    PDF va omitido por tamaño).
  - **Response recibida:** el JSON de respuesta del backend, **con formato indentado** para
    lectura cómoda (resumen, markdown extraído, detalle de clasificación, tiempos, etc.).

> Los documentos procesados **después** de la última actualización de la aplicación
> muestran la respuesta ya formateada. Registros muy antiguos podrían verse compactos;
> vuelve a procesarlos si necesitas la vista formateada.

---

## 10. Exportación (Excel / CSV)

Con **Exportar Excel** o **Exportar CSV** vuelcas los resultados a un fichero. Se abre un
diálogo para elegir la ruta y el nombre.

**Columnas exportadas** (mismas en ambos formatos):

```
FileName; Status; PagesIncluded; Pages; TDN1; TDN2; Confidence; Estado; ProcessDate; TotalDurationMs; Summary
```

La columna **Summary** contiene el resumen del documento (si se generó; ver
[Generar resumen](#5-configuración-detallada)). Estará vacía en los documentos procesados
con esa opción desactivada.

Detalles:

- **Alcance:** se exporta lo que corresponde al filtro activo. Sin filtro, se exporta toda
  la ejecución actual; con filtro de texto o de estado, solo las filas que cumplen el
  criterio.
- **CSV:** separador **`;`**, codificación **UTF-8 con BOM** (Excel lo abre con acentos
  correctos). Los valores que empiezan por `= + - @` se neutralizan para que Excel no los
  interprete como fórmulas.
- **Excel:** genera un `.xlsx` con una hoja llamada **Resultados**.

---

## 11. Ficheros de estado y dónde se guardan

| Fichero | Ubicación | Contenido |
|---|---|---|
| `config.json` | **Junto al ejecutable** | Entornos, Function Keys (**ofuscadas**, nunca en texto plano), paralelismo, lotes, polling y opciones de clasificación. |
| `lite.db` | `%LocalAppData%\DocumentIA.BatchLite\` | Base SQLite con el **histórico permanente** de ejecuciones y documentos. |

Ruta típica de la base de datos:
`C:\Users\<tu-usuario>\AppData\Local\DocumentIA.BatchLite\lite.db`

- La base de datos **no se borra automáticamente**. Es lo que permite omitir documentos ya
  clasificados y reanudar ejecuciones interrumpidas.
- Si `config.json` se corrompe, la aplicación arranca con los valores por defecto sin
  fallar (tendrás que volver a introducir la Function Key).

> **Copia de seguridad / reinicio:** para empezar de cero (perder el histórico), cierra la
> aplicación y borra `lite.db`. Para copiar la configuración a otro equipo, lleva el
> `config.json`.

---

## 12. Reanudar una ejecución interrumpida

Si la aplicación se cierra de forma inesperada (corte de luz, cierre forzado…) mientras
procesaba, al volver a abrirla detecta la **ejecución incompleta** y pregunta:

> *"Hay una ejecucion incompleta sobre '<carpeta>'. Pendientes: N, En vuelo: M,
> Completados: K. ¿Quieres continuarla? (No = descartarla)"*

- **Sí:** reengancha los documentos que quedaron en vuelo y termina los pendientes, sin
  reprocesar los ya completados.
- **No:** descarta esa ejecución (los resultados ya obtenidos se conservan en el
  histórico).

> Una simple **previsualización** (arrastrar ficheros sin llegar a pulsar Ejecutar) **no**
> dispara este aviso: solo se ofrecen para reanudar las ejecuciones que llegaron a
> lanzarse.

---

## 13. Omisión de documentos ya procesados (históricos)

Con **Skip Already Processed** activo (por defecto), antes de enviar un documento la
aplicación comprueba si ya lo clasificó con éxito anteriormente. La coincidencia se
determina por la clave:

```
nombre de fichero  +  tamaño en bytes  +  fecha de última modificación
```

Si coincide, el documento se marca como `SkippedHistory` (contador **Omitidos por
historico**) y no se vuelve a enviar, reutilizando el resultado anterior.

Implicaciones prácticas:

- Puedes **relanzar la misma carpeta** cada día: solo se procesan los documentos nuevos o
  modificados.
- Si **editas** un PDF (cambia tamaño o fecha), se considera distinto y se reprocesa.
- Para **forzar** el reproceso de todo, activa **Force Reprocess** en configuración.

---

## 14. Recomendaciones operativas

- **Paralelismo prudente:** mantén **Parallel Queries = 2** en horario laboral para no
  degradar el entorno PRO. Súbelo solo en ventanas de baja actividad y con autorización.
- **Lotes grandes fuera de horario:** lanza los volúmenes altos al final de la jornada y
  deja el equipo encendido. La aplicación impide la suspensión mientras procesa.
- **Reejecución diaria:** con **Skip Already Processed** activo, relanzar la misma carpeta
  es barato: solo entra lo nuevo.
- **Revisa los errores definitivos:** filtra por `DefinitiveError` al terminar y abre el
  detalle (doble clic) para ver el mensaje de cada fallo.
- **Previsualiza antes de ejecutar:** aprovecha que al arrastrar se ve el recuento y la
  lista para confirmar que la carpeta y el criterio de subcarpetas son los correctos.

---

## 15. Resolución de problemas

| Síntoma | Causa probable | Solución |
|---|---|---|
| Todos los documentos terminan en error nada más empezar; mensajes de **401 / no autorizado**. | Function Key vacía o incorrecta para el entorno seleccionado. | Configuracion → introduce la **Function Key** correcta del entorno → Guardar. |
| Arrastro ficheros y **el grid no muestra nada**, solo un número. | *(Comportamiento antiguo, ya corregido.)* Asegúrate de usar la versión actual: al soltar, el grid se puebla con las filas `Pending`. | Actualiza a la última versión del ejecutable. |
| El grid aparece **vacío** tras seleccionar una carpeta. | La carpeta no contiene PDF, o **Incluir subcarpetas** está desactivado y los PDF están en subniveles. | Activa **Incluir subcarpetas** y vuelve a seleccionar. |
| La pestaña **Response recibida** se ve en una sola línea. | Registro procesado con una versión anterior. | Vuelve a procesar el documento; los nuevos se guardan formateados. |
| Errores intermitentes que luego se recuperan solos. | Fallos transitorios de red o del backend; se reintentan según **Max Retries**. | Normal. Si persisten, revisa conectividad y sube **Max Retries**. |
| Algún PDF concreto acaba en **DefinitiveError** con contenido inválido. | Fichero corrupto o no es un PDF legible. | Abre el detalle para ver el mensaje; revisa el fichero de origen. |
| El proceso va lento. | Paralelismo bajo o intervalo de polling alto. | Sube **Parallel Queries** (con criterio) y/o baja **Polling Interval**. |
| El equipo se suspende a mitad del lote. | La prevención de suspensión solo actúa **durante** el proceso; si pausas mucho tiempo, el sistema puede suspenderse. | Evita pausas largas en lotes desatendidos, o ajusta el plan de energía. |
| No recuerda la configuración entre sesiones. | El `config.json` no se puede escribir (carpeta de solo lectura). | Mueve el ejecutable a una carpeta con permisos de escritura. |

---

## 16. Glosario

| Término | Significado |
|---|---|
| **TDN1** | Nivel 1 de la taxonomía: **familia** de la tipología documental (p. ej. `COMU`). |
| **TDN2** | Nivel 2: **subtipo** dentro de la familia (p. ej. `COMU-69`). |
| **Tipología** | Clase documental resultante de la clasificación. |
| **Confidence** | Grado de confianza de la clasificación, de 0 a 1. |
| **Provider** | Motor de clasificación (reglas, GPT, Document Intelligence o híbridos). |
| **Only Classification** | Modo en el que solo se clasifica, sin extracción/validación/integración posteriores. |
| **Resumen** | Síntesis del documento generada por el backend (objetivo, datos clave, alertas y contenido). Se guarda en el histórico, se exporta y se muestra en el detalle. |
| **Polling** | Consulta periódica del estado de un documento que se está procesando en el backend. |
| **Batch / Lote** | Grupo de documentos que se procesan juntos. |
| **Histórico** | Registro permanente en `lite.db` de lo ya procesado, usado para omitir duplicados y reanudar. |
| **Ejecución** | Un lanzamiento concreto de proceso sobre una selección de documentos. |

---

*Documento orientado al uso de la aplicación. Para dudas de plataforma (claves de acceso,
entornos, cuotas del backend), contacta con el equipo de DocumentIA.*
