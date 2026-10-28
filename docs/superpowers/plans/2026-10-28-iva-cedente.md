# IvaCedente — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consola .NET 8 `DocumentIA.Batch.IvaCedente` que, desde un CSV `ID_SAREB;Object ID del Documento;TDN2`, pasa por DocumentIA en PRO los documentos relevantes con un prompt que detecta IVA y su deducción en el título previo, escribe un CSV por documento reanudable y consolida un CSV por activo.

**Architecture:** Proyecto de consola en la solución Batch que referencia `DocumentIA.Batch.ClassificationLite` (y por transición `DocumentIA.Batch`) para reutilizar `DocumentIaBackendClient`, los DTOs `IngestRequest`, `IIngestBackend`/`IngestBackendAdapter`, `EnvironmentConfig`, `AdaptivePollingStrategy` y `LiteResultParser`, igual que hace `DocumentIA.Batch.Evaluation`. Los DTOs del Batch se amplían de forma aditiva con `objectIdGDC`, `forzarProcesadoSinLimitePaginas`, `prompt.modelKey` e `idActivo`. `CliArgs` y `CsvUtil` se copian de Evaluation (es un `Exe`; no se referencia un ejecutable desde otro). El CSV de salida es el estado: se anexa una fila por resultado y se reescribe ordenado al terminar; la reanudación lee ese fichero.

**Tech Stack:** .NET 8 (`net8.0-windows`, heredado de las referencias), System.Text.Json, xUnit 2.9.0, sin paquetes nuevos.

**Spec:** `docs/superpowers/specs/2026-10-28-iva-cedente-design.md` (aprobado 2026-10-28). Work item AB#101191. Rama `feature/101191-iva-cedente`, worktree `C:/temp/MVP/DocumentIA.Batch-iva-cedente`.

## Global Constraints

- Español en código visible al usuario, comentarios, docs y commits. Identificadores C# según convención del lenguaje; nombres de dominio en español (`FilaDocumento`, `Consolidador`).
- Sin `Co-Authored-By`, sin "Generated with", sin menciones a modelos ni proveedor en ningún artefacto. Todo commit lleva `(AB#101191)`.
- Comandos desde la raíz del worktree: build `dotnet build DocumentIA.Batch.sln`; tests `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests`; un test `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests --filter "FullyQualifiedName~<Nombre>"`; formato `dotnet format DocumentIA.Batch.sln --verify-no-changes`. No lanzar dos `dotnet` a la vez.
- Build sin advertencias nuevas (línea base: 0 advertencias, 59 tests de Evaluation en verde).
- Petición a DocumentIA siempre con `ClassificationOnly = true`, `ExecuteIntegrarWhenClassificationOnly = false`, `SkipDuplicateCheck = true`, `SkipGdcUpload = true`, `ForzarResumenPorDefecto = false`, `Documento.ObjectIdGDC` informado y `Content.Base64 = ""` (el trigger acepta vacío). Nunca base64 ni BlobPath.
- Valores de columnas exactamente: `Procesado` ∈ {`pendiente`, `no aplica`, `no es título`, `OK`, `error`, `timeout`, `sin contenido`, `respuesta no parseable`}; `HuboIVA` ∈ {`sí`, `no`, `no consta`, `no es título`}; `DeduccionIVA` ∈ {`sí`, `no`, `no consta`, `no aplica`}; `EsTituloPrevio` ∈ {`sí`, `no`, ``}; `Revisar` ∈ {`sí`, `no`}.
- CSV: separador `;`, UTF-8 con BOM, cabeceras fijas, escape por `CsvUtil`. Decimales con punto (cultura invariante).
- `config.json` y cualquier fichero con Function Key no se versionan; se versiona `config.sample.json` con claves vacías.
- Filtro por exclusión: `CodigosExcluidos = ["TASA-11", "SCXX-00"]` (decisión del usuario, 2026-10-28). Todo código que no esté excluido pasa a DocumentIA, también los no previstos y el TDN2 vacío. `CodigosPreclasificar = ["SCXX-00"]` por defecto (camino de dos llamadas), sin efecto mientras ese código siga excluido: la exclusión gana.

## Review Focus

1. CSV de entrada con BOM, espacios alrededor de los valores, finales de línea Windows y líneas vacías al final: se leen las filas correctas y nada más (test en Task 4).
2. Un mismo Object ID bajo varios ID_SAREB (ej. `107736649` en 79044, 97061 y 97553): una sola llamada a DocumentIA y una fila por cada activo con el mismo resultado (test en Task 8).
3. Respuesta del modelo envuelta en vallas ```` ```json ```` o con texto antes y después del JSON: se parsea igual; respuesta sin JSON válido queda en `respuesta no parseable` con el texto bruto en `Evidencia` (test en Task 5).
4. Cancelación con Ctrl+C a mitad de lote: lo ya anexado se conserva, las filas en vuelo no se escriben y un relanzamiento sin `--reintentar-errores` solo procesa las que faltan (test en Task 8).
5. `Evidencia` con `;`, comillas o saltos de línea: se escribe escapada y se lee idéntica (test en Task 7).

---

### Task 1: Esqueleto de proyectos, Program y utilidades copiadas

**Files:**
- Create: `src/DocumentIA.Batch.IvaCedente/DocumentIA.Batch.IvaCedente.csproj`
- Create: `src/DocumentIA.Batch.IvaCedente/Program.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/IvaCedenteUsageException.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/CliArgs.cs` (copia de `src/DocumentIA.Batch.Evaluation/CliArgs.cs`, namespace `DocumentIA.Batch.IvaCedente`, excepción `IvaCedenteUsageException`)
- Create: `src/DocumentIA.Batch.IvaCedente/Io/CsvUtil.cs` (copia de `src/DocumentIA.Batch.Evaluation/Io/CsvUtil.cs`, namespace `DocumentIA.Batch.IvaCedente.Io`)
- Create: `src/DocumentIA.Batch.IvaCedente/prompts/.gitkeep`
- Create: `tests/DocumentIA.Batch.IvaCedente.Tests/DocumentIA.Batch.IvaCedente.Tests.csproj`
- Create: `tests/DocumentIA.Batch.IvaCedente.Tests/CsvUtilTests.cs`, `tests/DocumentIA.Batch.IvaCedente.Tests/CliArgsTests.cs`
- Modify: `DocumentIA.Batch.sln` (añadir ambos proyectos)
- Modify: `.gitignore` (añadir `src/DocumentIA.Batch.IvaCedente/config.json`)
- Incluir en el commit: `docs/superpowers/specs/2026-10-28-iva-cedente-design.md` y este plan (ya están en el worktree).

**Interfaces:**
- Produces: `CliArgs.Parse(string[] args)`, `Require(string)`, `GetOrDefault(string,string)`, `GetIntOrDefault(string,int)` y `bool Has(string name)` (nuevo, para flags sin valor como `--reintentar-errores`). `CsvUtil.Delimiter`, `SplitLine`, `EscapeField`, `AppendRow(path, header, row)`, `ReadRows(path)`, `WriteRows(path, header, rows)`.

- [ ] **Step 1: Crear los dos proyectos y añadirlos a la solución**

Csproj de consola igual al de Evaluation (`OutputType Exe`, `net8.0-windows`, `Nullable`, `ImplicitUsings`, `AssemblyName` y `RootNamespace` `DocumentIA.Batch.IvaCedente`, `Description` "Consola headless que detecta IVA y su deducción en el título previo de cada activo mediante DocumentIA."), con `ProjectReference` a `..\DocumentIA.Batch.ClassificationLite\DocumentIA.Batch.ClassificationLite.csproj`, `InternalsVisibleTo DocumentIA.Batch.IvaCedente.Tests`, `None Include="config.sample.json" CopyToOutputDirectory="PreserveNewest"` y `None Include="prompts\**" CopyToOutputDirectory="PreserveNewest"` (los ficheros llegan en Task 3). Csproj de tests igual al de Evaluation.Tests con referencia al nuevo proyecto.

Run: `dotnet sln DocumentIA.Batch.sln add src/DocumentIA.Batch.IvaCedente/DocumentIA.Batch.IvaCedente.csproj tests/DocumentIA.Batch.IvaCedente.Tests/DocumentIA.Batch.IvaCedente.Tests.csproj`

- [ ] **Step 2: Escribir los tests de `CsvUtil` y `CliArgs`**

```csharp
[Fact]
public void AppendRow_YReadRows_ConservanCamposConSeparadorComillasYSaltos()
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
    CsvUtil.AppendRow(path, new[] { "a", "b" }, new[] { "x;y", "dice \"hola\"\nsegunda" });
    var rows = CsvUtil.ReadRows(path);
    Assert.Equal(2, rows.Count);
    Assert.Equal(new[] { "x;y", "dice \"hola\"\nsegunda" }, rows[1]);
}

