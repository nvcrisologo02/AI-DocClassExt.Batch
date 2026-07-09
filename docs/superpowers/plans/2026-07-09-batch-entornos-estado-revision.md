# Batch: selector de entornos + estado EXTRACCION_INCOMPLETA — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** La app WPF DocumentIA.Batch reconoce `EXTRACCION_INCOMPLETA` como estado de revisión (AB#99871) y sustituye el backend único por un catálogo de entornos seleccionable con desplegable, gestionado desde un diálogo y persistido en `config.json` (AB#99877).

**Architecture:** Se extrae la clasificación de estados a un helper puro testeable (`BatchStatusClassifier`). `BatchConfig` gana `Environments` + `SelectedEnvironment` con migración automática en `SettingsService.Load()`. `MainViewModel` deja de exponer `BackendUrl`/`FunctionKey` editables y deriva `EffectiveBackendUrl`/`EffectiveFunctionKey` del entorno activo; un diálogo nuevo (`EnvironmentEditorDialog`, patrón code-behind como `PromptEditorDialog`) gestiona el catálogo.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, System.Text.Json, xUnit (proyecto de tests nuevo).

**Spec:** `docs/superpowers/specs/2026-07-09-batch-entornos-y-estado-revision-design.md` (commit f6387e5).

## Global Constraints

- Repo: `c:/temp/MVP/DocumentIA.Batch`. Rama de trabajo: `feature/batch-entornos-estado-revision` (ya existe, con el spec commiteado). Base: `main`. NO tocar `README.md` (tiene cambios locales ajenos a este trabajo).
- Mensajes de commit: `tipo(scope): resumen` con cuerpo qué/por qué/impacto. **Sin** `Co-Authored-By`, sin "Generated with", sin menciones a modelos/herramientas de IA. Cada commit termina con su AB#: Task 1 → `AB#99871`; Tasks 2-4 → `AB#99877`.
- Tests: `dotnet test tests/DocumentIA.Batch.Tests/DocumentIA.Batch.Tests.csproj -v minimal` (desde la raíz del repo). Si `dotnet test` falla por DLL bloqueado por un proceso ".NET Host" huérfano: `taskkill //PID <pid> //F` y reintentar.
- Build de la app: `dotnet build src/DocumentIA.Batch/DocumentIA.Batch.csproj -v minimal`.
- URL de producción exacta (semilla/migración): `https://srbappprodocai.azurewebsites.net`. Nombre del entorno migrado/semilla exacto: `Producción`.
- Estados de revisión exactos: `REVISION`, `VALIDACION_CON_ERRORES`, `EXTRACCION_INCOMPLETA`, `BAJA_CONFIANZA_CLASIFICACION` (case-insensitive). `BAJA_CONFIANZA` (sin sufijo) desaparece: era un bug, el backend nunca emite ese valor.
- Shell: Git Bash (sintaxis Unix).

---

### Task 1: BatchStatusClassifier + proyecto de tests (AB#99871)

**Files:**
- Create: `tests/DocumentIA.Batch.Tests/DocumentIA.Batch.Tests.csproj`
- Create: `tests/DocumentIA.Batch.Tests/BatchStatusClassifierTests.cs`
- Create: `src/DocumentIA.Batch/Services/BatchStatusClassifier.cs`
- Modify: `src/DocumentIA.Batch/ViewModels/MainViewModel.cs:1517-1522` (método `IsRevisionQuality`)
- Modify: `DocumentIA.Batch.sln` (añadir proyecto de tests vía `dotnet sln add`)

**Interfaces:**
- Consumes: nada.
- Produces: `public static class BatchStatusClassifier` con `public static bool IsRevisionQuality(string? value)` en namespace `DocumentIA.Batch.Services`. El proyecto `tests/DocumentIA.Batch.Tests` (usado también por Task 2).

- [ ] **Step 1.1: Crear el proyecto de tests**

Crear `tests/DocumentIA.Batch.Tests/DocumentIA.Batch.Tests.csproj` (calcado del de Classification.Tests, cambiando la referencia):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\DocumentIA.Batch\DocumentIA.Batch.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

</Project>
```

Añadirlo a la solución:

```bash
dotnet sln DocumentIA.Batch.sln add tests/DocumentIA.Batch.Tests/DocumentIA.Batch.Tests.csproj
```

- [ ] **Step 1.2: Escribir el test que falla**

Crear `tests/DocumentIA.Batch.Tests/BatchStatusClassifierTests.cs`:

```csharp
using DocumentIA.Batch.Services;
using Xunit;

namespace DocumentIA.Batch.Tests;

public class BatchStatusClassifierTests
{
    [Theory]
    [InlineData("REVISION")]
    [InlineData("VALIDACION_CON_ERRORES")]
    [InlineData("EXTRACCION_INCOMPLETA")]
    [InlineData("BAJA_CONFIANZA_CLASIFICACION")]
    [InlineData("extraccion_incompleta")]
    [InlineData(" EXTRACCION_INCOMPLETA ")]
    public void IsRevisionQuality_EstadosDeRevision_DevuelveTrue(string estado)
    {
        Assert.True(BatchStatusClassifier.IsRevisionQuality(estado));
    }

    [Theory]
    [InlineData("OK")]
    [InlineData("ERROR")]
    [InlineData("DUPLICADO")]
    [InlineData("BAJA_CONFIANZA")] // valor antiguo erróneo: el backend nunca lo emite
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsRevisionQuality_OtrosEstados_DevuelveFalse(string? estado)
    {
        Assert.False(BatchStatusClassifier.IsRevisionQuality(estado));
    }
}
```

- [ ] **Step 1.3: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.Tests/DocumentIA.Batch.Tests.csproj -v minimal`
Expected: FAIL de compilación (`BatchStatusClassifier` no existe).

