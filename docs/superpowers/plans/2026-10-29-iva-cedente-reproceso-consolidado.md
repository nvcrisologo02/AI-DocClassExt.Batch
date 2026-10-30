# IvaCedente: tipología forzada y consolidado con sustento — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que la consola fuerce la tipología `resumen.documental` en la llamada con prompt y que el consolidado por activo diga qué documento aporta el IVA y la deducción, con sus datos y una frase de sustento.

**Architecture:** Dos cambios aislados en `src/DocumentIA.Batch.IvaCedente`: (1) una clave de configuración y una opción de línea de comandos que viajan a `Instrucciones.ExpectedType` solo en `ConstructorPeticiones.ConPrompt`; (2) `Consolidador` elige un título de sustento por activo y rellena nuevos campos de `FilaActivo`, que `SalidaActivosCsv` escribe en nuevas columnas; una clase `FraseSustento` compone la frase. Nada cambia en el CSV por documento ni en la reanudación.

**Tech Stack:** .NET 8, xUnit, CSV propio (`CsvUtil`, `AntiFormula`).

**Spec:** `docs/superpowers/specs/2026-10-29-iva-cedente-reproceso-consolidado-design.md`

## Global Constraints

- Español en código visible (nombres de columnas, mensajes), commits y documentación; identificadores C# en PascalCase.
- Todos los commits con `AB#101191`; sin coautores ni menciones a modelos.
- Rama `feature/101191-iva-cedente-sustento` desde `main` (844b12a). Build con 0 advertencias, `dotnet format` limpio sobre el proyecto y sus tests, 118 tests previos en verde más los nuevos.
- El CSV por documento no cambia de columnas; el consolidado solo añade columnas.
- La tipología forzada va solo en la petición con prompt; la de preclasificación sigue sin `ExpectedType`.
- Valores de `TipoIVA` y `CuotaIVA` en el consolidado con punto decimal e `InvariantCulture`, como en el CSV por documento. La frase `Sustento` usa formato español (`es-ES`): `21%`, `64.050,00 €`.
- Cada ejecución contra PRO con sí explícito del usuario y `dotnet run` en segundo plano. No versionar `config.json`.

## Review Focus

1. Activo con `HuboIVA = sí` cuyos títulos con `sí` no traen `CuotaIVA` ni `TipoIVA`: `DocIVA` debe elegir por `Confianza` y, a igualdad, el primero; `Sustento` omite tipo y cuota (`IVA sí (doc A, cedente X, 2008-03-12)`). Test en Tarea 2.
2. Activo con `HuboIVA = no`: `DocIVA` es el título con `no`, `TipoIVA` y `CuotaIVA` vacíos aunque ese título los trajera. Test en Tarea 2.
3. Dos títulos con `sí` y la misma cuota (documento repetido bajo dos Object ID): no es "cuotas distintas"; `Revisar` queda en `no`. Test en Tarea 2.
4. `DeduccionIVA = sí` sostenida por un título distinto de `DocIVA`: `DocDeduccion` es ese otro documento y la frase lo nombra (`deducción sí (doc B)`). Test en Tarea 2 y 3.
5. `--tipologia` con valor vacío o solo espacios: se trata como no informada (no se envía `ExpectedType`). Test en Tarea 1.

---

### Task 1: tipología forzada

**Files:**
- Modify: `src/DocumentIA.Batch.IvaCedente/Models/IvaConfig.cs`
- Modify: `src/DocumentIA.Batch.IvaCedente/config.sample.json`
- Modify: `src/DocumentIA.Batch.IvaCedente/Engine/ConstructorPeticiones.cs`
- Modify: `src/DocumentIA.Batch.IvaCedente/Commands/ProcesarCommand.cs` (`ExecuteAsync`, bloque de opciones y cabecera de consola)
- Modify: `src/DocumentIA.Batch.IvaCedente/Program.cs` (`PrintUsage`)
- Modify: `src/DocumentIA.Batch.IvaCedente/README.md` (tabla de configuración y opciones de `procesar`)
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/ConstructorPeticionesTests.cs`, `IvaConfigLoaderTests.cs`, `ProcesarCommandTests.cs`

**Interfaces:**
- Produces: `IvaConfig.TipologiaForzada : string?` (por defecto `null`); `ProcesarCommand.AplicarOpciones(CliArgs cli, IvaConfig config) : void` (aplica `--colas` y `--tipologia` sobre la configuración; `--tipologia` vacío o en blanco deja `TipologiaForzada` en `null`).

- [ ] **Paso 1: tests que fallan**

```csharp
// ConstructorPeticionesTests
[Fact]
public void ConPrompt_ConTipologiaForzada_LaEnviaComoExpectedType()
{
    var config = new IvaConfig { TipologiaForzada = "resumen.documental" };
    var r = ConstructorPeticiones.ConPrompt(new FilaEntrada("1", "2", "ESCR-10"), config, new Prompts("S", "U"), "c");
    Assert.Equal("resumen.documental", r.Instrucciones.ExpectedType);
}