[Fact]
public void Has_FlagSinValor_DevuelveTrue()
{
    var args = CliArgs.Parse(new[] { "--entrada", "in.csv", "--reintentar-errores", "--salida", "out.csv" });
    Assert.True(args.Has("reintentar-errores"));
    Assert.False(args.Has("colas"));
    Assert.Equal("out.csv", args.Require("salida"));
}
```

- [ ] **Step 3: Ejecutar y comprobar que fallan (los tipos no existen)**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests`

- [ ] **Step 4: Copiar `CliArgs` y `CsvUtil`, añadir `Has`**

`public bool Has(string name)` devuelve true si `--name` apareció, con o sin valor. Ajustar `Parse` para que un `--flag` seguido de otro `--x` o del final se guarde con valor `""`.

- [ ] **Step 5: `Program.Main` con despacho `procesar` | `consolidar` | `help`**

Mismo patrón que `DocumentIA.Batch.Evaluation.Program`: sin argumentos imprime el uso y devuelve 1; `IvaCedenteUsageException` → mensaje en stderr y 1. Los comandos se cablean en Tasks 8 y 9; hasta entonces el `switch` devuelve `PrintUnknownCommand`. Texto de uso en español con ambos comandos: `procesar --entrada <csv> --salida <csv> [--config <ruta>] [--colas <n>] [--reintentar-errores]` y `consolidar --entrada <csv por documento> --salida <csv por activo>`.

- [ ] **Step 6: Tests en verde, build completa sin advertencias y commit**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests` → 2 superados. `dotnet build DocumentIA.Batch.sln` → 0 advertencias.

```bash
git add DocumentIA.Batch.sln .gitignore src/DocumentIA.Batch.IvaCedente tests/DocumentIA.Batch.IvaCedente.Tests docs/superpowers
git commit -m "feat(iva-cedente): esqueleto de la consola IvaCedente, diseño y plan (AB#101191)"
```

---

### Task 2: Ampliar los DTOs de ingest del Batch

**Files:**
- Modify: `src/DocumentIA.Batch/Services/DocumentIaBackendClient.cs` (clases `IngestInstrucciones`, `IngestPromptConfig`, `IngestDocumento`, `IngestTrazabilidad`, al final del fichero)
- Test: `tests/DocumentIA.Batch.Tests/IngestRequestSerializationTests.cs`

**Interfaces:**
- Produces: `IngestDocumento.ObjectIdGDC` (`string?`, `[JsonPropertyName("objectIdGDC")]`), `IngestInstrucciones.ForzarProcesadoSinLimitePaginas` (`bool`, `"forzarProcesadoSinLimitePaginas"`), `IngestPromptConfig.ModelKey` (`string?`, `"modelKey"`), `IngestPromptConfig.Temperature` (`double?`, `"temperature"`), `IngestPromptConfig.MaxTokens` (`int?`, `"maxTokens"`), `IngestTrazabilidad.IdActivo` (`string?`, `"idActivo"`).

- [ ] **Step 1: Test de serialización**

```csharp
[Fact]
public void IngestRequest_ConObjectIdGdcYPrompt_SerializaLosNombresDelContrato()
{
    var request = new IngestRequest
    {
        Instrucciones = new IngestInstrucciones
        {
            ClassificationOnly = true, SkipDuplicateCheck = true, SkipGdcUpload = true,
            ForzarProcesadoSinLimitePaginas = true,
            Prompt = new IngestPromptConfig { SystemPrompt = "s", UserPromptTemplate = "u {contenido}", ModelKey = "gpt-x" }
        },
        Documento = new IngestDocumento { ObjectIdGDC = "107736649" },
        Trazabilidad = new IngestTrazabilidad { IdActivo = "79044", SubmittedBy = "iva-cedente/u" }
    };
    var json = JsonSerializer.Serialize(request);
    Assert.Contains("\"objectIdGDC\":\"107736649\"", json);
    Assert.Contains("\"forzarProcesadoSinLimitePaginas\":true", json);
    Assert.Contains("\"modelKey\":\"gpt-x\"", json);
    Assert.Contains("\"idActivo\":\"79044\"", json);
    Assert.Contains("\"base64\":\"\"", json);
}
```

- [ ] **Step 2: Ejecutar y ver que falla por compilación**

Run: `dotnet test tests/DocumentIA.Batch.Tests --filter "FullyQualifiedName~IngestRequestSerializationTests"`

- [ ] **Step 3: Añadir las seis propiedades con sus `JsonPropertyName`**

Todas opcionales y con valor por defecto nulo o `false`, para no alterar las peticiones que ya construyen Batch, Lite y Evaluation.

- [ ] **Step 4: Tests del Batch y de Lite en verde**

Run: `dotnet test tests/DocumentIA.Batch.Tests` y `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests`
Expected: todos superados.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch/Services/DocumentIaBackendClient.cs tests/DocumentIA.Batch.Tests/IngestRequestSerializationTests.cs
git commit -m "feat(batch): campos objectIdGDC, forzarProcesadoSinLimitePaginas, modelKey e idActivo en el contrato de ingest (AB#101191)"
```