- [ ] **Step 1.4: Implementar el clasificador**

Crear `src/DocumentIA.Batch/Services/BatchStatusClassifier.cs`:

```csharp
namespace DocumentIA.Batch.Services;

/// <summary>
/// Clasifica estados devueltos por el backend (Resultado.EstadoCalidad o Resultado.Estado)
/// en categorías operativas del batch. Lógica pura, sin dependencias de UI.
/// </summary>
public static class BatchStatusClassifier
{
    private static readonly HashSet<string> RevisionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "REVISION",
        "VALIDACION_CON_ERRORES",
        "EXTRACCION_INCOMPLETA",
        "BAJA_CONFIANZA_CLASIFICACION"
    };

    public static bool IsRevisionQuality(string? value) =>
        !string.IsNullOrWhiteSpace(value) && RevisionStates.Contains(value.Trim());
}
```

- [ ] **Step 1.5: Delegar desde MainViewModel**

En `src/DocumentIA.Batch/ViewModels/MainViewModel.cs`, sustituir (líneas 1517-1522):

```csharp
    private static bool IsRevisionQuality(string value)
    {
        return string.Equals(value, "REVISION", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "VALIDACION_CON_ERRORES", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "BAJA_CONFIANZA", StringComparison.OrdinalIgnoreCase);
    }
```
por:
```csharp
    private static bool IsRevisionQuality(string value) => BatchStatusClassifier.IsRevisionQuality(value);
```

(`using DocumentIA.Batch.Services;` ya está en el fichero, línea 11.)

- [ ] **Step 1.6: Ejecutar tests y build**

Run: `dotnet test tests/DocumentIA.Batch.Tests/DocumentIA.Batch.Tests.csproj -v minimal && dotnet build src/DocumentIA.Batch/DocumentIA.Batch.csproj -v minimal`
Expected: 13 tests PASS; build OK.

- [ ] **Step 1.7: Commit**

```bash
git add tests/DocumentIA.Batch.Tests/ src/DocumentIA.Batch/Services/BatchStatusClassifier.cs src/DocumentIA.Batch/ViewModels/MainViewModel.cs DocumentIA.Batch.sln
git commit -m "fix(batch): EXTRACCION_INCOMPLETA y BAJA_CONFIANZA_CLASIFICACION como revision

Que: nuevo BatchStatusClassifier (puro, testeable) con los estados de
revision REVISION/VALIDACION_CON_ERRORES/EXTRACCION_INCOMPLETA/
BAJA_CONFIANZA_CLASIFICACION; MainViewModel delega en el. Proyecto de
tests nuevo tests/DocumentIA.Batch.Tests.
Por que: el backend introdujo EXTRACCION_INCOMPLETA (AB#99861) y esos
documentos se mostraban como Completado; ademas BAJA_CONFIANZA nunca
hacia match (el backend emite BAJA_CONFIANZA_CLASIFICACION).
Impacto: documentos con fallback vacio o baja confianza entran en el
bucket Revision y son reprocesables.

AB#99871"
```

---

### Task 2: Modelo de entornos y migración de config (AB#99877)

**Files:**
- Modify: `src/DocumentIA.Batch/Models/BatchConfig.cs`
- Modify: `src/DocumentIA.Batch/Services/SettingsService.cs`
- Create: `tests/DocumentIA.Batch.Tests/SettingsServiceNormalizeTests.cs`
- Create: `tests/DocumentIA.Batch.Tests/BatchConfigSerializationTests.cs`

**Interfaces:**
- Consumes: proyecto de tests de Task 1.
- Produces: `public class EnvironmentConfig { string Name; string BackendUrl; string FunctionKey; }` (namespace `DocumentIA.Batch.Models`); `BatchConfig.Environments` (`List<EnvironmentConfig>`) y `BatchConfig.SelectedEnvironment` (`string`); `public static BatchConfig Normalize(BatchConfig config)` en `SettingsService`, invocado siempre desde `Load()`. Task 3 consume estos tres.

- [ ] **Step 2.1: Escribir los tests que fallan**

Crear `tests/DocumentIA.Batch.Tests/SettingsServiceNormalizeTests.cs`:

```csharp
using DocumentIA.Batch.Models;
using DocumentIA.Batch.Services;
using Xunit;

namespace DocumentIA.Batch.Tests;

public class SettingsServiceNormalizeTests
{
    private const string ProdUrl = "https://srbappprodocai.azurewebsites.net";

    [Fact]
    public void Normalize_ConfigLegacyConUrlYKey_MigraAEntornoProduccion()
    {
        var config = new BatchConfig
        {
            BackendUrl = "https://mi-backend-dev.azurewebsites.net",
            FunctionKey = "clave-legacy"
        };

        var result = SettingsService.Normalize(config);

        var env = Assert.Single(result.Environments);
        Assert.Equal("Producción", env.Name);
        Assert.Equal("https://mi-backend-dev.azurewebsites.net", env.BackendUrl);
        Assert.Equal("clave-legacy", env.FunctionKey);
        Assert.Equal("Producción", result.SelectedEnvironment);
    }

    [Fact]
    public void Normalize_ConfigYaMigrado_NoSeToca()
    {
        var config = new BatchConfig
        {
            Environments = new List<EnvironmentConfig>
            {
                new() { Name = "Dev", BackendUrl = "https://dev", FunctionKey = "k1" },
                new() { Name = "Pro", BackendUrl = "https://pro", FunctionKey = "k2" }
            },
            SelectedEnvironment = "Pro"
        };

        var result = SettingsService.Normalize(config);

        Assert.Equal(2, result.Environments.Count);
        Assert.Equal("Dev", result.Environments[0].Name);
        Assert.Equal("Pro", result.Environments[1].Name);
        Assert.Equal("Pro", result.SelectedEnvironment);
    }

    [Fact]
    public void Normalize_ConfigVacio_SiembraProduccionConUrlProdYKeyVacia()
    {
        var config = new BatchConfig { BackendUrl = string.Empty, FunctionKey = string.Empty };

        var result = SettingsService.Normalize(config);

        var env = Assert.Single(result.Environments);
        Assert.Equal("Producción", env.Name);
        Assert.Equal(ProdUrl, env.BackendUrl);
        Assert.Equal(string.Empty, env.FunctionKey);
        Assert.Equal("Producción", result.SelectedEnvironment);
    }

    [Fact]
    public void Normalize_LegacyLocalhost_SeSustituyePorUrlDeProd()
    {
        var config = new BatchConfig { BackendUrl = "http://localhost:7071", FunctionKey = "k" };

        var result = SettingsService.Normalize(config);

        var env = Assert.Single(result.Environments);
        Assert.Equal(ProdUrl, env.BackendUrl);
    }

    [Fact]
    public void Normalize_SelectedEnvironmentInexistente_SeleccionaElPrimero()
    {
        var config = new BatchConfig
        {
            Environments = new List<EnvironmentConfig>
            {
                new() { Name = "Dev", BackendUrl = "https://dev", FunctionKey = "" }
            },
            SelectedEnvironment = "NoExiste"
        };

        var result = SettingsService.Normalize(config);

        Assert.Equal("Dev", result.SelectedEnvironment);
    }
}
```

Crear `tests/DocumentIA.Batch.Tests/BatchConfigSerializationTests.cs`:

```csharp
using System.Text.Json;
using DocumentIA.Batch.Models;
using Xunit;

namespace DocumentIA.Batch.Tests;

public class BatchConfigSerializationTests
{
    [Fact]
    public void BatchConfig_ConEntornos_RoundTripPreservaTodo()
    {
        var original = new BatchConfig
        {
            Environments = new List<EnvironmentConfig>
            {
                new() { Name = "Dev", BackendUrl = "https://dev.example", FunctionKey = "key-dev" },
                new() { Name = "Pre", BackendUrl = "https://pre.example", FunctionKey = "key-pre" },
                new() { Name = "Producción", BackendUrl = "https://pro.example", FunctionKey = "key-pro" }
            },
            SelectedEnvironment = "Pre"
        };

        var json = JsonSerializer.Serialize(original, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<BatchConfig>(json)!;

        Assert.Equal(3, restored.Environments.Count);
        Assert.Equal("Pre", restored.SelectedEnvironment);
        Assert.Equal("https://pre.example", restored.Environments[1].BackendUrl);
        Assert.Equal("key-pro", restored.Environments[2].FunctionKey);
    }
}
```

- [ ] **Step 2.2: Ejecutar y verificar que fallan**

Run: `dotnet test tests/DocumentIA.Batch.Tests/DocumentIA.Batch.Tests.csproj -v minimal`
Expected: FAIL de compilación (`EnvironmentConfig`, `Environments`, `Normalize` no existen).

- [ ] **Step 2.3: Ampliar BatchConfig**

En `src/DocumentIA.Batch/Models/BatchConfig.cs`, añadir dentro de `BatchConfig` (tras `PromptOverrides`):

```csharp
    /// <summary>Catálogo de entornos configurables (dev/pre/pro/…). Fuente de verdad del backend a usar.</summary>
    public List<EnvironmentConfig> Environments { get; set; } = new();

    /// <summary>Nombre del entorno activo dentro de Environments.</summary>
    public string SelectedEnvironment { get; set; } = string.Empty;
```

Y a nivel de fichero (tras la clase `PromptOverride`):

```csharp
public class EnvironmentConfig
{
    public string Name { get; set; } = string.Empty;
    public string BackendUrl { get; set; } = string.Empty;
    public string FunctionKey { get; set; } = string.Empty;
}
```

Añadir comentario a los campos legacy (sin cambiar sus valores por defecto):

```csharp
    // Legacy: BackendUrl/FunctionKey se conservan solo para migrar config.json
    // anteriores (ver SettingsService.Normalize). La fuente de verdad es Environments.
    public string BackendUrl { get; set; } = "https://srbappprodocai.azurewebsites.net";
    public string FunctionKey { get; set; } = string.Empty;
```

- [ ] **Step 2.4: Migración en SettingsService**

En `src/DocumentIA.Batch/Services/SettingsService.cs`, sustituir el método `Load()` por:

```csharp
    public BatchConfig Load()
    {
        var configPath = GetConfigPath();
        if (!File.Exists(configPath))
        {
            return Normalize(new BatchConfig());
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<BatchConfig>(json, _jsonOptions) ?? new BatchConfig();
        return Normalize(config);
    }

    /// <summary>
    /// Garantiza que el config tiene un catálogo de entornos coherente:
    /// migra configs legacy (BackendUrl/FunctionKey sueltos) a un entorno "Producción",
    /// siembra "Producción" si no hay nada, y corrige SelectedEnvironment huérfano.
    /// Idempotente: un config ya migrado no se modifica.
    /// </summary>
    public static BatchConfig Normalize(BatchConfig config)
    {
        const string defaultName = "Producción";
        const string defaultUrl = "https://srbappprodocai.azurewebsites.net";

        if (config.Environments.Count == 0)
        {
            var legacyUrl = config.BackendUrl?.Trim() ?? string.Empty;
            var useDefault = string.IsNullOrWhiteSpace(legacyUrl)
                || string.Equals(legacyUrl, "http://localhost:7071", StringComparison.OrdinalIgnoreCase);

            config.Environments.Add(new EnvironmentConfig
            {
                Name = defaultName,
                BackendUrl = useDefault ? defaultUrl : legacyUrl,
                FunctionKey = config.FunctionKey ?? string.Empty
            });
            config.SelectedEnvironment = defaultName;
        }

        var selectedExists = config.Environments.Any(e =>
            string.Equals(e.Name, config.SelectedEnvironment, StringComparison.OrdinalIgnoreCase));
        if (!selectedExists)
        {
            config.SelectedEnvironment = config.Environments[0].Name;
        }

        return config;
    }
```

(Añadir `using System.Linq;` si `ImplicitUsings` no lo cubre — sí lo cubre, no hace falta.)

- [ ] **Step 2.5: Ejecutar tests**

Run: `dotnet test tests/DocumentIA.Batch.Tests/DocumentIA.Batch.Tests.csproj -v minimal`
Expected: PASS todos (13 de Task 1 + 6 nuevos = 19).

- [ ] **Step 2.6: Commit**

```bash
git add src/DocumentIA.Batch/Models/BatchConfig.cs src/DocumentIA.Batch/Services/SettingsService.cs tests/DocumentIA.Batch.Tests/SettingsServiceNormalizeTests.cs tests/DocumentIA.Batch.Tests/BatchConfigSerializationTests.cs
git commit -m "feat(batch): catalogo de entornos en BatchConfig con migracion automatica

Que: EnvironmentConfig (Name/BackendUrl/FunctionKey), lista Environments y
SelectedEnvironment en BatchConfig; SettingsService.Normalize migra configs
legacy a un entorno Produccion (respetando la regla localhost->prod),
siembra Produccion si no hay nada y corrige selecciones huerfanas.
Por que: base del selector de entornos; preserva la configuracion actual
del usuario sin perder su clave.
Impacto: config.json gana campos nuevos; los legacy se conservan solo
para migracion. Sin cambios de UI todavia.

AB#99877"
```

---

### Task 3: MainViewModel + ComboBox de entornos (AB#99877)

**Files:**
- Modify: `src/DocumentIA.Batch/ViewModels/MainViewModel.cs` (campos ~26-27, const 47, propiedades 158-179, comando 83, LoadConfig 504-532, SaveConfig 534-583, CanProcess 617-631, RefreshTipologiasAsync 633-643, RefreshHealthAsync 676-688, Ingest ~928, DurableStatus ~1100)
- Modify: `src/DocumentIA.Batch/MainWindow.xaml:285-289` (cajas URL/key → ComboBox)

**Interfaces:**
- Consumes: `EnvironmentConfig`, `BatchConfig.Environments/SelectedEnvironment`, `SettingsService.Normalize` (Task 2).
- Produces: en `MainViewModel`: `ObservableCollection<EnvironmentConfig> Environments`, `EnvironmentConfig? SelectedEnvironment` (TwoWay), `string EffectiveBackendUrl`, `string EffectiveFunctionKey`, `void PersistConfig()`, `BatchConfig BuildConfig()`, flag `_isLoadingConfig`. Task 4 consume `Environments`, `SelectedEnvironment`, `PersistConfig()` y `_isLoadingConfig`.

Los números de línea son orientativos: localizar por contenido. No hay test automatizado nuevo en esta task (es ViewModel/XAML WPF); el gate es que compila, la suite existente pasa y el self-review confirma que no queda ningún uso de las propiedades eliminadas.

- [ ] **Step 3.1: Sustituir campos y propiedades**

**(a)** Sustituir los campos (líneas 26-27):

```csharp
    private string _backendUrl = string.Empty;
    private string _functionKey = string.Empty;
```
por:
```csharp
    private EnvironmentConfig? _selectedEnvironment;
    private bool _isLoadingConfig;
```

**(b)** Eliminar la constante (línea 47): `private const string DefaultBackendUrl = "https://srbappprodocai.azurewebsites.net";` (la semilla vive ahora en `SettingsService.Normalize`).

**(c)** Sustituir las propiedades `BackendUrl`, `EffectiveBackendUrl` y `FunctionKey` (líneas 158-179) por:

```csharp
    public ObservableCollection<EnvironmentConfig> Environments { get; } = new();

    public EnvironmentConfig? SelectedEnvironment
    {
        get => _selectedEnvironment;
        set
        {
            if (SetProperty(ref _selectedEnvironment, value))
            {
                OnPropertyChanged(nameof(EffectiveBackendUrl));
                OnPropertyChanged(nameof(EffectiveFunctionKey));
                RefreshTipologiasCommand.RaiseCanExecuteChanged();
                StartProcessingCommand.RaiseCanExecuteChanged();
                RetryFailedCommand.RaiseCanExecuteChanged();

                if (!_isLoadingConfig && value is not null)
                {
                    PersistConfig();
                    _ = RefreshTipologiasAsync();
                    _ = RefreshHealthAsync();
                }
            }
        }
    }

    public string EffectiveBackendUrl => SelectedEnvironment?.BackendUrl?.Trim() ?? string.Empty;

    public string EffectiveFunctionKey => SelectedEnvironment?.FunctionKey ?? string.Empty;
```