[Fact]
public void ConPrompt_SinTipologiaForzada_NoEnviaExpectedType()
{
    var r = ConstructorPeticiones.ConPrompt(new FilaEntrada("1", "2", "ESCR-10"), new IvaConfig(), new Prompts("S", "U"), "c");
    Assert.Equal(string.Empty, r.Instrucciones.ExpectedType);
}

[Fact]
public void Clasificacion_NuncaLlevaExpectedType()
{
    var config = new IvaConfig { TipologiaForzada = "resumen.documental" };
    var r = ConstructorPeticiones.Clasificacion(new FilaEntrada("1", "2", "SCXX-00"), config, "c");
    Assert.Equal(string.Empty, r.Instrucciones.ExpectedType);
}

// IvaConfigLoaderTests, dentro de Cargar_SampleDelProyecto_TieneLosValoresPorDefecto
Assert.Null(config.TipologiaForzada);

// ProcesarCommandTests
[Theory]
[InlineData(new[] { "--tipologia", "resumen.documental" }, "resumen.documental")]
[InlineData(new[] { "--tipologia", "  " }, null)]
[InlineData(new string[0], null)]
public void AplicarOpciones_Tipologia(string[] args, string? esperado)
{
    var config = new IvaConfig();
    ProcesarCommand.AplicarOpciones(CliArgs.Parse(args), config);
    Assert.Equal(esperado, config.TipologiaForzada);
}

[Fact]
public void AplicarOpciones_TipologiaDeConfigSeMantieneSinOpcion()
{
    var config = new IvaConfig { TipologiaForzada = "resumen.documental" };
    ProcesarCommand.AplicarOpciones(CliArgs.Parse(new[] { "--colas", "2" }), config);
    Assert.Equal("resumen.documental", config.TipologiaForzada);
    Assert.Equal(2, config.Colas);
}
```

- [ ] **Paso 2: comprobar que fallan**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests --filter "FullyQualifiedName~ConstructorPeticionesTests|FullyQualifiedName~ProcesarCommandTests.AplicarOpciones|FullyQualifiedName~IvaConfigLoaderTests"`
Expected: error de compilación por `TipologiaForzada` y `AplicarOpciones` inexistentes.

- [ ] **Paso 3: implementar**

`IvaConfig`: `public string? TipologiaForzada { get; set; }`. `config.sample.json`: `"TipologiaForzada": null` tras `"ModelKey"`. `ConstructorPeticiones.ConPrompt`: si `!string.IsNullOrWhiteSpace(config.TipologiaForzada)`, `request.Instrucciones.ExpectedType = config.TipologiaForzada.Trim()`. `ProcesarCommand.AplicarOpciones(CliArgs, IvaConfig)`: mueve ahí el bloque actual de `--colas` y añade `--tipologia` (`cli.Has("tipologia")` → `GetOrDefault("tipologia", "")`, en blanco → `null`); `ExecuteAsync` la llama y, si `TipologiaForzada` no está vacía, imprime `Tipología forzada: {valor}` tras la línea `Colas`. `PrintUsage`: `[--tipologia <codigo>]`.

- [ ] **Paso 4: tests en verde**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests`
Expected: todos en verde (118 + 6 nuevos).

- [ ] **Paso 5: README**

En la tabla de configuración: fila `TipologiaForzada` · `null` · "Código de tipología que se envía como `ExpectedType` en la llamada con prompt: DocumentIA omite la clasificación y ejecuta el prompt siempre. `resumen.documental` sirve para cualquier documento. Se puede fijar por ejecución con `--tipologia`." En "Comando `procesar`": añadir `--tipologia` a la línea de opciones y un ejemplo. En "Qué envía a DocumentIA": una frase sobre `ExpectedType` opcional.

- [ ] **Paso 6: formato y commit**

Run: `dotnet format src/DocumentIA.Batch.IvaCedente/DocumentIA.Batch.IvaCedente.csproj --verify-no-changes && dotnet format tests/DocumentIA.Batch.IvaCedente.Tests/DocumentIA.Batch.IvaCedente.Tests.csproj --verify-no-changes`

```bash
git add src/DocumentIA.Batch.IvaCedente tests/DocumentIA.Batch.IvaCedente.Tests
git commit -m "feat(iva-cedente): tipología forzada por configuración y por --tipologia (AB#101191)"
```

---

### Task 2: título de sustento en el consolidado

**Files:**
- Modify: `src/DocumentIA.Batch.IvaCedente/Models/FilaActivo.cs`
- Modify: `src/DocumentIA.Batch.IvaCedente/Engine/Consolidador.cs`
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/ConsolidadorTests.cs`