---

### Task 3: Configuración y prompts

**Files:**
- Create: `src/DocumentIA.Batch.IvaCedente/Models/IvaConfig.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/Models/Prompts.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/Services/IvaConfigLoader.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/config.sample.json`
- Create: `src/DocumentIA.Batch.IvaCedente/prompts/system.md`, `src/DocumentIA.Batch.IvaCedente/prompts/user.md` (borrar `.gitkeep`)
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/IvaConfigLoaderTests.cs`

**Interfaces:**
- Consumes: `DocumentIA.Batch.ClassificationLite.Models.EnvironmentConfig` (`Name`, `BackendUrl`, `FunctionKey`).
- Produces:
  ```csharp
  public sealed class IvaConfig
  {
      public string Entorno { get; set; } = "PRO";
      public List<EnvironmentConfig> Environments { get; set; }   // por defecto DEV y PRO con las URL de Evaluation/config.sample.json y FunctionKey ""
      public int Colas { get; set; } = 3;
      public List<string> CodigosExcluidos { get; set; }          // por defecto ["TASA-11", "SCXX-00"]
      public List<string> CodigosPreclasificar { get; set; }      // por defecto ["SCXX-00"]
      public bool ForzarSinLimitePaginas { get; set; } = true;
      public string? ModelKey { get; set; }
      public string PromptSystemPath { get; set; } = "prompts/system.md";
      public string PromptUserPath { get; set; } = "prompts/user.md";
      public int PollingIntervalSeconds { get; set; } = 30;
      public int TimeoutMinutos { get; set; } = 30;
      public int MaxReintentos { get; set; } = 2;
      public int MaxPaginasClasificacion { get; set; } = 10;
  }
  public sealed record Prompts(string System, string User);
  public static class IvaConfigLoader
  {
      public const string VariableFunctionKey = "DOCIA_FUNCTION_KEY";
      public static IvaConfig Cargar(string path);                               // IvaCedenteUsageException si no existe
      public static EnvironmentConfig EntornoSeleccionado(IvaConfig config);      // la variable, si tiene valor, pisa la key del fichero; IvaCedenteUsageException si el entorno no existe o la key queda vacía
      public static Prompts CargarPrompts(IvaConfig config, string baseDirectory); // rutas relativas a baseDirectory
  }
  ```

- [ ] **Step 1: Tests**

```csharp
[Fact]
public void Cargar_SampleDelProyecto_TieneLosValoresPorDefecto()
{
    var config = IvaConfigLoader.Cargar(Path.Combine(AppContext.BaseDirectory, "config.sample.json"));
    Assert.Equal("PRO", config.Entorno);
    Assert.Equal(3, config.Colas);
    Assert.Equal(new[] { "TASA-11", "SCXX-00" }, config.CodigosExcluidos);
    Assert.Equal(new[] { "SCXX-00" }, config.CodigosPreclasificar);
    Assert.True(config.ForzarSinLimitePaginas);
}

[Fact]
public void EntornoSeleccionado_SinKeyEnFicheroPeroConVariable_UsaLaVariable()
{
    var config = new IvaConfig { Entorno = "PRO" };
    Environment.SetEnvironmentVariable(IvaConfigLoader.VariableFunctionKey, "clave-prueba");
    try { Assert.Equal("clave-prueba", IvaConfigLoader.EntornoSeleccionado(config).FunctionKey); }
    finally { Environment.SetEnvironmentVariable(IvaConfigLoader.VariableFunctionKey, null); }
}

[Fact]
public void EntornoSeleccionado_SinKeyNiVariable_Lanza()
{
    Environment.SetEnvironmentVariable(IvaConfigLoader.VariableFunctionKey, null);
    Assert.Throws<IvaCedenteUsageException>(() => IvaConfigLoader.EntornoSeleccionado(new IvaConfig()));
}

[Fact]
public void CargarPrompts_FicherosDelProyecto_UserContieneMarcadorContenido()
{
    var prompts = IvaConfigLoader.CargarPrompts(new IvaConfig(), AppContext.BaseDirectory);
    Assert.Contains("{contenido}", prompts.User);
    Assert.Contains("JSON", prompts.System);
}
```

- [ ] **Step 2: Ejecutar y ver que fallan**

- [ ] **Step 3: Implementar `IvaConfig`, `IvaConfigLoader` (System.Text.Json con `PropertyNameCaseInsensitive` y `ReadCommentHandling.Skip`) y `config.sample.json`** con los valores por defecto y las dos entradas de `Environments` (DEV `https://srbappdevdocai.azurewebsites.net`, PRO `https://srbappprodocai.azurewebsites.net`, `FunctionKey` vacías).

- [ ] **Step 4: Escribir los prompts**

`prompts/system.md` (copia exacta; se retoca tras la tanda de humo si hace falta):

```text
Eres un analista jurídico-fiscal especializado en transmisiones inmobiliarias en España. Analizas el texto de un documento que puede ser la escritura u otro título por el que la entidad cedente (la que después transmitió el activo a Sareb) adquirió el inmueble.

Tu tarea: determinar, solo a partir de lo que dice el texto, (1) si el documento es el título de adquisición del inmueble por la cedente, (2) si esa adquisición estuvo sujeta y no exenta al IVA y (3) si consta en el documento la deducción del IVA soportado por el adquirente.

Pistas que indican IVA: "sujeta a IVA", "repercute el IVA", "renuncia a la exención del IVA", "inversión del sujeto pasivo", porcentaje de IVA sobre el precio. Pistas que indican que NO hubo IVA: "Impuesto sobre Transmisiones Patrimoniales", "ITP", "exenta de IVA" sin renuncia, transmisión entre particulares, adjudicación judicial sin mención de IVA. Pistas de deducción: "derecho a la deducción total del IVA soportado", "sujeto pasivo con derecho a deducción", "inversión del sujeto pasivo" (implica deducción por el adquirente).

Reglas: no inventes. Si el texto no permite afirmarlo, responde "no consta". Si el documento no es el título de adquisición por la cedente (por ejemplo, una tasación, una nota simple, un acta de traspaso a Sareb, un préstamo), marca esTituloPrevio en false y el resto como "no consta" o "no aplica". La evidencia es una cita literal del texto, de 300 caracteres como máximo, que sustenta la respuesta.

Responde únicamente con un objeto JSON válido, sin texto antes ni después, con exactamente estas claves:
{"esTituloPrevio": true|false, "huboIVA": "sí"|"no"|"no consta", "deduccionIVA": "sí"|"no"|"no consta"|"no aplica", "cedente": string|null, "fechaAdquisicion": "AAAA-MM-DD"|null, "evidencia": string|null, "confianza": número entre 0 y 1}
```

`prompts/user.md`:

```text
Documento a analizar (texto extraído, puede estar incompleto o contener errores de OCR):

{contenido}

Devuelve solo el JSON.
```

