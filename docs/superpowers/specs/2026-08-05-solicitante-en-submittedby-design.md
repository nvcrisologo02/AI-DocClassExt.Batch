# Solicitante real (programa + usuario) en SubmittedBy

Fecha: 2026-08-05
Repos afectados: `DocumentIA.Batch` (principal) y `documento-ia-clasificacion-mvp` (un cambio acotado en backend)

## Problema

Los frontends batch envian `trazabilidad.submittedBy` con un literal fijo del programa
(`"DocumentIA.Batch"`, `"DocumentIA.Batch.Classification"`,
`"DocumentIA.Batch.ClassificationLite"`). El dato identifica el origen tecnico, pero no
dice **quien** lanzo el proceso, que es lo que hace falta para explotar la operacion y
para auditoria.

Ademas, el analisis previo destapo tres defectos reales:

1. **`Documentos.SubmittedBy` no lo escribe nunca nadie.** No existe ni una asignacion a
   ese campo en el backend; solo la propiedad de la entidad y el filtro de lectura. Esta
   NULL desde siempre salvo las filas que marco el script de migracion como
   `migracion-dev`.
2. **`DocumentIA.Batch.Evaluation` no fija `Trazabilidad`**, asi que hereda el default del
   DTO compartido (`DocumentIaBackendClient.cs`) y sus ejecuciones se registran como
   `DocumentIA.Batch`, indistinguibles de las del batch real.
3. **El dedup enmascara las pruebas.** Si el documento ya existe y `ForceReprocess` esta a
   `false` (el default en los tres frontends), el orquestador devuelve la ejecucion
   anterior y hace `return` antes de llegar a `Persistir`: no se inserta fila nueva. Toda
   validacion de este cambio debe hacerse con documento nuevo o con `ForceReprocess`.

## Decisiones

- **Un solo campo compuesto**, sin tocar el contrato ni el esquema: `{programa}/{usuario}`.
  El filtro del Monitor usa `Contains`, asi que sigue siendo filtrable tanto por programa
  como por usuario. `SubmittedBy` es `NVARCHAR(200)` en las dos tablas.
- **La identidad se resuelve sola**, sin pedirsela al usuario.
- **El campo es visible y editable** en los WPF, precargado con el valor resuelto.
- **`Documentos.SubmittedBy` se informa solo en el alta** del documento. Si el documento
  ya existe no se toca: el reproceso queda recogido en la ejecucion.

## Diseno

### 1. Resolucion de identidad

Componente nuevo en el proyecto base `DocumentIA.Batch`, que los otros tres referencian
por `ProjectReference`. Cascada, cacheada una vez por proceso:

1. `GetUserNameExW(NameUserPrincipal)` de `secur32.dll` -> `nombre.apellido@sareb.es`.
   Maneja `ERROR_MORE_DATA` reintentando con el tamano devuelto.
2. `WindowsIdentity.GetCurrent().Name` -> `SAREB\usuario`.
3. `Environment.UserName` -> `usuario`.
4. Nada -> se envia solo el nombre del programa.

Dos restricciones que no son opcionales:

- El paso 1 puede fallar con `ERROR_NO_SUCH_DOMAIN` ("the domain controller is not
  available"), es decir, **puede implicar ir al DC**. Se resuelve al arrancar, fuera del
  hilo de UI y con timeout corto (2 s) que degrada al paso 2, para que un equipo sin red
  corporativa no cuelgue el arranque de la aplicacion.
- El P/Invoke se aisla tras una interfaz pequena para poder testear la cascada sin
  depender del entorno de ejecucion.

Sin paquetes NuGet nuevos.

### 2. Composicion del valor

`{programa}/{usuario}`, por ejemplo
`DocumentIA.Batch.ClassificationLite/nombre.apellido@sareb.es`.

- Truncado a 200 caracteres recortando el usuario, nunca el programa.
- Si no hay usuario, queda solo el programa.

### 3. Puntos de cambio en el repo Batch

| Fichero | Cambio |
|---|---|
| `src/DocumentIA.Batch/Services/` | Componente nuevo de resolucion de solicitante |
| `src/DocumentIA.Batch/Services/DocumentIaBackendClient.cs` | Default de `SubmittedBy` pasa de `"DocumentIA.Batch"` a vacio |
| `src/DocumentIA.Batch/ViewModels/MainViewModel.cs` | Literal -> propiedad `Solicitante` |
| `src/DocumentIA.Batch.Classification/ViewModels/ClassificationMainViewModel.cs` | Idem |
| `src/DocumentIA.Batch.ClassificationLite/Engine/LiteRequestFactory.cs` | Idem |
| `src/DocumentIA.Batch.Evaluation/Commands/RunCommand.cs` | Fija `Trazabilidad` explicita con su propia etiqueta |

Vaciar el default obliga a que cada ejecutable declare su origen; ninguno hereda la
etiqueta de otro. El backend ya trata vacio como null.

### 4. Campo editable

En cada WPF, un campo "Solicitante" precargado con el valor resuelto.

- **No se persiste en la configuracion**: se recalcula en cada arranque. Si se guardase,
  una edicion puntual quedaria pegada para siempre y viajaria al siguiente usuario del
  equipo, que es justo el fallo que hace inutil el dato.
- Si se deja vacio, se restaura el valor calculado.
- `Evaluation`, al ser consola, no ofrece edicion.

Consecuencia asumida: al ser editable, el valor deja de ser un dato duro de auditoria y
pasa a ser una declaracion del usuario.

### 5. Cambio en backend

En `PersistirActivity`, al **crear** un `DocumentoEntity` nuevo se informa `SubmittedBy`
con el valor recibido en `PersistirInput`. Si el documento ya existe no se toca. La
ejecucion sigue llevando siempre el solicitante de esa ejecucion concreta, tal como ya
hace AB#99966.

## Validacion

Unitarios:

- Composicion del valor y truncado a 200 respetando el programa.
- Cascada de fallback con la interfaz de identidad mockeada (UPN disponible, solo cuenta
  Windows, solo `Environment.UserName`, nada).
- Cada frontend construye la peticion con su propia etiqueta de programa.
- `PersistirActivity` informa `Documentos.SubmittedBy` en el alta y no lo altera cuando el
  documento ya existe.

Manual: lanzar desde cada frontend **con documento nuevo o con `ForceReprocess` marcado**
y comprobar en el Monitor que la columna de solicitante muestra `programa/usuario`. Sin
esa precaucion el dedup devuelve la ejecucion anterior y parecera que no se guarda nada.

## Fuera de alcance

- `DocumentIA.Desktop` del repo MVP (hoy pide el dato a mano en un TextBox).
- El script PowerShell `scripts/extraccion-estafeta`, que corre desatendido.
- Cualquier cambio de esquema o de contrato de entrada.

## Nota de privacidad

Esto introduce UPNs de empleados en `Documentos` y `DocumentoEjecuciones`, que pasan a
contener dato personal con la retencion que tengan esas tablas. No es bloqueante en un
entorno corporativo interno, pero queda constancia.