**Interfaces:**
- Produces en `FilaActivo`: `string DocIVA`, `double? TipoIVA`, `double? CuotaIVA`, `string Cedente`, `string FechaAdquisicion`, `string DocDeduccion`, `string MotivoRevisar` (todos `""`/`null` por defecto) y `string Sustento` (lo rellena la Tarea 3; aquí queda `""`).
- Produces en `Consolidador`: constantes `MotivoSinResolver = "filas sin resolver"`, `MotivoIvaContradictorio = "IVA contradictorio"`, `MotivoDeduccionContradictoria = "deducción contradictoria"`, `MotivoCuotasDistintas = "cuotas de IVA distintas"`; `static FilaDocumento? ElegirTitulo(IEnumerable<FilaDocumento> candidatos)` (cuota, luego tipo, luego mayor confianza, luego orden).

- [ ] **Paso 1: tests que fallan**

Añadir a `ConsolidadorTests` un helper `Titulo(...)` ampliado con parámetros opcionales `double? tipo = null, double? cuota = null, double? confianza = null, string cedente = "", string fecha = ""`, y estos tests:

```csharp
[Fact] public void Consolidar_DocIVA_PrefiereElTituloConCuota()
// A sí sin cuota conf 0.9; B sí tipo 21 cuota 64050 conf 0.5 → DocIVA "B", TipoIVA 21, CuotaIVA 64050, Cedente y Fecha de B

[Fact] public void Consolidar_DocIVA_SinCuotasPrefiereTipoLuegoConfianzaLuegoOrden()
// A sí conf 0.6; B sí tipo 21 conf 0.4 → "B". Sin tipos: A 0.6, B 0.9 → "B". Sin nada: A, B → "A"

[Fact] public void Consolidar_HuboIVANo_DocIVAEsElNoYSinTipoNiCuota()
// A no (tipo 21 cuota 100 traídos por error del modelo) → DocIVA "A", TipoIVA null, CuotaIVA null, Cedente de A

[Fact] public void Consolidar_NoConstaOSinTitulo_DocIVAVacio()
// solo "no consta" → DocIVA ""; sin títulos → DocIVA "" y DocDeduccion ""

[Fact] public void Consolidar_DocDeduccion_PrefiereElMismoQueDocIVA()
// A sí/sí cuota 100; B sí/sí cuota 100 conf mayor → DocIVA por confianza "B", DocDeduccion "B"

[Fact] public void Consolidar_DocDeduccion_OtroTituloSiDocIVANoLaSostiene()
// A sí/no consta cuota 100; B sí/sí sin cuota → DocIVA "A", DocDeduccion "B"

[Fact] public void Consolidar_CuotasDistintas_MarcaRevisarConMotivo()
// A sí cuota 100; B sí cuota 200 → Revisar "sí", MotivoRevisar "cuotas de IVA distintas"

[Fact] public void Consolidar_CuotasIguales_NoMarcaRevisar()
// A sí cuota 100; B sí cuota 100 → Revisar "no", MotivoRevisar ""

[Fact] public void Consolidar_MotivoRevisar_AcumulaEnOrden()
// 2 filas error + A sí + B no + C deducción sí + D deducción no → MotivoRevisar "filas sin resolver (2); IVA contradictorio; deducción contradictoria"
```

- [ ] **Paso 2: comprobar que fallan**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests --filter "FullyQualifiedName~ConsolidadorTests"`
Expected: error de compilación por propiedades inexistentes.

- [ ] **Paso 3: implementar**

`FilaActivo`: añadir las propiedades del bloque Interfaces. `Consolidador.Consolidar`: tras resolver `hubo` y `deduccion`, si `hubo` es `sí` o `no`, `ElegirTitulo(conRespuesta.Where(f => f.HuboIVA == hubo))` → `DocIVA`, `Cedente`, `FechaAdquisicion`; `TipoIVA` y `CuotaIVA` solo si `hubo == sí`. Si `deduccion` es `sí` o `no`: candidatos `conRespuesta.Where(f => f.DeduccionIVA == deduccion)`; si el título de `DocIVA` está entre ellos, ese; si no, `ElegirTitulo(candidatos)`. Cuotas distintas: `conRespuesta.Where(f => f.HuboIVA == Si && f.CuotaIVA is not null).Select(f => f.CuotaIVA!.Value).Distinct().Count() > 1`. `MotivoRevisar`: lista en el orden sin resolver (con el recuento entre paréntesis), IVA contradictorio, deducción contradictoria, cuotas distintas, unida por `"; "`; `Revisar = sí` si la lista no está vacía (sustituye al booleano actual). `ElegirTitulo`: `OrderByDescending(CuotaIVA is not null).ThenByDescending(TipoIVA is not null).ThenByDescending(Confianza ?? -1)` con orden estable (`OrderBy` de LINQ lo es), `FirstOrDefault()`.

- [ ] **Paso 4: tests en verde**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests`
Expected: verde; los tests previos de `Consolidador` siguen pasando (`Revisar` conserva su semántica).