- [ ] **Step 5: Tests en verde y commit**

```bash
git add src/DocumentIA.Batch.IvaCedente tests/DocumentIA.Batch.IvaCedente.Tests
git commit -m "feat(iva-cedente): configuración por entorno, lista de TDN2 y prompts de IVA (AB#101191)"
```

---

### Task 4: Lectura del CSV de entrada y selección de camino

**Files:**
- Create: `src/DocumentIA.Batch.IvaCedente/Models/FilaEntrada.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/Io/EntradaCsv.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/Engine/SelectorCamino.cs`
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/EntradaCsvTests.cs`, `tests/DocumentIA.Batch.IvaCedente.Tests/SelectorCaminoTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record FilaEntrada(string IdSareb, string ObjectId, string Tdn2);
  public static class EntradaCsv
  {
      public const string CabeceraIdSareb = "ID_SAREB";
      public const string CabeceraObjectId = "Object ID del Documento";
      public const string CabeceraTdn2 = "TDN2";
      public static List<FilaEntrada> Leer(string path);
      // Por nombre de cabecera (sin distinguir mayúsculas ni BOM); valores recortados; TDN2 en mayúsculas;
      // filas vacías ignoradas; pares (IdSareb, ObjectId) exactamente repetidos se dejan una vez;
      // IvaCedenteUsageException si falta una cabecera o un ObjectId está vacío.
  }
  public enum Camino { NoAplica, UnaLlamada, DosLlamadas }
  public static class SelectorCamino
  {
      public static Camino Decidir(string tdn2, IvaConfig config);
      // NoAplica si tdn2 (recortado, mayúsculas) está en CodigosExcluidos; DosLlamadas si está en CodigosPreclasificar; si no, UnaLlamada (también con tdn2 vacío o desconocido).
  }
  ```

- [ ] **Step 1: Tests**

```csharp
[Fact]
public void Leer_ConBomEspaciosYLineasVacias_DevuelveFilasLimpias()
{
    var path = Escribir("\uFEFFID_SAREB;Object ID del Documento;TDN2\r\n 79044 ; 107736649 ;escr-10\r\n79044;107736649;ESCR-10\r\n\r\n");
    var filas = EntradaCsv.Leer(path);
    Assert.Single(filas);
    Assert.Equal(new FilaEntrada("79044", "107736649", "ESCR-10"), filas[0]);
}

[Fact]
public void Leer_MismoObjectIdEnDosActivos_ConservaAmbasFilas()
{
    var filas = EntradaCsv.Leer(Escribir("ID_SAREB;Object ID del Documento;TDN2\n79044;107736649;ESCR-10\n97061;107736649;ESCR-10\n"));
    Assert.Equal(2, filas.Count);
}

[Fact]
public void Leer_SinCabeceraTdn2_Lanza()
    => Assert.Throws<IvaCedenteUsageException>(() => EntradaCsv.Leer(Escribir("ID_SAREB;Object ID del Documento\n1;2\n")));

[Theory]
[InlineData("ESCR-10", Camino.UnaLlamada)]
[InlineData("cera-14", Camino.UnaLlamada)]
[InlineData("ESCR-24", Camino.UnaLlamada)]
[InlineData("SERE-90", Camino.UnaLlamada)]
[InlineData("TASA-09", Camino.UnaLlamada)]
[InlineData("ZZZZ-99", Camino.UnaLlamada)]
[InlineData("", Camino.UnaLlamada)]
[InlineData("TASA-11", Camino.NoAplica)]
[InlineData(" tasa-11 ", Camino.NoAplica)]
[InlineData("SCXX-00", Camino.NoAplica)]
public void Decidir_SegunConfiguracionPorDefecto(string tdn2, Camino esperado)
    => Assert.Equal(esperado, SelectorCamino.Decidir(tdn2, new IvaConfig()));

[Fact]
public void Decidir_ScxxQuitadoDeExcluidos_EsDosLlamadas()
    => Assert.Equal(Camino.DosLlamadas, SelectorCamino.Decidir("SCXX-00", new IvaConfig { CodigosExcluidos = new() { "TASA-11" } }));
```

(`Escribir(string contenido)` es un helper del test que escribe el texto en un fichero temporal con UTF-8 y devuelve la ruta.)

- [ ] **Step 2: Ejecutar y ver que fallan**
- [ ] **Step 3: Implementar `FilaEntrada`, `EntradaCsv.Leer` (sobre `CsvUtil.ReadRows`) y `SelectorCamino.Decidir`**
- [ ] **Step 4: Tests en verde y commit**

```bash
git add -A src/DocumentIA.Batch.IvaCedente tests/DocumentIA.Batch.IvaCedente.Tests
git commit -m "feat(iva-cedente): lectura del CSV de entrada y selección de camino por TDN2 (AB#101191)"
```

---

### Task 5: Construcción de peticiones y lectura de la salida

**Files:**
- Create: `src/DocumentIA.Batch.IvaCedente/Models/RespuestaPrompt.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/Models/Valores.cs` (`ValoresIva`, `EstadosProcesado`)
- Create: `src/DocumentIA.Batch.IvaCedente/Engine/ConstructorPeticiones.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/Engine/ParserSalida.cs`
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/ConstructorPeticionesTests.cs`, `tests/DocumentIA.Batch.IvaCedente.Tests/ParserSalidaTests.cs`

**Interfaces:**
- Consumes: `IngestRequest` y DTOs (Task 2), `SolicitanteProvider.ObtenerSolicitante(string programa)`, `LiteResultParser.Parse(JsonElement)`.
- Produces:
  ```csharp
  public static class ValoresIva { public const string Si = "sí", No = "no", NoConsta = "no consta", NoEsTitulo = "no es título", NoAplica = "no aplica"; }
  public static class EstadosProcesado
  {
      public const string Pendiente = "pendiente", NoAplica = "no aplica", NoEsTitulo = "no es título", Ok = "OK", Error = "error", Timeout = "timeout", SinContenido = "sin contenido", NoParseable = "respuesta no parseable";
      public static bool EsFinal(string? estado); // todo salvo Pendiente, Error y Timeout
  }
  public sealed class RespuestaPrompt
  {
      public bool EsTituloPrevio { get; set; }
      public string HuboIVA { get; set; } = ValoresIva.NoConsta;
      public string DeduccionIVA { get; set; } = ValoresIva.NoConsta;
      public string? Cedente { get; set; }
      public string? FechaAdquisicion { get; set; }
      public string? Evidencia { get; set; }
      public double? Confianza { get; set; }
  }
  public static class ConstructorPeticiones
  {
      public const string Programa = "iva-cedente";
      public static IngestRequest Clasificacion(FilaEntrada fila, IvaConfig config, string correlationId);
      public static IngestRequest ConPrompt(FilaEntrada fila, IvaConfig config, Prompts prompts, string correlationId);
  }
  public sealed record SalidaDocumentIa(string? EstadoContrato, string? Tdn2, string? ResultadoPrompt);
  public static class ParserSalida
  {
      public const string EstadoSinContenido = "SIN_CONTENIDO";
      public static SalidaDocumentIa Leer(JsonElement output);        // Resultado.Estado; Tdn2 vía LiteResultParser.Parse(output).Tdn2; DatosExtraidos.ResultadoPrompt (ambas grafías)
      public static RespuestaPrompt? ParsearRespuesta(string? texto);  // null si no hay JSON válido
      public static string NormalizarValor(string? valor, bool permiteNoAplica);
  }
  ```