- [ ] **Step 3.2: Actualizar todos los usos de BackendUrl/FunctionKey**

Localizar y sustituir (verificar después con `grep -n "BackendUrl\b\|FunctionKey\b" src/DocumentIA.Batch/ViewModels/MainViewModel.cs` que solo quedan `EffectiveBackendUrl`/`EffectiveFunctionKey` y las referencias del `BuildConfig` del Step 3.3):

1. Línea 83 (constructor): `RefreshTipologiasCommand = new RelayCommand(_ => _ = RefreshTipologiasAsync(), _ => !IsProcessing && !string.IsNullOrWhiteSpace(BackendUrl));` → `... !string.IsNullOrWhiteSpace(EffectiveBackendUrl));`
2. `CanProcess()` (línea 622): `&& !string.IsNullOrWhiteSpace(BackendUrl);` → `&& !string.IsNullOrWhiteSpace(EffectiveBackendUrl);`
3. `CanRetryFailed()` (línea 629): ídem.
4. `RefreshTipologiasAsync()` (línea 635): `if (IsProcessing || string.IsNullOrWhiteSpace(BackendUrl))` → `if (IsProcessing || string.IsNullOrWhiteSpace(EffectiveBackendUrl))`; y (línea 643) `GetTipologiasAsync(BackendUrl, ...)` → `GetTipologiasAsync(EffectiveBackendUrl, ...)`.
5. `RefreshHealthAsync()`: tras el guard `_isRefreshingHealth` (línea 681), añadir:
```csharp
        if (string.IsNullOrWhiteSpace(EffectiveBackendUrl))
        {
            return;
        }
```
y en la línea 688: `GetHealthAsync(EffectiveBackendUrl, FunctionKey, cts.Token)` → `GetHealthAsync(EffectiveBackendUrl, EffectiveFunctionKey, cts.Token)`.
6. Línea ~928: `IngestAsync(EffectiveBackendUrl, FunctionKey, request, cancellationToken)` → `IngestAsync(EffectiveBackendUrl, EffectiveFunctionKey, request, cancellationToken)`.
7. Línea ~1100: `GetDurableStatusAsync(statusQueryUri, FunctionKey, cancellationToken)` → `GetDurableStatusAsync(statusQueryUri, EffectiveFunctionKey, cancellationToken)`.

- [ ] **Step 3.3: Reescribir LoadConfig y dividir SaveConfig**

**(a)** Sustituir `LoadConfig()` (líneas 504-532) por:

```csharp
    private void LoadConfig()
    {
        _isLoadingConfig = true;
        try
        {
            var config = _settingsService.Load();

            Environments.Clear();
            foreach (var env in config.Environments)
            {
                Environments.Add(env);
            }

            SelectedEnvironment = Environments.FirstOrDefault(e =>
                string.Equals(e.Name, config.SelectedEnvironment, StringComparison.OrdinalIgnoreCase))
                ?? Environments.FirstOrDefault();

            PromptingEnabled = config.PromptingEnabled;
            SobreescribirUmbrales = config.SobreescribirUmbrales;
            UmbralExtraccion = string.IsNullOrWhiteSpace(config.UmbralExtraccion) ? "0.80" : config.UmbralExtraccion;
            UmbralExtraccionCompletitud = config.UmbralExtraccionCompletitud;
            UmbralExtraccionConfianza = config.UmbralExtraccionConfianza;
            NumeroColas = config.NumeroColas;
            EjecutarConAssetResolver = config.EjecutarConAssetResolver;
            AssetResolverCamposSolicitados = config.AssetResolverCamposSolicitados;
            SubirAGdc = config.SubirAGdc;
            ForceReprocess = config.ForceReprocess;
            _promptOverrides = new Dictionary<string, PromptOverride>(config.PromptOverrides, StringComparer.OrdinalIgnoreCase);

            SelectedTipologia = AvailableTipologias.FirstOrDefault(x =>
                string.Equals(x.Code, config.SelectedTipologia, StringComparison.OrdinalIgnoreCase))
                ?? AvailableTipologias.Last();
            OnPropertyChanged(nameof(HasPromptOverride));
            RefreshTipologiasCommand.RaiseCanExecuteChanged();
            StartProcessingCommand.RaiseCanExecuteChanged();
            RetryFailedCommand.RaiseCanExecuteChanged();
            RefreshBatchKpis();
        }
        finally
        {
            _isLoadingConfig = false;
        }
    }
```

**(b)** En `SaveConfig()` (líneas 534-583), sustituir el bloque `var config = new BatchConfig { ... }; _settingsService.Save(config);` por `PersistConfig();` (las validaciones y el `MessageBox.Show("Configuración guardada...")` se quedan como están), y añadir debajo los dos métodos nuevos:

```csharp
    private BatchConfig BuildConfig() => new()
    {
        Environments = Environments.ToList(),
        SelectedEnvironment = SelectedEnvironment?.Name ?? string.Empty,
        // Legacy: se rellenan con el entorno activo por compatibilidad con exes anteriores.
        BackendUrl = EffectiveBackendUrl,
        FunctionKey = EffectiveFunctionKey,
        SelectedTipologia = SelectedTipologia?.Code ?? "nota.simple.1_4",
        PromptingEnabled = PromptingEnabled,
        SobreescribirUmbrales = SobreescribirUmbrales,
        UmbralExtraccion = UmbralExtraccion,
        UmbralExtraccionCompletitud = UmbralExtraccionCompletitud,
        UmbralExtraccionConfianza = UmbralExtraccionConfianza,
        NumeroColas = NumeroColas,
        EjecutarConAssetResolver = EjecutarConAssetResolver,
        AssetResolverCamposSolicitados = AssetResolverCamposSolicitados,
        SubirAGdc = SubirAGdc,
        ForceReprocess = ForceReprocess,
        PromptOverrides = new Dictionary<string, PromptOverride>(_promptOverrides, StringComparer.OrdinalIgnoreCase)
    };

    private void PersistConfig() => _settingsService.Save(BuildConfig());
```