- [ ] **Paso 5: formato y commit**

```bash
git add src/DocumentIA.Batch.IvaCedente tests/DocumentIA.Batch.IvaCedente.Tests
git commit -m "feat(iva-cedente): título de sustento, datos de IVA y motivo de revisión por activo (AB#101191)"
```

---

### Task 3: frase de sustento

**Files:**
- Create: `src/DocumentIA.Batch.IvaCedente/Engine/FraseSustento.cs`
- Modify: `src/DocumentIA.Batch.IvaCedente/Engine/Consolidador.cs` (asignar `Sustento = FraseSustento.Componer(fila)` al final de cada activo)
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/FraseSustentoTests.cs`

**Interfaces:**
- Produces: `public static class FraseSustento { public static string Componer(FilaActivo a); }`
- Consumes: `FilaActivo` de la Tarea 2.

- [ ] **Paso 1: tests que fallan**

```csharp
[Fact] public void Componer_IvaSiCompleto()
// HuboIVA sí, TipoIVA 21, CuotaIVA 64050, DocIVA "12345", Cedente "BANCO X", Fecha "2008-03-12", DeduccionIVA sí, DocDeduccion "12345"
// → "IVA sí 21% 64.050,00 € (doc 12345, cedente BANCO X, 2008-03-12); deducción sí (doc 12345)"

[Fact] public void Componer_TipoConDecimales_YSinCuota()
// TipoIVA 7.5, CuotaIVA null, Cedente "", Fecha "" → "IVA sí 7,5% (doc 12345); deducción no consta"

[Fact] public void Componer_IvaNo()
// HuboIVA no, DocIVA "9", Cedente "C", DeduccionIVA "no aplica" → "IVA no (doc 9, cedente C); deducción no aplica"

[Fact] public void Componer_IvaNoConsta() // → "IVA no consta; deducción no consta"
[Fact] public void Componer_SinTitulo() // HuboIVA "sin título" → "sin título previo"
[Fact] public void Componer_DeduccionNoConOtroDoc() // deducción no, DocDeduccion "B" → termina en "deducción no (doc B)"
```

- [ ] **Paso 2: comprobar que fallan**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests --filter "FullyQualifiedName~FraseSustentoTests"`
Expected: error de compilación, `FraseSustento` no existe.

- [ ] **Paso 3: implementar `FraseSustento.Componer`**

Cultura `new CultureInfo("es-ES")`. Tipo: `TipoIVA.Value.ToString("0.##", es) + "%"`. Cuota: `CuotaIVA.Value.ToString("N2", es) + " €"`. Paréntesis del documento: partes no vacías de `doc {DocIVA}`, `cedente {Cedente}`, `{FechaAdquisicion}` unidas por `", "`; se omite el paréntesis si `DocIVA` está vacío. Deducción: `deducción {DeduccionIVA}` más ` (doc {DocDeduccion})` si no está vacío. `sin título` → `"sin título previo"`.

- [ ] **Paso 4: tests en verde, formato y commit**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests`

```bash
git add src/DocumentIA.Batch.IvaCedente tests/DocumentIA.Batch.IvaCedente.Tests
git commit -m "feat(iva-cedente): frase de sustento por activo en el consolidado (AB#101191)"
```

---

### Task 4: columnas nuevas en el CSV por activo y documentación

**Files:**
- Modify: `src/DocumentIA.Batch.IvaCedente/Io/SalidaActivosCsv.cs`
- Modify: `src/DocumentIA.Batch.IvaCedente/README.md` (sección "Comando `consolidar`")
- Modify: `docs/superpowers/specs/2026-10-29-iva-cedente-reproceso-consolidado-design.md` (nota de implementación solo si algo difiere)
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/SalidaActivosCsvTests.cs`