- [ ] **Step 1: Tests**

```csharp
[Fact]
public void Clasificacion_LlevaObjectIdYSinPromptNiContenido()
{
    var r = ConstructorPeticiones.Clasificacion(new FilaEntrada("79044", "107736649", "SCXX-00"), new IvaConfig(), "corr-1");
    Assert.Equal("107736649", r.Documento.ObjectIdGDC);
    Assert.Equal(string.Empty, r.Documento.Content.Base64);
    Assert.True(r.Instrucciones.ClassificationOnly);
    Assert.True(r.Instrucciones.SkipDuplicateCheck);
    Assert.True(r.Instrucciones.SkipGdcUpload);
    Assert.False(r.Instrucciones.ExecuteIntegrarWhenClassificationOnly);
    Assert.Equal(10, r.Instrucciones.MaxPagesForClassificationOnly);
    Assert.Null(r.Instrucciones.Prompt);
    Assert.False(r.Instrucciones.ForzarProcesadoSinLimitePaginas);
    Assert.Equal("79044", r.Trazabilidad.IdActivo);
    Assert.Equal("corr-1", r.Trazabilidad.CorrelationId);
    Assert.StartsWith("iva-cedente", r.Trazabilidad.SubmittedBy);
}

[Fact]
public void ConPrompt_LlevaPromptModeloYSinLimiteDePaginas()
{
    var config = new IvaConfig { ModelKey = "gpt-x" };
    var r = ConstructorPeticiones.ConPrompt(new FilaEntrada("79044", "107736649", "ESCR-10"), config, new Prompts("S", "U {contenido}"), "c");
    Assert.Equal("S", r.Instrucciones.Prompt!.SystemPrompt);
    Assert.Equal("U {contenido}", r.Instrucciones.Prompt.UserPromptTemplate);
    Assert.Equal("gpt-x", r.Instrucciones.Prompt.ModelKey);
    Assert.True(r.Instrucciones.ForzarProcesadoSinLimitePaginas);
    Assert.True(r.Instrucciones.ClassificationOnly);
}

[Fact]
public void Leer_OutputPascalCase_ExtraeEstadoTdn2YResultadoPrompt()
{
    var output = JsonDocument.Parse("""{"Resultado":{"Estado":"OK"},"Identificacion":{"Tdn2":"ESCR-10"},"DatosExtraidos":{"ResultadoPrompt":"{\"esTituloPrevio\":true}"}}""").RootElement;
    var s = ParserSalida.Leer(output);
    Assert.Equal("OK", s.EstadoContrato);
    Assert.Equal("ESCR-10", s.Tdn2);
    Assert.Contains("esTituloPrevio", s.ResultadoPrompt);
}

[Fact]
public void ParsearRespuesta_ConVallasYTextoAlrededor_Parsea()
{
    var texto = "Claro, aquí tienes:\n```json\n{\"esTituloPrevio\": true, \"huboIVA\": \"Sí\", \"deduccionIVA\": \"no consta\", \"cedente\": \"Banco X\", \"fechaAdquisicion\": \"2008-05-12\", \"evidencia\": \"sujeta a IVA\", \"confianza\": 0.8}\n```\nEspero que sirva.";
    var r = ParserSalida.ParsearRespuesta(texto)!;
    Assert.True(r.EsTituloPrevio);
    Assert.Equal("sí", r.HuboIVA);
    Assert.Equal("no consta", r.DeduccionIVA);
    Assert.Equal(0.8, r.Confianza);
}

[Theory]
[InlineData("no es JSON")]
[InlineData("")]
[InlineData(null)]
[InlineData("{\"esTituloPrevio\": }")]
public void ParsearRespuesta_SinJsonValido_DevuelveNull(string? texto) => Assert.Null(ParserSalida.ParsearRespuesta(texto));

[Theory]
[InlineData("SI", false, "sí")]
[InlineData("si", false, "sí")]
[InlineData("true", false, "sí")]
[InlineData("No", false, "no")]
[InlineData("desconocido", false, "no consta")]
[InlineData("no aplica", true, "no aplica")]
[InlineData("no aplica", false, "no consta")]
[InlineData(null, false, "no consta")]
public void NormalizarValor_MapeaALosValoresFijos(string? entrada, bool permiteNoAplica, string esperado)
    => Assert.Equal(esperado, ParserSalida.NormalizarValor(entrada, permiteNoAplica));
```

- [ ] **Step 2: Ejecutar y ver que fallan**
- [ ] **Step 3: Implementar**

`Clasificacion`: los valores de Global Constraints, `MaxPagesForClassificationOnly = config.MaxPaginasClasificacion`, `Documento.Name = ""`, `Trazabilidad.SubmittedBy = SolicitanteProvider.ObtenerSolicitante(Programa)`, `IdActivo = fila.IdSareb`. `ConPrompt`: lo mismo más `Prompt` (system, user, `ModelKey = config.ModelKey`) y `ForzarProcesadoSinLimitePaginas = config.ForzarSinLimitePaginas`.

`ParsearRespuesta`: recorta vallas de código; toma del primer `{` al último `}`; `JsonDocument.Parse`; lee claves sin distinguir mayúsculas; `esTituloPrevio` acepta bool o cadena ("true", "sí"); `confianza` número o cadena; aplica `NormalizarValor` a `huboIVA` (sin "no aplica") y a `deduccionIVA` (con "no aplica"). Cualquier excepción de JSON → null.

- [ ] **Step 4: Tests en verde y commit**

```bash
git add -A src/DocumentIA.Batch.IvaCedente tests/DocumentIA.Batch.IvaCedente.Tests
git commit -m "feat(iva-cedente): peticiones de clasificación y prompt, y parser de la salida de DocumentIA (AB#101191)"
```

---

### Task 6: Procesador de un documento (uno o dos pasos, reintentos, timeout)

**Files:**
- Create: `src/DocumentIA.Batch.IvaCedente/Models/FilaDocumento.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/Engine/ProcesadorDocumento.cs`
- Create: `tests/DocumentIA.Batch.IvaCedente.Tests/Fakes/FakeIngestBackend.cs` (a partir de `tests/DocumentIA.Batch.Evaluation.Tests/Fakes/FakeIngestBackend.cs`)
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/ProcesadorDocumentoTests.cs`