- [ ] **Step 3.4: XAML — ComboBox de entornos**

En `src/DocumentIA.Batch/MainWindow.xaml`, sustituir (líneas 285-289):

```xml
                        <TextBlock Text="Backend URL" Foreground="{StaticResource BrushMuted}" />
                        <TextBox Text="{Binding BackendUrl, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,10" />

                        <TextBlock Text="Function Key" Foreground="{StaticResource BrushMuted}" />
                        <TextBox Text="{Binding FunctionKey, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,12" />
```
por:
```xml
                        <TextBlock Text="Entorno" Foreground="{StaticResource BrushMuted}" />
                        <ComboBox ItemsSource="{Binding Environments}"
                                  DisplayMemberPath="Name"
                                  SelectedItem="{Binding SelectedEnvironment, Mode=TwoWay}"
                                  Height="32"
                                  Margin="0,4,0,6" />
                        <TextBlock Text="{Binding EffectiveBackendUrl, StringFormat=Endpoint: {0}}"
                                   Foreground="#94A3B8"
                                   FontSize="10"
                                   TextWrapping="Wrap"
                                   Margin="0,0,0,12" />
```

- [ ] **Step 3.5: Compilar, tests y self-review de usos**

Run: `dotnet build src/DocumentIA.Batch/DocumentIA.Batch.csproj -v minimal && dotnet test tests/DocumentIA.Batch.Tests/DocumentIA.Batch.Tests.csproj -v minimal`
Expected: build OK (0 errores; el binding XAML a `BackendUrl`/`FunctionKey` ya no existe), 19 tests PASS.

Run: `grep -n "\bBackendUrl\b\|\bFunctionKey\b" src/DocumentIA.Batch/ViewModels/MainViewModel.cs src/DocumentIA.Batch/MainWindow.xaml`
Expected: solo apariciones dentro de `BuildConfig` (asignación a los campos legacy) y de `EnvironmentConfig`/`EffectiveBackendUrl`/`EffectiveFunctionKey`.

- [ ] **Step 3.6: Commit**

```bash
git add src/DocumentIA.Batch/ViewModels/MainViewModel.cs src/DocumentIA.Batch/MainWindow.xaml
git commit -m "feat(batch): selector de entorno en la UI y backend derivado del entorno activo

Que: MainViewModel expone Environments + SelectedEnvironment y deriva
EffectiveBackendUrl/EffectiveFunctionKey del entorno activo; al cambiar
de entorno se persiste la seleccion y se refrescan tipologias y health.
El panel principal sustituye las cajas Backend URL/Function Key por un
ComboBox de entornos con el endpoint activo visible.
Por que: permitir cambiar entre dev/pre/pro sin editar URLs a mano.
Impacto: config.json se guarda con el catalogo; los campos legacy se
rellenan con el entorno activo por compatibilidad.

AB#99877"
```

---

### Task 4: Diálogo "Gestionar entornos" (AB#99877)

**Files:**
- Create: `src/DocumentIA.Batch/Views/EnvironmentEditorDialog.xaml`
- Create: `src/DocumentIA.Batch/Views/EnvironmentEditorDialog.xaml.cs`
- Modify: `src/DocumentIA.Batch/ViewModels/MainViewModel.cs` (comando + handler)
- Modify: `src/DocumentIA.Batch/MainWindow.xaml` (botón)

**Interfaces:**
- Consumes: `EnvironmentConfig` (Task 2); `Environments`, `SelectedEnvironment`, `PersistConfig()`, `_isLoadingConfig` (Task 3).
- Produces: `EnvironmentEditorDialog(List<EnvironmentConfig> environments)` con `public List<EnvironmentConfig>? Result { get; }`; `ManageEnvironmentsCommand` en `MainViewModel`.

- [ ] **Step 4.1: Crear el XAML del diálogo**

Crear `src/DocumentIA.Batch/Views/EnvironmentEditorDialog.xaml`:

```xml
<Window x:Class="DocumentIA.Batch.Views.EnvironmentEditorDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Gestionar entornos"
        Height="420"
        Width="640"
        MinHeight="380"
        MinWidth="560"
        WindowStartupLocation="CenterOwner">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" Margin="0,0,0,12">
            <TextBlock Text="Entornos" FontSize="18" FontWeight="SemiBold" />
            <TextBlock Text="Cada entorno define el Backend URL y la Function Key que usará la aplicación."
                       Foreground="{StaticResource BrushMuted}"
                       Margin="0,4,0,0" />
        </StackPanel>

        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="200" />
                <ColumnDefinition Width="12" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <Grid Grid.Column="0">
                <Grid.RowDefinitions>
                    <RowDefinition Height="*" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>
                <ListBox x:Name="EnvironmentsListBox"
                         DisplayMemberPath="Name"
                         SelectionChanged="EnvironmentsListBox_SelectionChanged" />
                <DockPanel Grid.Row="1" Margin="0,8,0,0" LastChildFill="False">
                    <Button Content="Añadir" Click="Add_Click" DockPanel.Dock="Left" />
                    <Button Content="Eliminar" Click="Delete_Click" DockPanel.Dock="Right" Background="#7F1D1D" />
                </DockPanel>
            </Grid>

            <StackPanel Grid.Column="2" x:Name="EditorPanel" IsEnabled="False">
                <TextBlock Text="Nombre" Foreground="{StaticResource BrushMuted}" />
                <TextBox x:Name="NameTextBox" Margin="0,4,0,10" TextChanged="NameTextBox_TextChanged" />

                <TextBlock Text="Backend URL" Foreground="{StaticResource BrushMuted}" />
                <TextBox x:Name="UrlTextBox" Margin="0,4,0,10" TextChanged="UrlTextBox_TextChanged" />

                <TextBlock Text="Function Key" Foreground="{StaticResource BrushMuted}" />
                <PasswordBox x:Name="KeyPasswordBox" Margin="0,4,0,10" PasswordChanged="KeyPasswordBox_PasswordChanged" />

                <TextBlock x:Name="ValidationText"
                           Foreground="#DC2626"
                           TextWrapping="Wrap"
                           Visibility="Collapsed" />
            </StackPanel>
        </Grid>

        <DockPanel Grid.Row="2" Margin="0,12,0,0" LastChildFill="False">
            <Button Content="Guardar" Click="Save_Click" DockPanel.Dock="Right" Margin="8,0,0,0" />
            <Button Content="Cancelar" Click="Cancel_Click" DockPanel.Dock="Right" Background="#374151" />
        </DockPanel>
    </Grid>
</Window>
```

- [ ] **Step 4.2: Crear el code-behind**

Crear `src/DocumentIA.Batch/Views/EnvironmentEditorDialog.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.Views;

public partial class EnvironmentEditorDialog : Window
{
    private readonly List<EnvironmentConfig> _environments;
    private bool _updatingFields;

    public EnvironmentEditorDialog(List<EnvironmentConfig> environments)
    {
        InitializeComponent();

        _environments = environments;
        EnvironmentsListBox.ItemsSource = _environments;
        if (_environments.Count > 0)
        {
            EnvironmentsListBox.SelectedIndex = 0;
        }
    }

    /// <summary>Catálogo resultante. Solo válido cuando DialogResult == true.</summary>
    public List<EnvironmentConfig>? Result { get; private set; }

    private EnvironmentConfig? Selected => EnvironmentsListBox.SelectedItem as EnvironmentConfig;

    private void EnvironmentsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _updatingFields = true;
        try
        {
            var env = Selected;
            EditorPanel.IsEnabled = env is not null;
            NameTextBox.Text = env?.Name ?? string.Empty;
            UrlTextBox.Text = env?.BackendUrl ?? string.Empty;
            KeyPasswordBox.Password = env?.FunctionKey ?? string.Empty;
        }
        finally
        {
            _updatingFields = false;
        }
    }

    private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFields || Selected is null)
        {
            return;
        }

        Selected.Name = NameTextBox.Text;
        EnvironmentsListBox.Items.Refresh();
    }

    private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFields || Selected is null)
        {
            return;
        }

        Selected.BackendUrl = UrlTextBox.Text;
    }

    private void KeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingFields || Selected is null)
        {
            return;
        }

        Selected.FunctionKey = KeyPasswordBox.Password;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var env = new EnvironmentConfig { Name = UniqueName("Nuevo entorno") };
        _environments.Add(env);
        EnvironmentsListBox.Items.Refresh();
        EnvironmentsListBox.SelectedItem = env;
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null)
        {
            return;
        }

        var index = EnvironmentsListBox.SelectedIndex;
        _environments.Remove(Selected);
        EnvironmentsListBox.Items.Refresh();
        EnvironmentsListBox.SelectedIndex = Math.Min(index, _environments.Count - 1);
        if (_environments.Count == 0)
        {
            EnvironmentsListBox_SelectionChanged(EnvironmentsListBox, null!);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var error = Validate();
        if (error is not null)
        {
            ValidationText.Text = error;
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        Result = _environments;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private string? Validate()
    {
        foreach (var env in _environments)
        {
            if (string.IsNullOrWhiteSpace(env.Name))
            {
                return "Todos los entornos deben tener nombre.";
            }

            if (string.IsNullOrWhiteSpace(env.BackendUrl))
            {
                return $"El entorno '{env.Name}' no tiene Backend URL.";
            }
        }

        var duplicated = _environments
            .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        return duplicated is not null
            ? $"Hay más de un entorno llamado '{duplicated.Key}'. Los nombres deben ser únicos."
            : null;
    }

    private string UniqueName(string baseName)
    {
        if (!_environments.Any(e => string.Equals(e.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        var i = 2;
        while (_environments.Any(e => string.Equals(e.Name, $"{baseName} {i}", StringComparison.OrdinalIgnoreCase)))
        {
            i++;
        }

        return $"{baseName} {i}";
    }
}
```

- [ ] **Step 4.3: Comando y handler en MainViewModel**

**(a)** En el constructor, tras la línea de `EditPromptCommand` (línea ~82), añadir:

```csharp
        ManageEnvironmentsCommand = new RelayCommand(_ => ManageEnvironments(), _ => !IsProcessing);
```

**(b)** Junto a las demás propiedades de comandos (tras `EditPromptCommand`, línea ~130), añadir:

```csharp
    public RelayCommand ManageEnvironmentsCommand { get; }
```

**(c)** Tras el método `EditPrompt()` (línea ~615), añadir:

```csharp
    private void ManageEnvironments()
    {
        var workingCopy = Environments
            .Select(e => new EnvironmentConfig
            {
                Name = e.Name,
                BackendUrl = e.BackendUrl,
                FunctionKey = e.FunctionKey
            })
            .ToList();

        var dialog = new EnvironmentEditorDialog(workingCopy)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        var previousName = SelectedEnvironment?.Name;

        _isLoadingConfig = true;
        try
        {
            Environments.Clear();
            foreach (var env in dialog.Result)
            {
                Environments.Add(env);
            }

            SelectedEnvironment = Environments.FirstOrDefault(e =>
                string.Equals(e.Name, previousName, StringComparison.OrdinalIgnoreCase))
                ?? Environments.FirstOrDefault();
        }
        finally
        {
            _isLoadingConfig = false;
        }

        PersistConfig();
        _ = RefreshTipologiasAsync();
        _ = RefreshHealthAsync();
    }
```

- [ ] **Step 4.4: Botón en MainWindow.xaml**

Justo después del `TextBlock` de "Endpoint: {0}" añadido en la Task 3 (Step 3.4), añadir:

```xml
                        <Button Content="Gestionar entornos"
                                Command="{Binding ManageEnvironmentsCommand}"
                                HorizontalAlignment="Stretch"
                                Margin="0,0,0,12"
                                Background="#EFF6FF"
                                BorderBrush="#BFDBFE"
                                Foreground="#2563EB" />
```

- [ ] **Step 4.5: Compilar y tests**

Run: `dotnet build src/DocumentIA.Batch/DocumentIA.Batch.csproj -v minimal && dotnet test tests/DocumentIA.Batch.Tests/DocumentIA.Batch.Tests.csproj -v minimal`
Expected: build OK, 19 tests PASS.

- [ ] **Step 4.6: Commit**

```bash
git add src/DocumentIA.Batch/Views/EnvironmentEditorDialog.xaml src/DocumentIA.Batch/Views/EnvironmentEditorDialog.xaml.cs src/DocumentIA.Batch/ViewModels/MainViewModel.cs src/DocumentIA.Batch/MainWindow.xaml
git commit -m "feat(batch): dialogo Gestionar entornos con alta, edicion y borrado

Que: EnvironmentEditorDialog (patron PromptEditorDialog) para anadir,
editar y borrar entornos con validacion de nombre unico y URL
obligatoria; boton Gestionar entornos en el panel principal; al aceptar
se actualiza el catalogo, se reajusta la seleccion y se persiste.
Por que: gestion completa de entornos desde la propia app.
Impacto: solo UI de la app Batch.

AB#99877"
```

---

### Task 5: Verificación final, smoke manual y PR

**Files:** ninguno nuevo.

- [ ] **Step 5.1: Suite y build completos**

Run: `dotnet build src/DocumentIA.Batch/DocumentIA.Batch.csproj -v minimal && dotnet test tests/DocumentIA.Batch.Tests/DocumentIA.Batch.Tests.csproj -v minimal && dotnet test tests/DocumentIA.Batch.Classification.Tests/DocumentIA.Batch.Classification.Tests.csproj -v minimal`
Expected: build OK; 19 tests PASS del proyecto nuevo; los tests de Classification siguen en verde (no regresión).

- [ ] **Step 5.2: Smoke manual (lo ejecuta el usuario, documentar en el informe)**

Checklist para validación manual con la app arrancada (`dotnet run --project src/DocumentIA.Batch`):
1. Primer arranque con `config.json` antiguo → aparece el entorno "Producción" seleccionado con la URL/key que había.
2. "Gestionar entornos" → añadir "Dev" con URL/key de dev → Guardar → aparece en el desplegable.
3. Cambiar al entorno "Dev" → se recargan tipologías y el health apunta al backend de dev; `config.json` refleja `SelectedEnvironment: "Dev"`.
4. Borrar el entorno activo desde el diálogo → al aceptar queda seleccionado el primero restante.
5. Nombre duplicado o URL vacía en el diálogo → mensaje de validación y no cierra.

- [ ] **Step 5.3: Diff y PR**

Run: `git diff main --stat` — verificar que solo se tocaron los ficheros del plan (más el spec ya commiteado). `README.md` NO debe aparecer.

Comprobar el remoto (`git remote -v`): si el repo está en Azure DevOps, push de la rama y PR hacia `main` con los tools MCP de ADO referenciando `AB#99871` y `AB#99877`; si no hay remoto, informar al usuario y dejar la rama local lista para merge. Descripción del PR: resumen de los dos cambios (estado de revisión + selector de entornos), la migración automática de config, y el checklist de smoke manual del Step 5.2. Sin líneas de atribución.

---

## Riesgos y decisiones

- **`SelectedEnvironment` compara por instancia** (`SetProperty` con `EnvironmentConfig?`): tras `Environments.Clear()` + re-add, la instancia seleccionada cambia siempre → el setter se dispara de forma fiable; el flag `_isLoadingConfig` evita persistencias/refrescos dobles durante carga y tras el diálogo.
- **Campos legacy escritos al guardar** (`BackendUrl`/`FunctionKey` = entorno activo): permite volver a un exe anterior sin perder el backend configurado. Coste: dos líneas.
- **`PasswordBox` no soporta binding** — se maneja por code-behind (patrón ya usado: el diálogo entero es code-behind, como `PromptEditorDialog`).
- **La UI WPF no tiene tests automatizados** (no hay harness de UI en el repo): el gate de las Tasks 3-4 es compilación + suite de lógica + smoke manual del Step 5.2.