**Interfaces:**
- Consumes: `FilaActivo` completa (Tareas 2 y 3).
- Produces: `SalidaActivosCsv.Cabecera` = `ID_SAREB, Documentos, Leidos, ConRespuesta, HuboIVA, DeduccionIVA, DocIVA, TipoIVA, CuotaIVA, Cedente, FechaAdquisicion, DocDeduccion, Sustento, MotivoRevisar, ObjectIdsSoporte, Revisar`.

- [ ] **Paso 1: test que falla**

```csharp
[Fact]
public void Escribir_ColumnasNuevasEnOrdenYNumerosConPunto()
// FilaActivo con DocIVA "=5", TipoIVA 21, CuotaIVA 64050.5, Cedente "C", FechaAdquisicion "2008-03-12", DocDeduccion "7", Sustento "IVA sí ...", MotivoRevisar "cuotas de IVA distintas", ObjectIdsSoporte "5,7", Revisar "sí"
// Assert.Equal(SalidaActivosCsv.Cabecera, filas[0]); fila[6] == "'=5"; fila[7] == "21"; fila[8] == "64050.5"; fila[13] == "cuotas de IVA distintas"; fila[14] == "5,7"; fila[15] == "sí"
```

- [ ] **Paso 2: comprobar que falla** — Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests --filter "FullyQualifiedName~SalidaActivosCsvTests"`. Expected: FAIL (cabecera de 8 columnas).

- [ ] **Paso 3: implementar** — ampliar `Cabecera` y la proyección; números con `ToString(CultureInfo.InvariantCulture)` o `""` si `null`; textos con `AntiFormula.Neutralizar`. Ajustar el test existente `Escribir_NeutralizaLasColumnasDeTexto` a los nuevos índices (`ObjectIdsSoporte` pasa a 14).

- [ ] **Paso 4: tests, build completo y formato**

Run: `dotnet build DocumentIA.Batch.sln` (0 advertencias) · `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests` · `dotnet format` de ambos proyectos con `--verify-no-changes`.

- [ ] **Paso 5: README** — en la tabla de columnas de `consolidar`, tras `DeduccionIVA`: `DocIVA`, `TipoIVA`, `CuotaIVA`, `Cedente`, `FechaAdquisicion`, `DocDeduccion`, `Sustento`, `MotivoRevisar` con el contenido de la spec; párrafo "Elección del título" con la regla cuota → tipo → confianza → orden; añadir a `Revisar` el motivo "cuotas de IVA distintas" y remitir a `MotivoRevisar`.

- [ ] **Paso 6: commit**

```bash
git add src/DocumentIA.Batch.IvaCedente tests/DocumentIA.Batch.IvaCedente.Tests docs/superpowers/specs
git commit -m "feat(iva-cedente): columnas de sustento en el CSV por activo y manual (AB#101191)"
```

---

### Task 5: reproceso en PRO (operativa; sí explícito antes de lanzar)

**Files:**
- `docs/auxiliares/temps/2026-10-29/lote-salida.csv` (se modifica), `lote-salida.antes-reproceso.csv` (copia), `lote-reproceso-entrada.csv` (nuevo), `lote-reproceso.log`, `lote-consolidado-v2.csv`.

- [ ] **Paso 1: preparar** — copiar `lote-salida.csv` a `lote-salida.antes-reproceso.csv`; generar `lote-reproceso-entrada.csv` con las 35 filas (32 `error` con causa GDC en activos que pueden cambiar y 3 `respuesta no parseable` que pueden cambiar) tomadas de `lote-errores-diagnostico.csv`; quitar de `lote-salida.csv` esas 3 filas no parseables. Mostrar recuento: 35 filas, 3 quitadas.
- [ ] **Paso 2: sí explícito del usuario** para la ejecución contra PRO (colas 2, `--tipologia resumen.documental`, `--reintentar-errores`).
- [ ] **Paso 3: ejecutar en segundo plano** desde la raíz del repo: `dotnet run --project src/DocumentIA.Batch.IvaCedente --no-build -- procesar --entrada docs/auxiliares/temps/2026-10-29/lote-reproceso-entrada.csv --salida docs/auxiliares/temps/2026-10-29/lote-salida.csv --reintentar-errores --tipologia resumen.documental --colas 2 > docs/auxiliares/temps/2026-10-29/lote-reproceso.log 2>&1`. Expected: `Filas pendientes: 35 de 35`, `Tipología forzada: resumen.documental`.
- [ ] **Paso 4: consolidar** a `lote-consolidado-v2.csv` y comparar con `lote-consolidado.csv`: activos que cambian de `HuboIVA`, `DeduccionIVA` o `Revisar`; recuento de los 35 por estado final. Informar.