**Interfaces:**
- Consumes: `IIngestBackend`, `AdaptivePollingStrategy(int steadyIntervalSeconds, int timeoutMinutes)`, Tasks 3-5.
- Produces:
  ```csharp
  public sealed class FilaDocumento
  {
      public string IdSareb { get; set; } = "", ObjectId = "", Tdn2 = "";
      public string Procesado { get; set; } = EstadosProcesado.Pendiente;
      public string EsTituloPrevio = "", HuboIVA = "", DeduccionIVA = "", Cedente = "", FechaAdquisicion = "", Evidencia = "", Tipologia = "", InstanceId = "", CorrelationId = "", Error = "";
      public double? Confianza { get; set; }
      public DateTime? Fecha { get; set; }   // UTC
      public FilaDocumento ParaActivo(string idSareb, string tdn2); // copia con otro IdSareb y Tdn2 (la usa Task 8)
  }
  public sealed class ProcesadorDocumento
  {
      public ProcesadorDocumento(IIngestBackend backend, IvaConfig config, Prompts prompts, Func<TimeSpan, CancellationToken, Task>? delay = null);
      public Task<FilaDocumento> ProcesarAsync(FilaEntrada fila, CancellationToken ct);
  }
  ```
  (Las cadenas se declaran como propiedades `{ get; set; } = ""`; la forma abreviada de arriba es solo para enumerarlas.)
- Fake: `FakeIngestBackend : IIngestBackend` con `Func<IngestRequest, IngestResponse> OnIngest`, `Func<string, DurableStatusResponse> OnStatus` (recibe el `statusQueryUri`, que el fake construye con el ObjectId de la petición), `List<IngestRequest> Requests`, y `static DurableStatusResponse CompletedStatus(string estado, string? tdn2, string? resultadoPrompt)` que construye el output con `Resultado.Estado`, `Identificacion.Tdn2` y `DatosExtraidos.ResultadoPrompt`, más `static DurableStatusResponse Runtime(string runtimeStatus)`.

- [ ] **Step 1: Tests** (todos con `delay: (_, _) => Task.CompletedTask`)

```csharp
const string JsonSi = "{\"esTituloPrevio\":true,\"huboIVA\":\"sí\",\"deduccionIVA\":\"sí\",\"cedente\":\"Banco X\",\"fechaAdquisicion\":\"2008-05-12\",\"evidencia\":\"con renuncia a la exención\",\"confianza\":0.9}";
const string JsonNoTitulo = "{\"esTituloPrevio\":false,\"huboIVA\":\"no consta\",\"deduccionIVA\":\"no aplica\",\"cedente\":null,\"fechaAdquisicion\":null,\"evidencia\":null,\"confianza\":0.95}";

[Fact] public async Task NoAplica_NoLlamaAlBackend()
// TASA-11 → Procesado "no aplica", HuboIVA "", Requests vacío, Fecha informada.
[Fact] public async Task UnaLlamada_TituloConIva_RellenaLaFila()
// ESCR-10, status OK + JsonSi → Procesado OK, EsTituloPrevio "sí", HuboIVA "sí", DeduccionIVA "sí", Cedente "Banco X", FechaAdquisicion "2008-05-12", Evidencia, Confianza 0.9, Tipologia "ESCR-10", InstanceId y CorrelationId no vacíos; exactamente 1 petición, con Prompt.
[Fact] public async Task UnaLlamada_ModeloDiceQueNoEsTitulo()
// JsonNoTitulo → Procesado OK, EsTituloPrevio "no", HuboIVA "no es título", DeduccionIVA "no aplica".
[Fact] public async Task NoAplica_ScxxExcluidoPorDefecto_NoLlamaAlBackend()
// SCXX-00 con IvaConfig por defecto → Procesado "no aplica", Requests vacío.
[Fact] public async Task DosLlamadas_ScxxQueResultaEscritura_HaceClasificacionYPrompt()
// IvaConfig { CodigosExcluidos = ["TASA-11"] }; SCXX-00; 1ª petición → status OK con Tdn2 ESCR-29 y sin ResultadoPrompt; 2ª → OK + JsonSi. 2 peticiones (1ª sin Prompt, 2ª con Prompt), Tipologia "ESCR-29", Procesado OK.
[Fact] public async Task DosLlamadas_ScxxQueResultaTasacion_NoLanzaElPrompt()
// Misma config; 1ª Tdn2 TASA-11 → 1 petición, Procesado "no es título", Tipologia "TASA-11", HuboIVA "no es título", EsTituloPrevio "no".
[Fact] public async Task RespuestaNoParseable_GuardaElTextoBruto()
// ResultadoPrompt "bla" → Procesado "respuesta no parseable", Evidencia "bla".
[Fact] public async Task EstadoSinContenido_MarcaSinContenido()
// Resultado.Estado SIN_CONTENIDO → Procesado "sin contenido".
[Fact] public async Task EstadoErrorDelContrato_ReintentaYMarcaError()
// Estado ERROR siempre; MaxReintentos 2 → 3 peticiones, Procesado "error", Error no vacío.
[Fact] public async Task ExcepcionDelBackend_ReintentaYMarcaError()
// OnIngest lanza HttpRequestException("caído") → 3 peticiones, Procesado "error", Error contiene "caído".
[Fact] public async Task RuntimeFailed_MarcaError()
// Runtime("Failed") → Procesado "error".
[Fact] public async Task RunningSinFin_MarcaTimeout()
// Runtime("Running") siempre, TimeoutMinutos 0 → Procesado "timeout" sin bucle infinito.
```

- [ ] **Step 2: Ejecutar y ver que fallan**
- [ ] **Step 3: Implementar `ProcesadorDocumento`**

Camino por `SelectorCamino.Decidir(fila.Tdn2, config)`. Un paso = `EjecutarPasoAsync(Func<string, IngestRequest> construir)`: genera `CorrelationId`, lo guarda en la fila, ingest, polling como `EvaluationClassifier.PollUntilTerminalAsync` (Completed con output → éxito; Failed o Terminated → fallo; timeout por `AdaptivePollingStrategy.IsTimedOut` con `(config.PollingIntervalSeconds, config.TimeoutMinutos)`), reintento del paso completo hasta `MaxReintentos` veces más si hay excepción, fallo de runtime o `EstadoContrato == "ERROR"`. `InstanceId` de la última respuesta de ingest. El timeout no se reintenta. En el camino de dos llamadas, el segundo paso solo se lanza si `SelectorCamino.Decidir(tdn2Devuelto, config) == Camino.UnaLlamada`; en otro caso la fila queda en `no es título`.

- [ ] **Step 4: Tests en verde y commit**

```bash
git add -A src/DocumentIA.Batch.IvaCedente tests/DocumentIA.Batch.IvaCedente.Tests
git commit -m "feat(iva-cedente): procesador de documento con camino de una o dos llamadas, reintentos y timeout (AB#101191)"
```

---

### Task 7: CSV de salida por documento y reanudación

**Files:**
- Create: `src/DocumentIA.Batch.IvaCedente/Io/SalidaDocumentosCsv.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/Engine/Reanudacion.cs`
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/SalidaDocumentosCsvTests.cs`, `tests/DocumentIA.Batch.IvaCedente.Tests/ReanudacionTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public static class SalidaDocumentosCsv
  {
      public static readonly string[] Cabecera = { "ID_SAREB", "Object ID del Documento", "TDN2", "Procesado", "EsTituloPrevio", "HuboIVA", "DeduccionIVA", "Cedente", "FechaAdquisicion", "Evidencia", "Confianza", "Tipologia", "InstanceId", "CorrelationId", "Error", "Fecha" };
      public static void Anexar(string path, FilaDocumento fila);          // crea el fichero con cabecera si no existe
      public static void Escribir(string path, IEnumerable<FilaDocumento> filas);
      public static List<FilaDocumento> Leer(string path);                 // lista vacía si no existe
  }
  public static class Reanudacion
  {
      public static List<FilaEntrada> Pendientes(IReadOnlyList<FilaEntrada> entrada, IReadOnlyList<FilaDocumento> previas, bool reintentarErrores);
      public static List<FilaDocumento> Fusionar(IReadOnlyList<FilaEntrada> entrada, IReadOnlyList<FilaDocumento> previas, IReadOnlyList<FilaDocumento> nuevas);
  }
  ```
  `Pendientes`: clave `(IdSareb, ObjectId)`; una fila está resuelta si existe fila previa con `EstadosProcesado.EsFinal(Procesado)`; con `reintentarErrores == false`, `error` y `timeout` también cuentan como resueltas; `pendiente` nunca. `Fusionar`: orden de `entrada`; `nuevas` pisan a `previas`; sin resultado → fila `pendiente`. `Fecha` en ISO 8601 UTC (`"o"`); `Confianza` con `CultureInfo.InvariantCulture`.

- [ ] **Step 1: Tests**

```csharp
[Fact] public void Anexar_YLeer_IdaYVueltaConEvidenciaMultilinea()
// Evidencia "precio; \"con IVA\"\nlínea 2", Confianza 0.85, Fecha 2026-10-28T10:00:00Z → Leer devuelve lo mismo.
[Fact] public void Leer_FicheroInexistente_DevuelveVacio()
[Fact] public void Pendientes_SinReintentar_SaltaFinalesYErrores()
// previas: OK, "no aplica", "error", "pendiente"; entrada con esas 4 más una quinta sin fila previa → pendientes = la "pendiente" y la quinta.
[Fact] public void Pendientes_ConReintentar_IncluyeErroresYTimeouts()
[Fact] public void Fusionar_RespetaOrdenDeEntradaYNuevasPisanPrevias()
```

- [ ] **Step 2: Ejecutar y ver que fallan**
- [ ] **Step 3: Implementar sobre `CsvUtil`**
- [ ] **Step 4: Tests en verde y commit**

```bash
git add -A src/DocumentIA.Batch.IvaCedente tests/DocumentIA.Batch.IvaCedente.Tests
git commit -m "feat(iva-cedente): CSV de salida por documento y reanudación por fila (AB#101191)"
```

---

### Task 8: Comando `procesar` (colas, avance, una llamada por Object ID, cancelación)

**Files:**
- Create: `src/DocumentIA.Batch.IvaCedente/Commands/ProcesarCommand.cs`
- Modify: `src/DocumentIA.Batch.IvaCedente/Program.cs` (cablear `"procesar"`)
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/ProcesarCommandTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record ResumenProceso(int Total, int Omitidas, int Procesadas, int Ok, int NoAplica, int NoEsTitulo, int SinContenido, int NoParseable, int Errores, int Timeouts);
  public static class ProcesarCommand
  {
      public static Task<int> ExecuteAsync(string[] args);
      // --entrada, --salida, [--config] (por defecto config.json junto al ejecutable), [--colas] (pisa config.Colas), [--reintentar-errores]; Ctrl+C → cancelación limpia.
      public static Task<ResumenProceso> EjecutarAsync(string entradaPath, string salidaPath, IvaConfig config, Prompts prompts, IIngestBackend backend, bool reintentarErrores, TextWriter consola, CancellationToken ct);
  }
  ```
  `EjecutarAsync`: lee entrada y previas; calcula pendientes; agrupa pendientes por `ObjectId` (una llamada por documento; `FilaDocumento.ParaActivo` para el resto del grupo); `SemaphoreSlim(config.Colas)`; cada resultado se anexa bajo `lock` en cuanto llega (todas las filas del grupo); línea de avance cada 10 documentos y al final: `[n/total] OK=a noAplica=b noTitulo=c errores=d media=e s`; al terminar o al cancelar escribe `Fusionar` ordenado y devuelve el resumen. Con `ct` cancelado antes de empezar un documento, ese documento no se procesa; los que estaban en vuelo se cancelan y no se anexan.

- [ ] **Step 1: Tests** (fake con dos filas ESCR-10 y una TASA-11; el fake responde OK + JSON de título con IVA)

```csharp
[Fact] public async Task Ejecutar_EscribeUnaFilaPorEntradaYDevuelveResumen()
// 3 filas → fichero con 3 filas en el orden de entrada; Ok 2, NoAplica 1; Requests del fake = 2.
[Fact] public async Task Ejecutar_MismoObjectIdEnTresActivos_UnaLlamadaTresFilas()
// 107736649 bajo 79044, 97061 y 97553 → 1 petición, 3 filas OK con el mismo InstanceId y cada una con su IdSareb.
[Fact] public async Task Ejecutar_Relanzado_SoloProcesaPendientes()
// 1ª pasada con fake que devuelve ERROR para el ObjectId "B"; 2ª pasada sin reintentar → 0 peticiones, Omitidas 3; 3ª con reintentar → 1 petición.
[Fact] public async Task Ejecutar_Cancelado_ConservaLoAnexadoYDejaPendientes()
// Colas 1; fake que cancela el CancellationTokenSource al recibir la 2ª petición y lanza OperationCanceledException → fichero con 1 fila OK y el resto "pendiente"; relanzar procesa solo esas.
```

- [ ] **Step 2: Ejecutar y ver que fallan**
- [ ] **Step 3: Implementar `ProcesarCommand` y cablear en `Program`**

`ExecuteAsync` monta `new IngestBackendAdapter(new DocumentIaBackendClient(), IvaConfigLoader.EntornoSeleccionado(config))`, imprime entorno, URL, colas, entrada, salida y número de pendientes antes de empezar, engancha `Console.CancelKeyPress` a un `CancellationTokenSource`, y devuelve 0 si no hay errores ni timeouts y 2 si los hay.

- [ ] **Step 4: Tests en verde, build sin advertencias y commit**

```bash
git add -A src/DocumentIA.Batch.IvaCedente tests/DocumentIA.Batch.IvaCedente.Tests
git commit -m "feat(iva-cedente): comando procesar con colas, avance, una llamada por Object ID y reanudación (AB#101191)"
```

---

### Task 9: Consolidación por activo y comando `consolidar`

**Files:**
- Create: `src/DocumentIA.Batch.IvaCedente/Models/FilaActivo.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/Engine/Consolidador.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/Io/SalidaActivosCsv.cs`
- Create: `src/DocumentIA.Batch.IvaCedente/Commands/ConsolidarCommand.cs`
- Modify: `src/DocumentIA.Batch.IvaCedente/Program.cs` (cablear `"consolidar"`)
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/ConsolidadorTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class FilaActivo
  {
      public string IdSareb { get; set; } = "";
      public int Documentos { get; set; }
      public int Leidos { get; set; }
      public int ConRespuesta { get; set; }
      public string HuboIVA { get; set; } = "";
      public string DeduccionIVA { get; set; } = "";
      public string ObjectIdsSoporte { get; set; } = "";
      public string Revisar { get; set; } = "no";
  }
  public static class Consolidador
  {
      public const string SinTitulo = "sin título";
      public static List<FilaActivo> Consolidar(IEnumerable<FilaDocumento> filas); // orden de primera aparición del IdSareb
      public static string Resolver(IEnumerable<string> valores);                   // "sí" si alguno es "sí"; si no, "no" si alguno es "no"; si no, "no consta"
  }
  public static class SalidaActivosCsv
  {
      public static readonly string[] Cabecera = { "ID_SAREB", "Documentos", "Leidos", "ConRespuesta", "HuboIVA", "DeduccionIVA", "ObjectIdsSoporte", "Revisar" };
      public static void Escribir(string path, IEnumerable<FilaActivo> filas);
  }
  public static class ConsolidarCommand { public static int Execute(string[] args); } // --entrada, --salida
  ```
  Definiciones: `Documentos` = filas del activo; `Leidos` = filas con `Procesado` ∈ {OK, respuesta no parseable}; `ConRespuesta` = filas OK con `EsTituloPrevio == "sí"`. `HuboIVA` = `Resolver` sobre las `ConRespuesta`, o `sin título` si no hay ninguna (valor añadido por el plan para distinguir "la escritura no lo dice" de "no se ha leído ninguna escritura"). `DeduccionIVA` igual, sobre las `ConRespuesta` cuyo valor no sea "no aplica"; `sin título` si no hay `ConRespuesta` y `no aplica` si las hay pero todas son "no aplica". `ObjectIdsSoporte` = Object IDs cuyo `HuboIVA` coincide con el resuelto, separados por coma (vacío si `no consta` o `sin título`). `Revisar = "sí"` si entre las `ConRespuesta` hay a la vez "sí" y "no" en `HuboIVA` o en `DeduccionIVA`.

- [ ] **Step 1: Tests**

```csharp
[Theory]
[InlineData(new[] { "no consta", "sí", "no" }, "sí")]
[InlineData(new[] { "no consta", "no" }, "no")]
[InlineData(new[] { "no consta", "no consta" }, "no consta")]
[InlineData(new string[0], "no consta")]
public void Resolver_AplicaLaPrecedencia(string[] valores, string esperado) => Assert.Equal(esperado, Consolidador.Resolver(valores));

[Fact] public void Consolidar_ActivoConContradiccion_MarcaRevisarYSoporte()
// 3 filas OK título: sí (A), no (B), no consta (C) → HuboIVA "sí", Revisar "sí", ObjectIdsSoporte "A", ConRespuesta 3, Leidos 3, Documentos 3.
[Fact] public void Consolidar_ActivoSinTitulos_EsSinTitulo()
// filas "no aplica" y "no es título" (OK con EsTituloPrevio "no") → Documentos 2, Leidos 1, ConRespuesta 0, HuboIVA "sin título", DeduccionIVA "sin título", Revisar "no".
[Fact] public void Consolidar_DeduccionIgnoraNoAplica()
// títulos con deducción "no aplica" y "sí" → DeduccionIVA "sí", Revisar "no".
[Fact] public void Consolidar_ConservaElOrdenDePrimeraAparicion()
```

- [ ] **Step 2: Ejecutar y ver que fallan**
- [ ] **Step 3: Implementar y cablear `consolidar` en `Program`**
- [ ] **Step 4: Tests en verde y commit**

```bash
git add -A src/DocumentIA.Batch.IvaCedente tests/DocumentIA.Batch.IvaCedente.Tests
git commit -m "feat(iva-cedente): consolidación por activo y comando consolidar (AB#101191)"
```

---

### Task 10: Documentación, formato y verificación final

**Files:**
- Create: `src/DocumentIA.Batch.IvaCedente/README.md` (manual: propósito, configuración y Function Key por variable, los dos comandos con ejemplos, columnas de ambos CSV con sus valores, reanudación, exclusión y preclasificación por TDN2, coste en PRO y tanda de humo)
- Modify: `README.md` (raíz: entrada en "Estructura", párrafo en "Estado actual" y `AB#101191` en "Work items relacionados")

- [ ] **Step 1: Escribir ambos README**
- [ ] **Step 2: Formato y build**

Run: `dotnet format DocumentIA.Batch.sln` y después `dotnet format DocumentIA.Batch.sln --verify-no-changes` → sin cambios. `dotnet build DocumentIA.Batch.sln` → 0 advertencias.

- [ ] **Step 3: Todos los tests de la solución**

Run: `dotnet test DocumentIA.Batch.sln` → todos superados; anotar el n/n por proyecto para el cierre.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "docs(iva-cedente): manual de la consola IvaCedente y README del Batch (AB#101191)"
```

---

### Task 11 (manual, con el usuario): tanda de humo en PRO

No la ejecuta un subagente. El usuario aporta la Function Key de PRO (en `config.json` junto al ejecutable o en `DOCIA_FUNCTION_KEY`) y elige 3 a 5 Object ID del CSV: una escritura ESCR-10 de la que se conozca el IVA, una ESCR-29, una SERE-26 y una TASA-11 de control (debe salir `no aplica` sin llamar a PRO).

- [ ] **Step 1:** `dotnet run --project src/DocumentIA.Batch.IvaCedente -- procesar --entrada <csv de 5 filas> --salida <salida.csv> --colas 1`
- [ ] **Step 2:** Revisar con el usuario `Procesado`, `EsTituloPrevio`, `HuboIVA`, `DeduccionIVA` y `Evidencia` de cada fila; comprobar en el Monitor de PRO las ejecuciones cuyo `SubmittedBy` empieza por `iva-cedente` y anotar el coste por documento.
- [ ] **Step 3:** Comprobar las tres dudas del diseño: el prompt se ejecuta cuando la clasificación devuelve Desconocido; `ForzarProcesadoSinLimitePaginas` cubre el markdown del prompt en una escritura larga; no hace falta configuración de proxy.
- [ ] **Step 4:** Ajustar `prompts/*.md` si hace falta y confirmar con el usuario antes del lote completo. Commit `fix(iva-cedente): ajustes del prompt tras la tanda de humo (AB#101191)` si hay cambios.
