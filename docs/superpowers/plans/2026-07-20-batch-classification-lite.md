# Batch Classification Lite — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Nueva app WPF `DocumentIA.Batch.ClassificationLite`: clasificación masiva (hasta 100k docs) con motor sin UI, SQLite como fuente de verdad, reintentos batch-completion, polling adaptativo e histórico local.

**Architecture:** Proyecto nuevo en la solución `DocumentIA.Batch` que reutiliza `DocumentIaBackendClient` (ingest + polling Durable Functions) de la librería compartida. Motor (`Engine/`) sin dependencia de WPF que lee/escribe estado en SQLite; la UI observa eventos y pinta. Spec aprobado: `docs/superpowers/specs/2026-07-20-batch-classification-lite-design.md`.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, Microsoft.Data.Sqlite 9.0.0, Dapper 2.1.15, xunit 2.9.0.

## Global Constraints

- Repo: `c:/temp/MVP/DocumentIA.Batch`. Todos los comandos se ejecutan desde esa raíz. Rama de trabajo: `feature/batch-classification-lite` (creada en Task 1 desde `docs/batch-classification-lite-spec`).
- Versiones EXACTAS de paquetes (las ya usadas en la solución): `Microsoft.Data.Sqlite` 9.0.0, `Dapper` 2.1.15, `Microsoft.NET.Test.Sdk` 17.10.0, `xunit` 2.9.0, `xunit.runner.visualstudio` 2.8.2. No añadir NINGÚN otro paquete (el Excel se genera a mano con `ZipArchive` + `XmlWriter`).
- Namespace raíz: `DocumentIA.Batch.ClassificationLite`. Target: `net8.0-windows`, `Nullable=enable`, `ImplicitUsings=enable`.
- NO tocar `src/DocumentIA.Batch.Classification` ni `src/DocumentIA.Batch` (solo consumirlos). NO tocar `scripts/extraccion-estafeta/**` (hay cambios locales del usuario sin commitear).
- Mensajes de commit: `tipo(classification-lite): resumen en español` + cuerpo breve. PROHIBIDO añadir `Co-Authored-By`, "Generated with", o mencionar modelos/herramientas de IA. Si el orquestador proporciona una referencia `AB#nnn` para la tarea, añadirla al final del asunto; si no se proporciona, no inventarla.
- Tests: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal` debe quedar en verde al final de cada tarea. Build: `dotnet build DocumentIA.Batch.sln -v minimal` sin errores NI warnings nuevos.
- Valores por defecto de config (spec §5): Environment=PRO, ParallelQueries=2, InternalBatchSize=1000, PollingInterval=60s, ClassificationLevel=TDN1_TDN2, Provider=auto, Model=auto, OnlyClassification=true, ForceReprocess=false, MaxRetries=3, SkipAlreadyProcessed=true.
- BD SQLite: `%LocalAppData%\DocumentIA.BatchLite\lite.db` (WAL). `config.json` junto al exe.
- Textos de UI en español (como las apps hermanas).

---

### Task 1: Scaffolding — proyecto Lite, proyecto de tests, alta en solución

**Files:**
- Create: `src/DocumentIA.Batch.ClassificationLite/DocumentIA.Batch.ClassificationLite.csproj`
- Create: `src/DocumentIA.Batch.ClassificationLite/App.xaml`
- Create: `src/DocumentIA.Batch.ClassificationLite/App.xaml.cs`
- Create: `src/DocumentIA.Batch.ClassificationLite/Views/MainWindow.xaml`
- Create: `src/DocumentIA.Batch.ClassificationLite/Views/MainWindow.xaml.cs`
- Create: `tests/DocumentIA.Batch.ClassificationLite.Tests/DocumentIA.Batch.ClassificationLite.Tests.csproj`
- Create: `tests/DocumentIA.Batch.ClassificationLite.Tests/SmokeTests.cs`
- Modify: `DocumentIA.Batch.sln` (vía `dotnet sln add`)

**Interfaces:**
- Consumes: nada.
- Produces: proyectos compilables; las tareas siguientes añaden clases dentro de estos proyectos.

- [ ] **Step 1: Crear rama de trabajo**

```bash
git checkout docs/batch-classification-lite-spec
git checkout -b feature/batch-classification-lite
```

- [ ] **Step 2: Crear csproj de la app**

`src/DocumentIA.Batch.ClassificationLite/DocumentIA.Batch.ClassificationLite.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <AssemblyName>DocumentIA.Batch.ClassificationLite</AssemblyName>
    <RootNamespace>DocumentIA.Batch.ClassificationLite</RootNamespace>
    <Version>1.0.0</Version>
    <Authors>GDC SAREB</Authors>
    <Description>Batch Classification Lite: clasificacion masiva simplificada de documentos.</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.0" />
    <PackageReference Include="Dapper" Version="2.1.15" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DocumentIA.Batch\DocumentIA.Batch.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: App.xaml y MainWindow mínimos**

`src/DocumentIA.Batch.ClassificationLite/App.xaml`:

```xml
<Application x:Class="DocumentIA.Batch.ClassificationLite.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="Views/MainWindow.xaml">
    <Application.Resources />
</Application>
```

`src/DocumentIA.Batch.ClassificationLite/App.xaml.cs`:

```csharp
using System.Windows;

namespace DocumentIA.Batch.ClassificationLite;

public partial class App : Application
{
}
```

`src/DocumentIA.Batch.ClassificationLite/Views/MainWindow.xaml`:

```xml
<Window x:Class="DocumentIA.Batch.ClassificationLite.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Batch Classification Lite" Height="720" Width="1180">
    <Grid />
</Window>
```

`src/DocumentIA.Batch.ClassificationLite/Views/MainWindow.xaml.cs`:

```csharp
using System.Windows;

namespace DocumentIA.Batch.ClassificationLite.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 4: Crear csproj de tests con un smoke test**

`tests/DocumentIA.Batch.ClassificationLite.Tests/DocumentIA.Batch.ClassificationLite.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\DocumentIA.Batch.ClassificationLite\DocumentIA.Batch.ClassificationLite.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

</Project>
```

`tests/DocumentIA.Batch.ClassificationLite.Tests/SmokeTests.cs`:

```csharp
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class SmokeTests
{
    [Fact]
    public void ProjectCompiles()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 5: Alta en la solución y verificación**

```bash
dotnet sln DocumentIA.Batch.sln add src/DocumentIA.Batch.ClassificationLite/DocumentIA.Batch.ClassificationLite.csproj
dotnet sln DocumentIA.Batch.sln add tests/DocumentIA.Batch.ClassificationLite.Tests/DocumentIA.Batch.ClassificationLite.Tests.csproj
dotnet build DocumentIA.Batch.sln -v minimal
dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal
```

Esperado: build OK, 1 test PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests DocumentIA.Batch.sln
git commit -m "feat(classification-lite): scaffolding del proyecto WPF y tests"
```

---

### Task 2: Modelos de dominio + configuración (`LiteConfig` / `LiteConfigService`)

**Files:**
- Create: `src/DocumentIA.Batch.ClassificationLite/Models/LiteDocument.cs`
- Create: `src/DocumentIA.Batch.ClassificationLite/Models/LiteExecution.cs`
- Create: `src/DocumentIA.Batch.ClassificationLite/Models/LiteConfig.cs`
- Create: `src/DocumentIA.Batch.ClassificationLite/Services/LiteConfigService.cs`
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/LiteConfigServiceTests.cs`

**Interfaces:**
- Consumes: `DocumentIA.Batch.Models.EnvironmentConfig` (clase existente: `Name`, `BackendUrl`, `FunctionKey`, strings).
- Produces:
  - `LiteDocument` (propiedades: `long Id`, `string ExecutionId`, `string FileName`, `string FullPath`, `long FileSize`, `string LastModifiedUtc`, `string Status`, `int BatchNumber`, `int RetryCount`, `string? InstanceId`, `string? StatusQueryUri`, `string? Tdn1`, `string? Tdn2`, `double? Confidence`, `int? Pages`, `string? PagesIncluded`, `string? ProcessDate`, `long? DurationMs`, `string? RequestJson`, `string? ResponseJson`, `string? ErrorMessage`).
  - `LiteDocumentStatus` (const strings: `Pending`, `InFlight`, `Succeeded`, `Error`, `DefinitiveError`, `SkippedHistory`, `Cancelled`).
  - `LiteExecution` (`string ExecutionId`, `string RootPath`, `bool IncludeSubfolders`, `string Status`, `string StartedAt`, `string? CompletedAt`, `string ConfigSnapshotJson`) y `LiteExecutionStatus` (`Running`, `Paused`, `Completed`, `Cancelled`, `Aborted`).
  - `LiteConfig` con defaults del spec y `LiteConfigService` (`LiteConfig Load()`, `void Save(LiteConfig config)`, `static LiteConfig Normalize(LiteConfig config)`).

- [ ] **Step 1: Escribir tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/LiteConfigServiceTests.cs`:

```csharp
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteConfigServiceTests : IDisposable
{
    private readonly string _tempDir;

    public LiteConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string ConfigPath => Path.Combine(_tempDir, "config.json");

    [Fact]
    public void Load_SinFichero_DevuelveDefaultsConProPrecargado()
    {
        var service = new LiteConfigService(ConfigPath);

        var config = service.Load();

        Assert.Equal("PRO", config.SelectedEnvironment);
        var pro = Assert.Single(config.Environments);
        Assert.Equal("PRO", pro.Name);
        Assert.Equal("https://srbappprodocai.azurewebsites.net", pro.BackendUrl);
        Assert.Equal(2, config.ParallelQueries);
        Assert.Equal(1000, config.InternalBatchSize);
        Assert.Equal(60, config.PollingIntervalSeconds);
        Assert.Equal("TDN1_TDN2", config.ClassificationLevel);
        Assert.Equal("auto", config.Provider);
        Assert.Equal("auto", config.Model);
        Assert.True(config.OnlyClassification);
        Assert.False(config.ForceReprocess);
        Assert.Equal(3, config.MaxRetries);
        Assert.True(config.SkipAlreadyProcessed);
    }

    [Fact]
    public void SaveYLoad_HacenRoundTrip()
    {
        var service = new LiteConfigService(ConfigPath);
        var config = service.Load();
        config.ParallelQueries = 4;
        config.ForceReprocess = true;
        config.Model = "gpt-4o";

        service.Save(config);
        var reloaded = new LiteConfigService(ConfigPath).Load();

        Assert.Equal(4, reloaded.ParallelQueries);
        Assert.True(reloaded.ForceReprocess);
        Assert.Equal("gpt-4o", reloaded.Model);
    }

    [Fact]
    public void Normalize_AplicaLimites()
    {
        var config = new LiteConfig
        {
            ParallelQueries = 99,
            InternalBatchSize = 5,
            PollingIntervalSeconds = 1,
            MaxRetries = -1
        };

        var normalized = LiteConfigService.Normalize(config);

        Assert.Equal(10, normalized.ParallelQueries);
        Assert.Equal(50, normalized.InternalBatchSize);
        Assert.Equal(10, normalized.PollingIntervalSeconds);
        Assert.Equal(0, normalized.MaxRetries);
    }

    [Fact]
    public void Normalize_SinEntornos_RestauraPro()
    {
        var config = new LiteConfig();
        config.Environments.Clear();

        var normalized = LiteConfigService.Normalize(config);

        Assert.Contains(normalized.Environments, e => e.Name == "PRO");
        Assert.Equal("PRO", normalized.SelectedEnvironment);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación (`LiteConfig`/`LiteConfigService` no existen).

- [ ] **Step 3: Implementar modelos y servicio**

`src/DocumentIA.Batch.ClassificationLite/Models/LiteDocument.cs`:

```csharp
namespace DocumentIA.Batch.ClassificationLite.Models;

public class LiteDocument
{
    public long Id { get; set; }
    public string ExecutionId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    /// <summary>Fecha de última modificación del fichero, ISO 8601 UTC ("O"). Parte de la clave de dedup.</summary>
    public string LastModifiedUtc { get; set; } = string.Empty;
    public string Status { get; set; } = LiteDocumentStatus.Pending;
    public int BatchNumber { get; set; }
    public int RetryCount { get; set; }
    public string? InstanceId { get; set; }
    public string? StatusQueryUri { get; set; }
    public string? Tdn1 { get; set; }
    public string? Tdn2 { get; set; }
    public double? Confidence { get; set; }
    public int? Pages { get; set; }
    public string? PagesIncluded { get; set; }
    public string? ProcessDate { get; set; }
    public long? DurationMs { get; set; }
    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class LiteDocumentStatus
{
    public const string Pending = "Pending";
    public const string InFlight = "InFlight";
    public const string Succeeded = "Succeeded";
    public const string Error = "Error";
    public const string DefinitiveError = "DefinitiveError";
    public const string SkippedHistory = "SkippedHistory";
    public const string Cancelled = "Cancelled";
}
```

`src/DocumentIA.Batch.ClassificationLite/Models/LiteExecution.cs`:

```csharp
namespace DocumentIA.Batch.ClassificationLite.Models;

public class LiteExecution
{
    public string ExecutionId { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public bool IncludeSubfolders { get; set; }
    public string Status { get; set; } = LiteExecutionStatus.Running;
    public string StartedAt { get; set; } = string.Empty;
    public string? CompletedAt { get; set; }
    public string ConfigSnapshotJson { get; set; } = string.Empty;
}

public static class LiteExecutionStatus
{
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string Aborted = "Aborted";
}
```

`src/DocumentIA.Batch.ClassificationLite/Models/LiteConfig.cs`:

```csharp
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.ClassificationLite.Models;

public class LiteConfig
{
    public string SelectedEnvironment { get; set; } = "PRO";

    public List<EnvironmentConfig> Environments { get; set; } = new()
    {
        new EnvironmentConfig
        {
            Name = "PRO",
            BackendUrl = "https://srbappprodocai.azurewebsites.net",
            FunctionKey = string.Empty
        }
    };

    public int ParallelQueries { get; set; } = 2;
    public int InternalBatchSize { get; set; } = 1000;
    public int PollingIntervalSeconds { get; set; } = 60;
    public string ClassificationLevel { get; set; } = "TDN1_TDN2";
    public string Provider { get; set; } = "auto";
    public string Model { get; set; } = "auto";
    public bool OnlyClassification { get; set; } = true;
    public bool ForceReprocess { get; set; } = false;
    public int MaxRetries { get; set; } = 3;
    public bool SkipAlreadyProcessed { get; set; } = true;
}
```

`src/DocumentIA.Batch.ClassificationLite/Services/LiteConfigService.cs`:

```csharp
using System.IO;
using System.Text.Json;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.ClassificationLite.Services;

public class LiteConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _configPath;

    public LiteConfigService(string? configPath = null)
    {
        _configPath = configPath ?? Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    public LiteConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            return Normalize(new LiteConfig());
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<LiteConfig>(json) ?? new LiteConfig();
            return Normalize(config);
        }
        catch (JsonException)
        {
            // config.json corrupto: se parte de defaults sin romper el arranque.
            return Normalize(new LiteConfig());
        }
    }

    public void Save(LiteConfig config)
    {
        var normalized = Normalize(config);
        File.WriteAllText(_configPath, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    public static LiteConfig Normalize(LiteConfig config)
    {
        config.ParallelQueries = Math.Clamp(config.ParallelQueries, 1, 10);
        config.InternalBatchSize = Math.Clamp(config.InternalBatchSize, 50, 10000);
        config.PollingIntervalSeconds = Math.Clamp(config.PollingIntervalSeconds, 10, 600);
        config.MaxRetries = Math.Clamp(config.MaxRetries, 0, 10);

        if (config.Environments.Count == 0)
        {
            config.Environments.Add(new EnvironmentConfig
            {
                Name = "PRO",
                BackendUrl = "https://srbappprodocai.azurewebsites.net",
                FunctionKey = string.Empty
            });
        }

        if (!config.Environments.Any(e => string.Equals(e.Name, config.SelectedEnvironment, StringComparison.OrdinalIgnoreCase)))
        {
            config.SelectedEnvironment = config.Environments[0].Name;
        }

        return config;
    }
}
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): modelos de dominio y servicio de configuracion"
```

---

### Task 3: `LiteRepository` — esquema SQLite y CRUD de ejecuciones/documentos

**Files:**
- Create: `src/DocumentIA.Batch.ClassificationLite/Data/LiteRepository.cs`
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/LiteRepositoryTests.cs`

**Interfaces:**
- Consumes: `LiteDocument`, `LiteDocumentStatus`, `LiteExecution`, `LiteExecutionStatus` (Task 2).
- Produces: `LiteRepository` con constructor `LiteRepository(string dbPath)` (crea carpeta + esquema) y métodos:
  - `static string GetDefaultDbPath()` → `%LocalAppData%\DocumentIA.BatchLite\lite.db`
  - `LiteExecution CreateExecution(string rootPath, bool includeSubfolders, string configSnapshotJson)`
  - `void UpdateExecutionStatus(string executionId, string status, bool setCompletedAt = false)`
  - `LiteExecution? GetIncompleteExecution()` (Running/Paused más reciente)
  - `void InsertDocuments(IEnumerable<LiteDocument> documents)` (transacción)
  - `void UpdateDocument(LiteDocument document)` (por `Id`, todos los campos mutables)
  - `List<LiteDocument> GetDocuments(string executionId)` (orden por `Id`)

- [ ] **Step 1: Escribir tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/LiteRepositoryTests.cs`:

```csharp
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LiteRepository _repository;

    public LiteRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-repo-tests-" + Guid.NewGuid().ToString("N"));
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static LiteDocument NewDoc(string executionId, string name, string status = LiteDocumentStatus.Pending, int batch = 1)
        => new()
        {
            ExecutionId = executionId,
            FileName = name,
            FullPath = @"c:\docs\" + name,
            FileSize = 100,
            LastModifiedUtc = "2026-07-20T10:00:00.0000000Z",
            Status = status,
            BatchNumber = batch
        };

    [Fact]
    public void CreateExecution_YGetIncomplete_LaDevuelve()
    {
        var execution = _repository.CreateExecution(@"\\server\share\docs", includeSubfolders: true, configSnapshotJson: "{}");

        var incomplete = _repository.GetIncompleteExecution();

        Assert.NotNull(incomplete);
        Assert.Equal(execution.ExecutionId, incomplete!.ExecutionId);
        Assert.Equal(LiteExecutionStatus.Running, incomplete.Status);
        Assert.Equal(@"\\server\share\docs", incomplete.RootPath);
        Assert.True(incomplete.IncludeSubfolders);
    }

    [Fact]
    public void UpdateExecutionStatus_Completed_YaNoEsIncompleta()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");

        _repository.UpdateExecutionStatus(execution.ExecutionId, LiteExecutionStatus.Completed, setCompletedAt: true);

        Assert.Null(_repository.GetIncompleteExecution());
    }

    [Fact]
    public void InsertDocuments_YGetDocuments_HacenRoundTrip()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _repository.InsertDocuments(new[] { NewDoc(execution.ExecutionId, "a.pdf"), NewDoc(execution.ExecutionId, "b.pdf") });

        var docs = _repository.GetDocuments(execution.ExecutionId);

        Assert.Equal(2, docs.Count);
        Assert.All(docs, d => Assert.True(d.Id > 0));
        Assert.Equal(new[] { "a.pdf", "b.pdf" }, docs.Select(d => d.FileName).ToArray());
    }

    [Fact]
    public void UpdateDocument_PersisteResultado()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _repository.InsertDocuments(new[] { NewDoc(execution.ExecutionId, "a.pdf") });
        var doc = _repository.GetDocuments(execution.ExecutionId).Single();

        doc.Status = LiteDocumentStatus.Succeeded;
        doc.Tdn1 = "T01";
        doc.Tdn2 = "T01.03";
        doc.Confidence = 0.93;
        doc.Pages = 12;
        doc.PagesIncluded = "1-10";
        doc.DurationMs = 4200;
        doc.InstanceId = "abc";
        doc.StatusQueryUri = "https://x/runtime/webhooks/durabletask/instances/abc";
        doc.RequestJson = "{\"a\":1}";
        doc.ResponseJson = "{\"b\":2}";
        _repository.UpdateDocument(doc);

        var reloaded = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.Succeeded, reloaded.Status);
        Assert.Equal("T01", reloaded.Tdn1);
        Assert.Equal("T01.03", reloaded.Tdn2);
        Assert.Equal(0.93, reloaded.Confidence);
        Assert.Equal(12, reloaded.Pages);
        Assert.Equal("1-10", reloaded.PagesIncluded);
        Assert.Equal(4200, reloaded.DurationMs);
        Assert.Equal("{\"b\":2}", reloaded.ResponseJson);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación (`LiteRepository` no existe).

- [ ] **Step 3: Implementar el repositorio**

`src/DocumentIA.Batch.ClassificationLite/Data/LiteRepository.cs`:

```csharp
using System.IO;
using Dapper;
using DocumentIA.Batch.ClassificationLite.Models;
using Microsoft.Data.Sqlite;

namespace DocumentIA.Batch.ClassificationLite.Data;

public class LiteRepository
{
    private readonly string _connectionString;

    public LiteRepository(string dbPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        EnsureSchema();
    }

    public static string GetDefaultDbPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DocumentIA.BatchLite",
            "lite.db");

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        connection.Execute("PRAGMA journal_mode=WAL;");
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS Executions (
                ExecutionId TEXT PRIMARY KEY,
                RootPath TEXT NOT NULL,
                IncludeSubfolders INTEGER NOT NULL,
                Status TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                ConfigSnapshotJson TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Documents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ExecutionId TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FullPath TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                LastModifiedUtc TEXT NOT NULL,
                Status TEXT NOT NULL,
                BatchNumber INTEGER NOT NULL DEFAULT 0,
                RetryCount INTEGER NOT NULL DEFAULT 0,
                InstanceId TEXT NULL,
                StatusQueryUri TEXT NULL,
                Tdn1 TEXT NULL,
                Tdn2 TEXT NULL,
                Confidence REAL NULL,
                Pages INTEGER NULL,
                PagesIncluded TEXT NULL,
                ProcessDate TEXT NULL,
                DurationMs INTEGER NULL,
                RequestJson TEXT NULL,
                ResponseJson TEXT NULL,
                ErrorMessage TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Documents_Dedup
                ON Documents(FileName, FileSize, LastModifiedUtc);
            CREATE INDEX IF NOT EXISTS IX_Documents_Execution
                ON Documents(ExecutionId, Status);
            """);
        connection.Execute("PRAGMA user_version=1;");
    }

    public LiteExecution CreateExecution(string rootPath, bool includeSubfolders, string configSnapshotJson)
    {
        var execution = new LiteExecution
        {
            ExecutionId = Guid.NewGuid().ToString(),
            RootPath = rootPath,
            IncludeSubfolders = includeSubfolders,
            Status = LiteExecutionStatus.Running,
            StartedAt = DateTime.UtcNow.ToString("O"),
            ConfigSnapshotJson = configSnapshotJson
        };

        using var connection = Open();
        connection.Execute("""
            INSERT INTO Executions (ExecutionId, RootPath, IncludeSubfolders, Status, StartedAt, CompletedAt, ConfigSnapshotJson)
            VALUES (@ExecutionId, @RootPath, @IncludeSubfolders, @Status, @StartedAt, NULL, @ConfigSnapshotJson);
            """, execution);
        return execution;
    }

    public void UpdateExecutionStatus(string executionId, string status, bool setCompletedAt = false)
    {
        using var connection = Open();
        connection.Execute("""
            UPDATE Executions
            SET Status = @status,
                CompletedAt = CASE WHEN @setCompletedAt THEN @now ELSE CompletedAt END
            WHERE ExecutionId = @executionId;
            """, new { executionId, status, setCompletedAt, now = DateTime.UtcNow.ToString("O") });
    }

    public LiteExecution? GetIncompleteExecution()
    {
        using var connection = Open();
        return connection.QueryFirstOrDefault<LiteExecution>("""
            SELECT * FROM Executions
            WHERE Status IN ('Running', 'Paused')
            ORDER BY StartedAt DESC
            LIMIT 1;
            """);
    }

    public void InsertDocuments(IEnumerable<LiteDocument> documents)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("""
            INSERT INTO Documents (
                ExecutionId, FileName, FullPath, FileSize, LastModifiedUtc, Status, BatchNumber, RetryCount,
                InstanceId, StatusQueryUri, Tdn1, Tdn2, Confidence, Pages, PagesIncluded,
                ProcessDate, DurationMs, RequestJson, ResponseJson, ErrorMessage)
            VALUES (
                @ExecutionId, @FileName, @FullPath, @FileSize, @LastModifiedUtc, @Status, @BatchNumber, @RetryCount,
                @InstanceId, @StatusQueryUri, @Tdn1, @Tdn2, @Confidence, @Pages, @PagesIncluded,
                @ProcessDate, @DurationMs, @RequestJson, @ResponseJson, @ErrorMessage);
            """, documents, transaction);
        transaction.Commit();
    }

    public void UpdateDocument(LiteDocument document)
    {
        using var connection = Open();
        connection.Execute("""
            UPDATE Documents SET
                Status = @Status,
                BatchNumber = @BatchNumber,
                RetryCount = @RetryCount,
                InstanceId = @InstanceId,
                StatusQueryUri = @StatusQueryUri,
                Tdn1 = @Tdn1,
                Tdn2 = @Tdn2,
                Confidence = @Confidence,
                Pages = @Pages,
                PagesIncluded = @PagesIncluded,
                ProcessDate = @ProcessDate,
                DurationMs = @DurationMs,
                RequestJson = @RequestJson,
                ResponseJson = @ResponseJson,
                ErrorMessage = @ErrorMessage
            WHERE Id = @Id;
            """, document);
    }

    public List<LiteDocument> GetDocuments(string executionId)
    {
        using var connection = Open();
        return connection.Query<LiteDocument>(
            "SELECT * FROM Documents WHERE ExecutionId = @executionId ORDER BY Id;",
            new { executionId }).ToList();
    }
}
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS (todos los tests).

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): repositorio SQLite con esquema y CRUD basico"
```

---

### Task 4: `LiteRepository` — dedup por histórico, lotes y contadores

**Files:**
- Modify: `src/DocumentIA.Batch.ClassificationLite/Data/LiteRepository.cs` (añadir métodos al final de la clase)
- Create: `src/DocumentIA.Batch.ClassificationLite/Data/LiteCounters.cs`
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/LiteRepositoryQueryTests.cs`

**Interfaces:**
- Consumes: Task 3.
- Produces (métodos nuevos en `LiteRepository`):
  - `LiteDocument? FindLastSucceeded(string fileName, long fileSize, string lastModifiedUtc)` — último `Succeeded` con esa clave triple, en CUALQUIER ejecución.
  - `List<int> GetBatchNumbers(string executionId)` — distinct `BatchNumber` con docs `Pending`/`Error`/`InFlight`, ascendente.
  - `List<LiteDocument> GetPendingBatch(string executionId, int batchNumber)`
  - `List<LiteDocument> GetErrorsInBatch(string executionId, int batchNumber)`
  - `List<LiteDocument> GetInFlight(string executionId)`
  - `LiteCounters GetCounters(string executionId)`
  - Clase `LiteCounters` (ints: `Total`, `Pending`, `InFlight`, `Succeeded`, `Error`, `DefinitiveError`, `SkippedHistory`, `Cancelled`).

- [ ] **Step 1: Escribir tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/LiteRepositoryQueryTests.cs`:

```csharp
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteRepositoryQueryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LiteRepository _repository;

    public LiteRepositoryQueryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-repoq-tests-" + Guid.NewGuid().ToString("N"));
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static LiteDocument NewDoc(string executionId, string name, string status, int batch = 1,
        long size = 100, string modified = "2026-07-20T10:00:00.0000000Z")
        => new()
        {
            ExecutionId = executionId,
            FileName = name,
            FullPath = @"c:\docs\" + name,
            FileSize = size,
            LastModifiedUtc = modified,
            Status = status,
            BatchNumber = batch
        };

    [Fact]
    public void FindLastSucceeded_EncuentraEnOtraEjecucion_IgnorandoRuta()
    {
        var old = _repository.CreateExecution(@"c:\vieja", false, "{}");
        var succeeded = NewDoc(old.ExecutionId, "a.pdf", LiteDocumentStatus.Succeeded);
        succeeded.Tdn1 = "T01";
        succeeded.Tdn2 = "T01.02";
        succeeded.Confidence = 0.9;
        _repository.InsertDocuments(new[] { succeeded });

        var found = _repository.FindLastSucceeded("a.pdf", 100, "2026-07-20T10:00:00.0000000Z");

        Assert.NotNull(found);
        Assert.Equal("T01", found!.Tdn1);
        Assert.Equal("T01.02", found.Tdn2);
    }

    [Fact]
    public void FindLastSucceeded_NoEncuentraSiCambiaClave_OSiNoEsSucceeded()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _repository.InsertDocuments(new[]
        {
            NewDoc(execution.ExecutionId, "a.pdf", LiteDocumentStatus.Error),
            NewDoc(execution.ExecutionId, "b.pdf", LiteDocumentStatus.Succeeded, size: 999)
        });

        Assert.Null(_repository.FindLastSucceeded("a.pdf", 100, "2026-07-20T10:00:00.0000000Z"));
        Assert.Null(_repository.FindLastSucceeded("b.pdf", 100, "2026-07-20T10:00:00.0000000Z"));
    }

    [Fact]
    public void GetBatchNumbers_SoloLotesConTrabajoPendiente()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _repository.InsertDocuments(new[]
        {
            NewDoc(execution.ExecutionId, "a.pdf", LiteDocumentStatus.Succeeded, batch: 1),
            NewDoc(execution.ExecutionId, "b.pdf", LiteDocumentStatus.Pending, batch: 2),
            NewDoc(execution.ExecutionId, "c.pdf", LiteDocumentStatus.Error, batch: 3),
            NewDoc(execution.ExecutionId, "d.pdf", LiteDocumentStatus.SkippedHistory, batch: 4)
        });

        Assert.Equal(new[] { 2, 3 }, _repository.GetBatchNumbers(execution.ExecutionId).ToArray());
    }

    [Fact]
    public void GetPendingBatch_GetErrorsInBatch_GetInFlight_FiltranPorEstadoYLote()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _repository.InsertDocuments(new[]
        {
            NewDoc(execution.ExecutionId, "a.pdf", LiteDocumentStatus.Pending, batch: 1),
            NewDoc(execution.ExecutionId, "b.pdf", LiteDocumentStatus.Error, batch: 1),
            NewDoc(execution.ExecutionId, "c.pdf", LiteDocumentStatus.Pending, batch: 2),
            NewDoc(execution.ExecutionId, "d.pdf", LiteDocumentStatus.InFlight, batch: 1)
        });

        Assert.Equal(new[] { "a.pdf" }, _repository.GetPendingBatch(execution.ExecutionId, 1).Select(d => d.FileName).ToArray());
        Assert.Equal(new[] { "b.pdf" }, _repository.GetErrorsInBatch(execution.ExecutionId, 1).Select(d => d.FileName).ToArray());
        Assert.Equal(new[] { "d.pdf" }, _repository.GetInFlight(execution.ExecutionId).Select(d => d.FileName).ToArray());
    }

    [Fact]
    public void GetCounters_AgregaPorEstado()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _repository.InsertDocuments(new[]
        {
            NewDoc(execution.ExecutionId, "a.pdf", LiteDocumentStatus.Pending),
            NewDoc(execution.ExecutionId, "b.pdf", LiteDocumentStatus.Pending),
            NewDoc(execution.ExecutionId, "c.pdf", LiteDocumentStatus.InFlight),
            NewDoc(execution.ExecutionId, "d.pdf", LiteDocumentStatus.Succeeded),
            NewDoc(execution.ExecutionId, "e.pdf", LiteDocumentStatus.DefinitiveError),
            NewDoc(execution.ExecutionId, "f.pdf", LiteDocumentStatus.SkippedHistory)
        });

        var counters = _repository.GetCounters(execution.ExecutionId);

        Assert.Equal(6, counters.Total);
        Assert.Equal(2, counters.Pending);
        Assert.Equal(1, counters.InFlight);
        Assert.Equal(1, counters.Succeeded);
        Assert.Equal(1, counters.DefinitiveError);
        Assert.Equal(1, counters.SkippedHistory);
        Assert.Equal(0, counters.Error);
        Assert.Equal(0, counters.Cancelled);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación (métodos no existen).

- [ ] **Step 3: Implementar**

`src/DocumentIA.Batch.ClassificationLite/Data/LiteCounters.cs`:

```csharp
namespace DocumentIA.Batch.ClassificationLite.Data;

public class LiteCounters
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int InFlight { get; set; }
    public int Succeeded { get; set; }
    public int Error { get; set; }
    public int DefinitiveError { get; set; }
    public int SkippedHistory { get; set; }
    public int Cancelled { get; set; }
}
```

Añadir al final de la clase `LiteRepository` (antes de la llave de cierre):

```csharp
    public LiteDocument? FindLastSucceeded(string fileName, long fileSize, string lastModifiedUtc)
    {
        using var connection = Open();
        return connection.QueryFirstOrDefault<LiteDocument>("""
            SELECT * FROM Documents
            WHERE FileName = @fileName AND FileSize = @fileSize AND LastModifiedUtc = @lastModifiedUtc
              AND Status = 'Succeeded'
            ORDER BY Id DESC
            LIMIT 1;
            """, new { fileName, fileSize, lastModifiedUtc });
    }

    public List<int> GetBatchNumbers(string executionId)
    {
        using var connection = Open();
        return connection.Query<int>("""
            SELECT DISTINCT BatchNumber FROM Documents
            WHERE ExecutionId = @executionId AND Status IN ('Pending', 'Error', 'InFlight')
            ORDER BY BatchNumber;
            """, new { executionId }).ToList();
    }

    public List<LiteDocument> GetPendingBatch(string executionId, int batchNumber)
    {
        using var connection = Open();
        return connection.Query<LiteDocument>("""
            SELECT * FROM Documents
            WHERE ExecutionId = @executionId AND BatchNumber = @batchNumber AND Status = 'Pending'
            ORDER BY Id;
            """, new { executionId, batchNumber }).ToList();
    }

    public List<LiteDocument> GetErrorsInBatch(string executionId, int batchNumber)
    {
        using var connection = Open();
        return connection.Query<LiteDocument>("""
            SELECT * FROM Documents
            WHERE ExecutionId = @executionId AND BatchNumber = @batchNumber AND Status = 'Error'
            ORDER BY Id;
            """, new { executionId, batchNumber }).ToList();
    }

    public List<LiteDocument> GetInFlight(string executionId)
    {
        using var connection = Open();
        return connection.Query<LiteDocument>("""
            SELECT * FROM Documents
            WHERE ExecutionId = @executionId AND Status = 'InFlight'
            ORDER BY Id;
            """, new { executionId }).ToList();
    }

    public LiteCounters GetCounters(string executionId)
    {
        using var connection = Open();
        return connection.QuerySingle<LiteCounters>("""
            SELECT
                COUNT(*) AS Total,
                COALESCE(SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END), 0) AS Pending,
                COALESCE(SUM(CASE WHEN Status = 'InFlight' THEN 1 ELSE 0 END), 0) AS InFlight,
                COALESCE(SUM(CASE WHEN Status = 'Succeeded' THEN 1 ELSE 0 END), 0) AS Succeeded,
                COALESCE(SUM(CASE WHEN Status = 'Error' THEN 1 ELSE 0 END), 0) AS Error,
                COALESCE(SUM(CASE WHEN Status = 'DefinitiveError' THEN 1 ELSE 0 END), 0) AS DefinitiveError,
                COALESCE(SUM(CASE WHEN Status = 'SkippedHistory' THEN 1 ELSE 0 END), 0) AS SkippedHistory,
                COALESCE(SUM(CASE WHEN Status = 'Cancelled' THEN 1 ELSE 0 END), 0) AS Cancelled
            FROM Documents
            WHERE ExecutionId = @executionId;
            """, new { executionId });
    }
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): consultas de dedup, lotes y contadores en el repositorio"
```

---

### Task 5: `LiteRequestFactory` — construcción del `IngestRequest` y JSON almacenable sin base64

**Files:**
- Create: `src/DocumentIA.Batch.ClassificationLite/Engine/LiteRequestFactory.cs`
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/LiteRequestFactoryTests.cs`

**Interfaces:**
- Consumes: `LiteConfig` (Task 2); DTOs existentes de `DocumentIA.Batch.Services`: `IngestRequest`, `IngestInstrucciones`, `IngestIaConfig`, `IngestDocumento`, `IngestDocumentoContent`, `IngestTrazabilidad` (propiedades exactas: `Instrucciones.ExpectedType/ClassificationOnly/ExecuteIntegrarWhenClassificationOnly/MaxPagesForClassificationOnly/ForzarResumenPorDefecto/SkipDuplicateCheck/ForceReprocess/SkipGdcUpload/Classification/Extraction`, `IngestIaConfig.Provider/Model/NivelClasificacion`, `Documento.Name/Content.Base64`, `Trazabilidad.CorrelationId/SubmittedBy`).
- Produces:
  - `static IngestRequest LiteRequestFactory.Build(LiteConfig config, string fileName, byte[] fileBytes, string correlationId)`
  - `static string LiteRequestFactory.BuildRequestJsonForStorage(IngestRequest request, long fileSizeBytes)` — JSON indentado con `base64` sustituido por `"<base64 omitido, N bytes>"`.

Reglas de negocio (idénticas a la herramienta actual, `ClassificationMainViewModel.BuildIngestRequest`):
- `effectiveClassificationOnly = config.OnlyClassification || config.ClassificationLevel == "TDN1"` (case-insensitive).
- `ExecuteIntegrarWhenClassificationOnly = effective ? false : null` (Execute Integrity fijo FALSE en Lite).
- `MaxPagesForClassificationOnly = effective ? 10 : 0`.
- `ForzarResumenPorDefecto = null` (no se envía; el backend decide).
- `SkipDuplicateCheck = false`; `ForceReprocess = config.ForceReprocess`; `SkipGdcUpload = true`.
- `Classification = { Provider = config.Provider, Model = config.Model, NivelClasificacion = config.ClassificationLevel == "DEFAULT" ? null : config.ClassificationLevel }`.
- `Extraction = { Provider = "auto", Model = "auto" }`; `ExpectedType = string.Empty`.
- `Trazabilidad.SubmittedBy = "DocumentIA.Batch.ClassificationLite"`.

- [ ] **Step 1: Escribir tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/LiteRequestFactoryTests.cs`:

```csharp
using System.Text;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteRequestFactoryTests
{
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("pdf-fake");

    [Fact]
    public void Build_ConDefaults_EsClassificationOnlyTdn1Tdn2()
    {
        var config = new LiteConfig();

        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        Assert.True(request.Instrucciones.ClassificationOnly);
        Assert.False(request.Instrucciones.ExecuteIntegrarWhenClassificationOnly);
        Assert.Equal(10, request.Instrucciones.MaxPagesForClassificationOnly);
        Assert.Null(request.Instrucciones.ForzarResumenPorDefecto);
        Assert.False(request.Instrucciones.SkipDuplicateCheck);
        Assert.False(request.Instrucciones.ForceReprocess);
        Assert.True(request.Instrucciones.SkipGdcUpload);
        Assert.Equal("auto", request.Instrucciones.Classification.Provider);
        Assert.Equal("auto", request.Instrucciones.Classification.Model);
        Assert.Equal("TDN1_TDN2", request.Instrucciones.Classification.NivelClasificacion);
        Assert.Equal(string.Empty, request.Instrucciones.ExpectedType);
        Assert.Equal("doc.pdf", request.Documento.Name);
        Assert.Equal(Convert.ToBase64String(Bytes), request.Documento.Content.Base64);
        Assert.Equal("corr-1", request.Trazabilidad.CorrelationId);
        Assert.Equal("DocumentIA.Batch.ClassificationLite", request.Trazabilidad.SubmittedBy);
    }

    [Fact]
    public void Build_NivelDefault_NoEnviaNivel()
    {
        var config = new LiteConfig { ClassificationLevel = "DEFAULT" };

        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        Assert.Null(request.Instrucciones.Classification.NivelClasificacion);
    }

    [Fact]
    public void Build_NivelTdn1_FuerzaClassificationOnly()
    {
        var config = new LiteConfig { ClassificationLevel = "TDN1", OnlyClassification = false };

        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        Assert.True(request.Instrucciones.ClassificationOnly);
    }

    [Fact]
    public void Build_SinOnlyClassificationNiTdn1_NoLimitaPaginas()
    {
        var config = new LiteConfig { OnlyClassification = false };

        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        Assert.False(request.Instrucciones.ClassificationOnly);
        Assert.Null(request.Instrucciones.ExecuteIntegrarWhenClassificationOnly);
        Assert.Equal(0, request.Instrucciones.MaxPagesForClassificationOnly);
    }

    [Fact]
    public void Build_ForceReprocess_SePropaga()
    {
        var config = new LiteConfig { ForceReprocess = true };

        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        Assert.True(request.Instrucciones.ForceReprocess);
    }

    [Fact]
    public void BuildRequestJsonForStorage_SustituyeBase64PorPlaceholder()
    {
        var config = new LiteConfig();
        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        var json = LiteRequestFactory.BuildRequestJsonForStorage(request, 123456);

        Assert.Contains("<base64 omitido, 123456 bytes>", json);
        Assert.DoesNotContain(Convert.ToBase64String(Bytes), json);
        // El request original no se muta:
        Assert.Equal(Convert.ToBase64String(Bytes), request.Documento.Content.Base64);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación (`LiteRequestFactory` no existe).

- [ ] **Step 3: Implementar**

`src/DocumentIA.Batch.ClassificationLite/Engine/LiteRequestFactory.cs`:

```csharp
using System.Text.Json;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.ClassificationLite.Engine;

public static class LiteRequestFactory
{
    private static readonly JsonSerializerOptions StorageJsonOptions = new() { WriteIndented = true };

    public static IngestRequest Build(LiteConfig config, string fileName, byte[] fileBytes, string correlationId)
    {
        var isTdn1Level = string.Equals(config.ClassificationLevel, "TDN1", StringComparison.OrdinalIgnoreCase);
        var effectiveClassificationOnly = config.OnlyClassification || isTdn1Level;
        var isDefaultLevel = string.Equals(config.ClassificationLevel, "DEFAULT", StringComparison.OrdinalIgnoreCase);

        return new IngestRequest
        {
            Instrucciones = new IngestInstrucciones
            {
                ExpectedType = string.Empty,
                ClassificationOnly = effectiveClassificationOnly,
                ExecuteIntegrarWhenClassificationOnly = effectiveClassificationOnly ? false : null,
                MaxPagesForClassificationOnly = effectiveClassificationOnly ? 10 : 0,
                ForzarResumenPorDefecto = null,
                SkipDuplicateCheck = false,
                ForceReprocess = config.ForceReprocess,
                SkipGdcUpload = true,
                Classification = new IngestIaConfig
                {
                    Provider = config.Provider,
                    Model = config.Model,
                    NivelClasificacion = isDefaultLevel ? null : config.ClassificationLevel
                },
                Extraction = new IngestIaConfig
                {
                    Provider = "auto",
                    Model = "auto"
                }
            },
            Documento = new IngestDocumento
            {
                Name = fileName,
                Content = new IngestDocumentoContent
                {
                    Base64 = Convert.ToBase64String(fileBytes)
                }
            },
            Trazabilidad = new IngestTrazabilidad
            {
                CorrelationId = correlationId,
                SubmittedBy = "DocumentIA.Batch.ClassificationLite"
            }
        };
    }

    public static string BuildRequestJsonForStorage(IngestRequest request, long fileSizeBytes)
    {
        var storageCopy = new IngestRequest
        {
            Instrucciones = request.Instrucciones,
            Trazabilidad = request.Trazabilidad,
            Documento = new IngestDocumento
            {
                Name = request.Documento.Name,
                Content = new IngestDocumentoContent
                {
                    Base64 = $"<base64 omitido, {fileSizeBytes} bytes>"
                }
            }
        };

        return JsonSerializer.Serialize(storageCopy, StorageJsonOptions);
    }
}
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): factoria de requests con JSON almacenable sin base64"
```

---

### Task 6: `AdaptivePollingStrategy`

**Files:**
- Create: `src/DocumentIA.Batch.ClassificationLite/Engine/AdaptivePollingStrategy.cs`
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/AdaptivePollingStrategyTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `AdaptivePollingStrategy` con:
  - ctor `AdaptivePollingStrategy(int steadyIntervalSeconds, int timeoutMinutes = 30)`
  - `TimeSpan GetDelay(int attempt)` — attempt 0-based: 3s, 5s, 10s, 20s, 30s y después siempre `steadyIntervalSeconds`. Si `steadyIntervalSeconds` es menor que un escalón inicial, se usa el menor de los dos.
  - `bool IsTimedOut(TimeSpan elapsed)`

- [ ] **Step 1: Escribir tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/AdaptivePollingStrategyTests.cs`:

```csharp
using DocumentIA.Batch.ClassificationLite.Engine;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class AdaptivePollingStrategyTests
{
    [Fact]
    public void GetDelay_SigueLaSecuenciaAdaptativaYSeEstabiliza()
    {
        var strategy = new AdaptivePollingStrategy(steadyIntervalSeconds: 60);

        Assert.Equal(TimeSpan.FromSeconds(3), strategy.GetDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(5), strategy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(20), strategy.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(30), strategy.GetDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(60), strategy.GetDelay(5));
        Assert.Equal(TimeSpan.FromSeconds(60), strategy.GetDelay(50));
    }

    [Fact]
    public void GetDelay_ConIntervaloCorto_NuncaSuperaElIntervalo()
    {
        var strategy = new AdaptivePollingStrategy(steadyIntervalSeconds: 10);

        Assert.Equal(TimeSpan.FromSeconds(3), strategy.GetDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.GetDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.GetDelay(9));
    }

    [Fact]
    public void IsTimedOut_RespetaElLimite()
    {
        var strategy = new AdaptivePollingStrategy(steadyIntervalSeconds: 60, timeoutMinutes: 30);

        Assert.False(strategy.IsTimedOut(TimeSpan.FromMinutes(29)));
        Assert.True(strategy.IsTimedOut(TimeSpan.FromMinutes(30)));
        Assert.True(strategy.IsTimedOut(TimeSpan.FromMinutes(31)));
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar**

`src/DocumentIA.Batch.ClassificationLite/Engine/AdaptivePollingStrategy.cs`:

```csharp
namespace DocumentIA.Batch.ClassificationLite.Engine;

/// <summary>
/// Polling adaptativo por documento: sondeos rapidos al inicio (detecta docs rapidos)
/// con backoff hasta el intervalo configurado (no castiga el status endpoint en docs lentos).
/// </summary>
public class AdaptivePollingStrategy
{
    private static readonly int[] InitialDelaysSeconds = { 3, 5, 10, 20, 30 };

    private readonly int _steadyIntervalSeconds;
    private readonly TimeSpan _timeout;

    public AdaptivePollingStrategy(int steadyIntervalSeconds, int timeoutMinutes = 30)
    {
        _steadyIntervalSeconds = Math.Max(1, steadyIntervalSeconds);
        _timeout = TimeSpan.FromMinutes(timeoutMinutes);
    }

    public TimeSpan GetDelay(int attempt)
    {
        var seconds = attempt >= 0 && attempt < InitialDelaysSeconds.Length
            ? Math.Min(InitialDelaysSeconds[attempt], _steadyIntervalSeconds)
            : _steadyIntervalSeconds;
        return TimeSpan.FromSeconds(seconds);
    }

    public bool IsTimedOut(TimeSpan elapsed) => elapsed >= _timeout;
}
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): estrategia de polling adaptativo"
```

---

### Task 7: `LiteResultParser` — extracción de campos del `output` de Durable Functions

**Files:**
- Create: `src/DocumentIA.Batch.ClassificationLite/Engine/LiteResultParser.cs`
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/LiteResultParserTests.cs`

**Interfaces:**
- Consumes: `System.Text.Json.JsonElement` (el `Output` de `DurableStatusResponse`).
- Produces:
  - `LiteClassificationResult` (`string? Tdn1`, `string? Tdn2`, `double? Confidence`, `int? Pages`, `string? PagesIncluded`, `string? ProcessDate`, `long? DurationMs`, `string? Estado`).
  - `static LiteClassificationResult LiteResultParser.Parse(JsonElement output)`.

Rutas del output (con fallback PascalCase/camelCase, igual que la herramienta actual):
- `Tdn1` ← `Identificacion.Tdn1`
- `Tdn2` ← `Identificacion.Tdn2`, fallback `DetalleEjecucion.Clasificacion.Tdn2Detectado`
- `Confidence` ← `Resultado.ConfianzaGlobal`, fallback `DetalleEjecucion.Clasificacion.Confianza` (puede venir como número o string con InvariantCulture)
- `Pages` ← `Identificacion.Paginas` (número o string)
- `PagesIncluded` ← `DetalleEjecucion.PaginasIncluidas` (se guarda como string tal cual)
- `ProcessDate` ← `Identificacion.FechaProceso`
- `DurationMs` ← `DetalleEjecucion.Seguimiento.DuracionTotalMs` (número o string)
- `Estado` ← `Resultado.Estado`

- [ ] **Step 1: Escribir tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/LiteResultParserTests.cs`:

```csharp
using System.Text.Json;
using DocumentIA.Batch.ClassificationLite.Engine;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteResultParserTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Parse_OutputPascalCase_ExtraeTodosLosCampos()
    {
        var output = Parse("""
            {
              "Identificacion": { "Tdn1": "T01", "Tdn2": "T01.02", "Paginas": 14, "FechaProceso": "2026-07-20T10:00:00Z" },
              "Resultado": { "Estado": "OK", "ConfianzaGlobal": 0.91 },
              "DetalleEjecucion": {
                "PaginasIncluidas": "1-10",
                "Clasificacion": { "Confianza": 0.85, "Tdn2Detectado": "T01.99" },
                "Seguimiento": { "DuracionTotalMs": 5230 }
              }
            }
            """);

        var result = LiteResultParser.Parse(output);

        Assert.Equal("T01", result.Tdn1);
        Assert.Equal("T01.02", result.Tdn2);
        Assert.Equal(0.91, result.Confidence);
        Assert.Equal(14, result.Pages);
        Assert.Equal("1-10", result.PagesIncluded);
        Assert.Equal("2026-07-20T10:00:00Z", result.ProcessDate);
        Assert.Equal(5230, result.DurationMs);
        Assert.Equal("OK", result.Estado);
    }

    [Fact]
    public void Parse_OutputCamelCase_ConFallbacks()
    {
        var output = Parse("""
            {
              "identificacion": { "tdn1": "T05", "paginas": "7" },
              "resultado": { "estado": "OK" },
              "detalleEjecucion": {
                "paginasIncluidas": "3",
                "clasificacion": { "confianza": "0.72", "tdn2Detectado": "T05.01" },
                "seguimiento": { "duracionTotalMs": "8100" }
              }
            }
            """);

        var result = LiteResultParser.Parse(output);

        Assert.Equal("T05", result.Tdn1);
        Assert.Equal("T05.01", result.Tdn2);
        Assert.Equal(0.72, result.Confidence);
        Assert.Equal(7, result.Pages);
        Assert.Equal("3", result.PagesIncluded);
        Assert.Equal(8100, result.DurationMs);
    }

    [Fact]
    public void Parse_OutputVacio_DevuelveNulos()
    {
        var result = LiteResultParser.Parse(Parse("{}"));

        Assert.Null(result.Tdn1);
        Assert.Null(result.Tdn2);
        Assert.Null(result.Confidence);
        Assert.Null(result.Pages);
        Assert.Null(result.PagesIncluded);
        Assert.Null(result.ProcessDate);
        Assert.Null(result.DurationMs);
        Assert.Null(result.Estado);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar**

`src/DocumentIA.Batch.ClassificationLite/Engine/LiteResultParser.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace DocumentIA.Batch.ClassificationLite.Engine;

public class LiteClassificationResult
{
    public string? Tdn1 { get; set; }
    public string? Tdn2 { get; set; }
    public double? Confidence { get; set; }
    public int? Pages { get; set; }
    public string? PagesIncluded { get; set; }
    public string? ProcessDate { get; set; }
    public long? DurationMs { get; set; }
    public string? Estado { get; set; }
}

public static class LiteResultParser
{
    public static LiteClassificationResult Parse(JsonElement output)
    {
        var identificacion = GetProperty(output, "Identificacion", "identificacion");
        var resultado = GetProperty(output, "Resultado", "resultado");
        var detalle = GetProperty(output, "DetalleEjecucion", "detalleEjecucion");
        var clasificacion = detalle.HasValue ? GetProperty(detalle.Value, "Clasificacion", "clasificacion") : null;
        var seguimiento = detalle.HasValue ? GetProperty(detalle.Value, "Seguimiento", "seguimiento") : null;

        return new LiteClassificationResult
        {
            Tdn1 = GetString(identificacion, "Tdn1", "tdn1"),
            Tdn2 = FirstNonEmpty(
                GetString(identificacion, "Tdn2", "tdn2"),
                GetString(clasificacion, "Tdn2Detectado", "tdn2Detectado")),
            Confidence = GetDouble(resultado, "ConfianzaGlobal", "confianzaGlobal")
                ?? GetDouble(clasificacion, "Confianza", "confianza"),
            Pages = (int?)GetLong(identificacion, "Paginas", "paginas"),
            PagesIncluded = GetString(detalle, "PaginasIncluidas", "paginasIncluidas"),
            ProcessDate = GetString(identificacion, "FechaProceso", "fechaProceso"),
            DurationMs = GetLong(seguimiento, "DuracionTotalMs", "duracionTotalMs"),
            Estado = GetString(resultado, "Estado", "estado")
        };
    }

    private static JsonElement? GetProperty(JsonElement source, params string[] names)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (source.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement? source, params string[] names)
    {
        if (!source.HasValue)
        {
            return null;
        }

        var property = GetProperty(source.Value, names);
        if (!property.HasValue)
        {
            return null;
        }

        return property.Value.ValueKind switch
        {
            JsonValueKind.String => property.Value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
            _ => null
        };
    }

    private static double? GetDouble(JsonElement? source, params string[] names)
    {
        if (!source.HasValue)
        {
            return null;
        }

        var property = GetProperty(source.Value, names);
        if (!property.HasValue)
        {
            return null;
        }

        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var number))
        {
            return number;
        }

        if (property.Value.ValueKind == JsonValueKind.String
            && double.TryParse(property.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static long? GetLong(JsonElement? source, params string[] names)
    {
        if (!source.HasValue)
        {
            return null;
        }

        var property = GetProperty(source.Value, names);
        if (!property.HasValue)
        {
            return null;
        }

        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var number))
        {
            return number;
        }

        if (property.Value.ValueKind == JsonValueKind.String
            && long.TryParse(property.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): parser del output de clasificacion"
```

---

### Task 8: `FolderScanner` — escaneo progresivo con dedup y asignación de lotes

**Files:**
- Create: `src/DocumentIA.Batch.ClassificationLite/Engine/FolderScanner.cs`
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/FolderScannerTests.cs`

**Interfaces:**
- Consumes: `LiteRepository` (Tasks 3-4), `LiteDocument`/`LiteDocumentStatus` (Task 2).
- Produces:
  - `ScanResult` (`int TotalFound`, `int Enqueued`, `int Skipped`, `int Failed`).
  - `FolderScanner` con ctor `FolderScanner(LiteRepository repository)`, evento `event Action<ScanResult>? Progress` (se dispara tras cada inserción por chunks) y método:
  - `ScanResult Scan(string executionId, IReadOnlyList<string> paths, bool includeSubfolders, bool skipAlreadyProcessed, bool forceReprocess, int internalBatchSize, CancellationToken cancellationToken)`

Reglas:
- `paths` puede mezclar ficheros y carpetas (drag & drop). Solo `*.pdf` (case-insensitive). Carpetas: `Directory.EnumerateFiles` con `AllDirectories` si `includeSubfolders`.
- Dedup: si `skipAlreadyProcessed && !forceReprocess` y `FindLastSucceeded(FileName, FileSize, LastModifiedUtc)` devuelve fila → insertar como `SkippedHistory` copiando `Tdn1/Tdn2/Confidence/Pages/PagesIncluded/ProcessDate/DurationMs`, `BatchNumber=0`.
- Resto → `Pending` con `BatchNumber = (enqueued / internalBatchSize) + 1` (enqueued cuenta desde 0 ANTES de incrementar: los primeros `internalBatchSize` docs → lote 1).
- Fichero al que no se puede acceder (`IOException`/`UnauthorizedAccessException` al leer metadata) → fila `Error` con `ErrorMessage`, cuenta como enqueued (el motor lo reintentará).
- Directorio ilegible → se ignora (no aborta el escaneo).
- Inserciones por chunks de 500 en transacción; `Progress` tras cada chunk.

- [ ] **Step 1: Escribir tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/FolderScannerTests.cs`:

```csharp
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class FolderScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _docsDir;
    private readonly LiteRepository _repository;
    private readonly FolderScanner _scanner;

    public FolderScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-scan-tests-" + Guid.NewGuid().ToString("N"));
        _docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(_docsDir);
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
        _scanner = new FolderScanner(_repository);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string CreatePdf(string relativePath, string content = "pdf")
    {
        var fullPath = Path.Combine(_docsDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    [Fact]
    public void Scan_EncolaSoloPdfs_YAsignaLotes()
    {
        CreatePdf("a.pdf");
        CreatePdf("b.pdf");
        CreatePdf("c.pdf");
        CreatePdf("ignorame.txt");
        var execution = _repository.CreateExecution(_docsDir, false, "{}");

        var result = _scanner.Scan(execution.ExecutionId, new[] { _docsDir }, includeSubfolders: false,
            skipAlreadyProcessed: true, forceReprocess: false, internalBatchSize: 2, CancellationToken.None);

        Assert.Equal(3, result.TotalFound);
        Assert.Equal(3, result.Enqueued);
        Assert.Equal(0, result.Skipped);
        var docs = _repository.GetDocuments(execution.ExecutionId);
        Assert.Equal(3, docs.Count);
        Assert.All(docs, d => Assert.Equal(LiteDocumentStatus.Pending, d.Status));
        Assert.Equal(new[] { 1, 1, 2 }, docs.OrderBy(d => d.Id).Select(d => d.BatchNumber).ToArray());
        Assert.All(docs, d => Assert.True(d.FileSize > 0));
        Assert.All(docs, d => Assert.NotEqual(string.Empty, d.LastModifiedUtc));
    }

    [Fact]
    public void Scan_SinSubcarpetas_IgnoraAnidados_ConSubcarpetas_LosIncluye()
    {
        CreatePdf("raiz.pdf");
        CreatePdf(Path.Combine("sub", "anidado.pdf"));

        var e1 = _repository.CreateExecution(_docsDir, false, "{}");
        var r1 = _scanner.Scan(e1.ExecutionId, new[] { _docsDir }, false, true, false, 1000, CancellationToken.None);
        Assert.Equal(1, r1.TotalFound);

        var e2 = _repository.CreateExecution(_docsDir, true, "{}");
        var r2 = _scanner.Scan(e2.ExecutionId, new[] { _docsDir }, true, true, false, 1000, CancellationToken.None);
        Assert.Equal(2, r2.TotalFound);
    }

    [Fact]
    public void Scan_DocumentoYaProcesado_SeOmiteConDatosHistoricos()
    {
        var path = CreatePdf("repetido.pdf");
        var info = new FileInfo(path);
        var previous = _repository.CreateExecution(@"c:\otra", false, "{}");
        _repository.InsertDocuments(new[]
        {
            new LiteDocument
            {
                ExecutionId = previous.ExecutionId,
                FileName = "repetido.pdf",
                FullPath = @"c:\otra\repetido.pdf",
                FileSize = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc.ToString("O"),
                Status = LiteDocumentStatus.Succeeded,
                Tdn1 = "T09",
                Tdn2 = "T09.01",
                Confidence = 0.88,
                Pages = 3,
                PagesIncluded = "1-3",
                ProcessDate = "2026-07-19T09:00:00Z",
                DurationMs = 1500
            }
        });

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var result = _scanner.Scan(execution.ExecutionId, new[] { _docsDir }, false, true, false, 1000, CancellationToken.None);

        Assert.Equal(1, result.TotalFound);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Enqueued);
        var doc = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.SkippedHistory, doc.Status);
        Assert.Equal("T09", doc.Tdn1);
        Assert.Equal("T09.01", doc.Tdn2);
        Assert.Equal(0.88, doc.Confidence);
        Assert.Equal("1-3", doc.PagesIncluded);
    }

    [Fact]
    public void Scan_ConForceReprocess_NoOmiteNada()
    {
        var path = CreatePdf("repetido.pdf");
        var info = new FileInfo(path);
        var previous = _repository.CreateExecution(@"c:\otra", false, "{}");
        _repository.InsertDocuments(new[]
        {
            new LiteDocument
            {
                ExecutionId = previous.ExecutionId,
                FileName = "repetido.pdf",
                FullPath = @"c:\otra\repetido.pdf",
                FileSize = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc.ToString("O"),
                Status = LiteDocumentStatus.Succeeded
            }
        });

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var result = _scanner.Scan(execution.ExecutionId, new[] { _docsDir }, false, true, true, 1000, CancellationToken.None);

        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Enqueued);
        Assert.Equal(LiteDocumentStatus.Pending, _repository.GetDocuments(execution.ExecutionId).Single().Status);
    }

    [Fact]
    public void Scan_FicheroSuelto_TambienSeEncola()
    {
        var path = CreatePdf("suelto.pdf");
        var execution = _repository.CreateExecution(path, false, "{}");

        var result = _scanner.Scan(execution.ExecutionId, new[] { path }, false, true, false, 1000, CancellationToken.None);

        Assert.Equal(1, result.Enqueued);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar**

`src/DocumentIA.Batch.ClassificationLite/Engine/FolderScanner.cs`:

```csharp
using System.IO;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.Engine;

public class ScanResult
{
    public int TotalFound { get; set; }
    public int Enqueued { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}

public class FolderScanner
{
    private const int InsertChunkSize = 500;

    private readonly LiteRepository _repository;

    public FolderScanner(LiteRepository repository)
    {
        _repository = repository;
    }

    public event Action<ScanResult>? Progress;

    public ScanResult Scan(
        string executionId,
        IReadOnlyList<string> paths,
        bool includeSubfolders,
        bool skipAlreadyProcessed,
        bool forceReprocess,
        int internalBatchSize,
        CancellationToken cancellationToken)
    {
        var result = new ScanResult();
        var buffer = new List<LiteDocument>(InsertChunkSize);
        var enqueued = 0;

        foreach (var fullPath in EnumeratePdfFiles(paths, includeSubfolders))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.TotalFound++;

            LiteDocument document;
            try
            {
                var info = new FileInfo(fullPath);
                var fileSize = info.Length;
                var lastModifiedUtc = info.LastWriteTimeUtc.ToString("O");

                var historical = skipAlreadyProcessed && !forceReprocess
                    ? _repository.FindLastSucceeded(info.Name, fileSize, lastModifiedUtc)
                    : null;

                if (historical is not null)
                {
                    result.Skipped++;
                    document = new LiteDocument
                    {
                        ExecutionId = executionId,
                        FileName = info.Name,
                        FullPath = fullPath,
                        FileSize = fileSize,
                        LastModifiedUtc = lastModifiedUtc,
                        Status = LiteDocumentStatus.SkippedHistory,
                        BatchNumber = 0,
                        Tdn1 = historical.Tdn1,
                        Tdn2 = historical.Tdn2,
                        Confidence = historical.Confidence,
                        Pages = historical.Pages,
                        PagesIncluded = historical.PagesIncluded,
                        ProcessDate = historical.ProcessDate,
                        DurationMs = historical.DurationMs
                    };
                }
                else
                {
                    document = new LiteDocument
                    {
                        ExecutionId = executionId,
                        FileName = info.Name,
                        FullPath = fullPath,
                        FileSize = fileSize,
                        LastModifiedUtc = lastModifiedUtc,
                        Status = LiteDocumentStatus.Pending,
                        BatchNumber = (enqueued / internalBatchSize) + 1
                    };
                    enqueued++;
                    result.Enqueued++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Failed++;
                document = new LiteDocument
                {
                    ExecutionId = executionId,
                    FileName = Path.GetFileName(fullPath),
                    FullPath = fullPath,
                    FileSize = 0,
                    LastModifiedUtc = string.Empty,
                    Status = LiteDocumentStatus.Error,
                    ErrorMessage = "No se pudo leer el fichero: " + ex.Message,
                    BatchNumber = (enqueued / internalBatchSize) + 1
                };
                enqueued++;
            }

            buffer.Add(document);
            if (buffer.Count >= InsertChunkSize)
            {
                _repository.InsertDocuments(buffer);
                buffer.Clear();
                Progress?.Invoke(result);
            }
        }

        if (buffer.Count > 0)
        {
            _repository.InsertDocuments(buffer);
            Progress?.Invoke(result);
        }

        return result;
    }

    private static IEnumerable<string> EnumeratePdfFiles(IReadOnlyList<string> paths, bool includeSubfolders)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    yield return path;
                }

                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(
                    path,
                    "*.pdf",
                    includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }
}
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): escaner progresivo con dedup historico y lotes"
```

---

### Task 9: `IIngestBackend` + núcleo de `LiteEngine` (procesar documentos con semáforo y polling)

**Files:**
- Create: `src/DocumentIA.Batch.ClassificationLite/Engine/IIngestBackend.cs`
- Create: `src/DocumentIA.Batch.ClassificationLite/Engine/LiteEngine.cs`
- Create: `tests/DocumentIA.Batch.ClassificationLite.Tests/Fakes/FakeIngestBackend.cs`
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/LiteEngineCoreTests.cs`

**Interfaces:**
- Consumes: `LiteRepository`, `LiteConfig`, `LiteRequestFactory`, `AdaptivePollingStrategy`, `LiteResultParser`; DTOs `IngestRequest`/`IngestResponse`/`DurableStatusResponse` y `DocumentIaBackendClient` de `DocumentIA.Batch.Services`; `EnvironmentConfig` de `DocumentIA.Batch.Models`.
- Produces:
  - `interface IIngestBackend { Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct); Task<DurableStatusResponse> GetStatusAsync(string statusQueryUri, CancellationToken ct); }`
  - `class IngestBackendAdapter : IIngestBackend` — ctor `(DocumentIaBackendClient client, EnvironmentConfig environment)`; delega en `client.IngestAsync(environment.BackendUrl, environment.FunctionKey, request, ct)` y `client.GetDurableStatusAsync(statusQueryUri, environment.FunctionKey, ct)`.
  - `class LiteEngine` — ctor `(LiteRepository repository, IIngestBackend backend, LiteConfig config, Func<TimeSpan, CancellationToken, Task>? delay = null)` (delay inyectable para tests; default `Task.Delay`). Miembros de esta task:
    - `event Action? ProgressChanged` (tras cada cambio de estado persistido)
    - `void Pause()` / `void Resume()` / `bool IsPaused { get; }` (gate simple; tests en Task 11)
    - `Task ProcessDocumentsAsync(IReadOnlyList<LiteDocument> documents, CancellationToken ct)` — semáforo `config.ParallelQueries`, ciclo completo por doc.
    - `internal Task ProcessDocumentAsync(LiteDocument document, CancellationToken ct)`

Ciclo por documento (`ProcessDocumentAsync`):
1. Releer `FileInfo` (re-stat) y `File.ReadAllBytesAsync` → actualizar `FileSize`/`LastModifiedUtc`.
2. `LiteRequestFactory.Build` con `correlationId = Guid.NewGuid().ToString()`; `RequestJson = BuildRequestJsonForStorage(request, bytes.LongLength)`.
3. `backend.IngestAsync` → guardar `InstanceId`/`StatusQueryUri`, `Status=InFlight`, `UpdateDocument`, `ProgressChanged`.
4. Polling con `AdaptivePollingStrategy(config.PollingIntervalSeconds)` (delay ANTES de cada sondeo): `Completed` → aplicar resultado (`LiteResultParser`), `ResponseJson = output.GetRawText()`, `DurationMs = resultado ?? cronómetro`, `ProcessDate = resultado ?? UtcNow ISO`, `Status=Succeeded`. `Failed`/`Terminated` → `Status=Error` con `ErrorMessage`. Timeout 30 min → `Status=Error` con mensaje de timeout.
5. `Completed` sin `Output` → `Error` ("Completed sin output").
6. Excepción → `Status=Error` + `ErrorMessage`. `OperationCanceledException` → `Status=Cancelled` (sin relanzar).
7. Siempre `UpdateDocument` + `ProgressChanged` al terminar.

- [ ] **Step 1: Escribir fake y tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/Fakes/FakeIngestBackend.cs`:

```csharp
using System.Text.Json;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.ClassificationLite.Tests.Fakes;

public class FakeIngestBackend : IIngestBackend
{
    private int _ingestCalls;
    private int _statusCalls;

    public int IngestCalls => _ingestCalls;
    public int StatusCalls => _statusCalls;
    public List<IngestRequest> Requests { get; } = new();

    /// <summary>Por nombre de documento: comportamiento del ingest. Default: OK con instanceId=nombre.</summary>
    public Func<IngestRequest, IngestResponse> OnIngest { get; set; } = request => new IngestResponse
    {
        InstanceId = "inst-" + request.Documento.Name,
        StatusQueryUri = "https://backend/runtime/instances/" + request.Documento.Name
    };

    /// <summary>Por statusQueryUri: respuesta de estado. Default: Completed con output basico.</summary>
    public Func<string, DurableStatusResponse> OnStatus { get; set; } = uri => CompletedStatus("T01", "T01.02", 0.9);

    public Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _ingestCalls);
        lock (Requests) { Requests.Add(request); }
        return Task.FromResult(OnIngest(request));
    }

    public Task<DurableStatusResponse> GetStatusAsync(string statusQueryUri, CancellationToken ct)
    {
        Interlocked.Increment(ref _statusCalls);
        return Task.FromResult(OnStatus(statusQueryUri));
    }

    public static DurableStatusResponse CompletedStatus(string tdn1, string tdn2, double confidence)
    {
        var json = $$"""
            {
              "Identificacion": { "Tdn1": "{{tdn1}}", "Tdn2": "{{tdn2}}", "Paginas": 5, "FechaProceso": "2026-07-20T12:00:00Z" },
              "Resultado": { "Estado": "OK", "ConfianzaGlobal": {{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}} },
              "DetalleEjecucion": { "PaginasIncluidas": "1-5", "Seguimiento": { "DuracionTotalMs": 3000 } }
            }
            """;
        return new DurableStatusResponse
        {
            RuntimeStatus = "Completed",
            Output = JsonDocument.Parse(json).RootElement.Clone()
        };
    }

    public static DurableStatusResponse FailedStatus() => new() { RuntimeStatus = "Failed" };

    public static DurableStatusResponse RunningStatus() => new() { RuntimeStatus = "Running" };
}
```

`tests/DocumentIA.Batch.ClassificationLite.Tests/LiteEngineCoreTests.cs`:

```csharp
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Tests.Fakes;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteEngineCoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _docsDir;
    private readonly LiteRepository _repository;
    private readonly FakeIngestBackend _backend = new();

    public LiteEngineCoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-engine-tests-" + Guid.NewGuid().ToString("N"));
        _docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(_docsDir);
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private LiteEngine NewEngine(LiteConfig? config = null)
        => new(_repository, _backend, config ?? new LiteConfig(), delay: (_, _) => Task.CompletedTask);

    private LiteDocument SeedDoc(string executionId, string name)
    {
        var path = Path.Combine(_docsDir, name);
        File.WriteAllText(path, "pdf-" + name);
        var info = new FileInfo(path);
        var doc = new LiteDocument
        {
            ExecutionId = executionId,
            FileName = name,
            FullPath = path,
            FileSize = info.Length,
            LastModifiedUtc = info.LastWriteTimeUtc.ToString("O"),
            Status = LiteDocumentStatus.Pending,
            BatchNumber = 1
        };
        _repository.InsertDocuments(new[] { doc });
        return _repository.GetDocuments(executionId).Single(d => d.FileName == name);
    }

    [Fact]
    public async Task ProcessDocument_Completed_MarcaSucceededConResultado()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var doc = SeedDoc(execution.ExecutionId, "ok.pdf");

        await NewEngine().ProcessDocumentAsync(doc, CancellationToken.None);

        var stored = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.Succeeded, stored.Status);
        Assert.Equal("T01", stored.Tdn1);
        Assert.Equal("T01.02", stored.Tdn2);
        Assert.Equal(0.9, stored.Confidence);
        Assert.Equal(5, stored.Pages);
        Assert.Equal("1-5", stored.PagesIncluded);
        Assert.Equal(3000, stored.DurationMs);
        Assert.Equal("inst-ok.pdf", stored.InstanceId);
        Assert.NotNull(stored.StatusQueryUri);
        Assert.NotNull(stored.ResponseJson);
        Assert.Contains("base64 omitido", stored.RequestJson);
    }

    [Fact]
    public async Task ProcessDocument_Failed_MarcaError()
    {
        _backend.OnStatus = _ => FakeIngestBackend.FailedStatus();
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var doc = SeedDoc(execution.ExecutionId, "mal.pdf");

        await NewEngine().ProcessDocumentAsync(doc, CancellationToken.None);

        var stored = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.Error, stored.Status);
        Assert.Contains("Failed", stored.ErrorMessage);
    }

    [Fact]
    public async Task ProcessDocument_ExcepcionDeIngest_MarcaError()
    {
        _backend.OnIngest = _ => throw new InvalidOperationException("Error al invocar ingest: 500");
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var doc = SeedDoc(execution.ExecutionId, "boom.pdf");

        await NewEngine().ProcessDocumentAsync(doc, CancellationToken.None);

        var stored = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.Error, stored.Status);
        Assert.Contains("500", stored.ErrorMessage);
    }

    [Fact]
    public async Task ProcessDocument_FicheroInexistente_MarcaError()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var doc = SeedDoc(execution.ExecutionId, "borrado.pdf");
        File.Delete(doc.FullPath);

        await NewEngine().ProcessDocumentAsync(doc, CancellationToken.None);

        Assert.Equal(LiteDocumentStatus.Error, _repository.GetDocuments(execution.ExecutionId).Single().Status);
    }

    [Fact]
    public async Task ProcessDocuments_RespetaElParalelismo()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var docs = Enumerable.Range(1, 6).Select(i => SeedDoc(execution.ExecutionId, $"p{i}.pdf")).ToList();

        var concurrent = 0;
        var maxConcurrent = 0;
        var gate = new object();
        _backend.OnIngest = request =>
        {
            lock (gate)
            {
                concurrent++;
                maxConcurrent = Math.Max(maxConcurrent, concurrent);
            }
            Thread.Sleep(30);
            lock (gate) { concurrent--; }
            return new DocumentIA.Batch.Services.IngestResponse
            {
                InstanceId = "inst-" + request.Documento.Name,
                StatusQueryUri = "https://backend/" + request.Documento.Name
            };
        };

        var config = new LiteConfig { ParallelQueries = 2 };
        await NewEngine(config).ProcessDocumentsAsync(docs, CancellationToken.None);

        Assert.True(maxConcurrent <= 2, $"Concurrencia maxima {maxConcurrent}, esperada <= 2");
        Assert.All(_repository.GetDocuments(execution.ExecutionId), d => Assert.Equal(LiteDocumentStatus.Succeeded, d.Status));
    }

    [Fact]
    public async Task ProcessDocument_PollingReintentaHastaCompleted()
    {
        var calls = 0;
        _backend.OnStatus = _ => ++calls < 3
            ? FakeIngestBackend.RunningStatus()
            : FakeIngestBackend.CompletedStatus("T02", "T02.01", 0.8);
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var doc = SeedDoc(execution.ExecutionId, "lento.pdf");

        await NewEngine().ProcessDocumentAsync(doc, CancellationToken.None);

        Assert.Equal(3, calls);
        Assert.Equal(LiteDocumentStatus.Succeeded, _repository.GetDocuments(execution.ExecutionId).Single().Status);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar**

`src/DocumentIA.Batch.ClassificationLite/Engine/IIngestBackend.cs`:

```csharp
using DocumentIA.Batch.Models;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.ClassificationLite.Engine;

public interface IIngestBackend
{
    Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct);
    Task<DurableStatusResponse> GetStatusAsync(string statusQueryUri, CancellationToken ct);
}

public class IngestBackendAdapter : IIngestBackend
{
    private readonly DocumentIaBackendClient _client;
    private readonly EnvironmentConfig _environment;

    public IngestBackendAdapter(DocumentIaBackendClient client, EnvironmentConfig environment)
    {
        _client = client;
        _environment = environment;
    }

    public Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct)
        => _client.IngestAsync(_environment.BackendUrl, _environment.FunctionKey, request, ct);

    public Task<DurableStatusResponse> GetStatusAsync(string statusQueryUri, CancellationToken ct)
        => _client.GetDurableStatusAsync(statusQueryUri, _environment.FunctionKey, ct);
}
```

`src/DocumentIA.Batch.ClassificationLite/Engine/LiteEngine.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.Engine;

public class LiteEngine
{
    private readonly LiteRepository _repository;
    private readonly IIngestBackend _backend;
    private readonly LiteConfig _config;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly AdaptivePollingStrategy _polling;

    private volatile bool _paused;

    public LiteEngine(
        LiteRepository repository,
        IIngestBackend backend,
        LiteConfig config,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _repository = repository;
        _backend = backend;
        _config = config;
        _delay = delay ?? Task.Delay;
        _polling = new AdaptivePollingStrategy(config.PollingIntervalSeconds);
    }

    public event Action? ProgressChanged;

    public bool IsPaused => _paused;

    public void Pause() => _paused = true;

    public void Resume() => _paused = false;

    public async Task ProcessDocumentsAsync(IReadOnlyList<LiteDocument> documents, CancellationToken ct)
    {
        using var semaphore = new SemaphoreSlim(_config.ParallelQueries);
        var tasks = documents.Select(async document =>
        {
            await WaitWhilePausedAsync(ct);
            await semaphore.WaitAsync(ct);
            try
            {
                await ProcessDocumentAsync(document, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    internal async Task ProcessDocumentAsync(LiteDocument document, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var info = new FileInfo(document.FullPath);
            var bytes = await File.ReadAllBytesAsync(document.FullPath, ct);
            document.FileSize = info.Length;
            document.LastModifiedUtc = info.LastWriteTimeUtc.ToString("O");

            var correlationId = Guid.NewGuid().ToString();
            var request = LiteRequestFactory.Build(_config, document.FileName, bytes, correlationId);
            document.RequestJson = LiteRequestFactory.BuildRequestJsonForStorage(request, bytes.LongLength);

            var response = await _backend.IngestAsync(request, ct);
            document.InstanceId = response.InstanceId;
            document.StatusQueryUri = response.StatusQueryUri;
            document.Status = LiteDocumentStatus.InFlight;
            _repository.UpdateDocument(document);
            ProgressChanged?.Invoke();

            await PollUntilTerminalAsync(document, stopwatch, ct);
        }
        catch (OperationCanceledException)
        {
            document.Status = LiteDocumentStatus.Cancelled;
            _repository.UpdateDocument(document);
            ProgressChanged?.Invoke();
        }
        catch (Exception ex)
        {
            MarkError(document, ex.Message);
        }
    }

    internal async Task PollUntilTerminalAsync(LiteDocument document, Stopwatch stopwatch, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (_polling.IsTimedOut(stopwatch.Elapsed))
            {
                MarkError(document, $"Timeout esperando el resultado ({stopwatch.Elapsed.TotalMinutes:F0} min).");
                return;
            }

            await _delay(_polling.GetDelay(attempt), ct);

            var status = await _backend.GetStatusAsync(document.StatusQueryUri!, ct);

            if (string.Equals(status.RuntimeStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                if (!status.Output.HasValue)
                {
                    MarkError(document, "Orquestacion completada sin output.");
                    return;
                }

                var result = LiteResultParser.Parse(status.Output.Value);
                document.Tdn1 = result.Tdn1;
                document.Tdn2 = result.Tdn2;
                document.Confidence = result.Confidence;
                document.Pages = result.Pages;
                document.PagesIncluded = result.PagesIncluded;
                document.ProcessDate = result.ProcessDate ?? DateTime.UtcNow.ToString("O");
                document.DurationMs = result.DurationMs ?? stopwatch.ElapsedMilliseconds;
                document.ResponseJson = status.Output.Value.GetRawText();
                document.ErrorMessage = null;
                document.Status = LiteDocumentStatus.Succeeded;
                _repository.UpdateDocument(document);
                ProgressChanged?.Invoke();
                return;
            }

            if (string.Equals(status.RuntimeStatus, "Failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status.RuntimeStatus, "Terminated", StringComparison.OrdinalIgnoreCase))
            {
                MarkError(document, $"Orquestacion terminada con estado {status.RuntimeStatus}.");
                return;
            }
        }
    }

    private void MarkError(LiteDocument document, string message)
    {
        document.Status = LiteDocumentStatus.Error;
        document.ErrorMessage = message;
        _repository.UpdateDocument(document);
        ProgressChanged?.Invoke();
    }

    private async Task WaitWhilePausedAsync(CancellationToken ct)
    {
        while (_paused)
        {
            await _delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }
}
```

Nota: el primer sondeo llega tras 3s (primer delay adaptativo); con el `delay` inyectado en tests es inmediato.

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): nucleo del motor con semaforo, ingest y polling"
```

---

### Task 10: `LiteEngine` — lotes internos y reintentos batch-completion

**Files:**
- Modify: `src/DocumentIA.Batch.ClassificationLite/Engine/LiteEngine.cs` (añadir método `RunAsync` y auxiliares)
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/LiteEngineBatchTests.cs`

**Interfaces:**
- Consumes: Task 9 (`ProcessDocumentsAsync`), `LiteRepository.GetBatchNumbers/GetPendingBatch/GetErrorsInBatch/UpdateExecutionStatus`.
- Produces en `LiteEngine`:
  - `Task RunAsync(string executionId, CancellationToken ct)`

Algoritmo:
```text
para cada BatchNumber (asc) devuelto por GetBatchNumbers(executionId):
    procesar GetPendingBatch(executionId, batch)
    para retry en 1..MaxRetries:
        errores = GetErrorsInBatch(executionId, batch)
        si vacío -> salir del bucle
        errores.RetryCount = retry, Status = Pending, UpdateDocument
        procesar esos documentos
    restantes = GetErrorsInBatch(executionId, batch)
    restantes -> Status = DefinitiveError, UpdateDocument
al terminar: si no hay cancelación -> UpdateExecutionStatus(executionId, Completed, setCompletedAt: true)
si cancelado -> UpdateExecutionStatus(executionId, Cancelled, setCompletedAt: true)
```
Los lotes se recalculan con `GetBatchNumbers` UNA vez al principio (los lotes nuevos no aparecen a mitad de ejecución).

- [ ] **Step 1: Escribir tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/LiteEngineBatchTests.cs`:

```csharp
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Tests.Fakes;
using DocumentIA.Batch.Services;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteEngineBatchTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _docsDir;
    private readonly LiteRepository _repository;
    private readonly FakeIngestBackend _backend = new();

    public LiteEngineBatchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-batch-tests-" + Guid.NewGuid().ToString("N"));
        _docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(_docsDir);
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private LiteEngine NewEngine(LiteConfig? config = null)
        => new(_repository, _backend, config ?? new LiteConfig(), delay: (_, _) => Task.CompletedTask);

    private void SeedDocs(string executionId, int count, int batchNumber)
    {
        var docs = new List<LiteDocument>();
        for (var i = 1; i <= count; i++)
        {
            var name = $"b{batchNumber}-d{i}.pdf";
            var path = Path.Combine(_docsDir, name);
            File.WriteAllText(path, "pdf");
            var info = new FileInfo(path);
            docs.Add(new LiteDocument
            {
                ExecutionId = executionId,
                FileName = name,
                FullPath = path,
                FileSize = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc.ToString("O"),
                Status = LiteDocumentStatus.Pending,
                BatchNumber = batchNumber
            });
        }

        _repository.InsertDocuments(docs);
    }

    [Fact]
    public async Task RunAsync_ProcesaTodosLosLotes_YMarcaEjecucionCompleted()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDocs(execution.ExecutionId, 3, batchNumber: 1);
        SeedDocs(execution.ExecutionId, 2, batchNumber: 2);

        await NewEngine().RunAsync(execution.ExecutionId, CancellationToken.None);

        var docs = _repository.GetDocuments(execution.ExecutionId);
        Assert.Equal(5, docs.Count);
        Assert.All(docs, d => Assert.Equal(LiteDocumentStatus.Succeeded, d.Status));
        Assert.Null(_repository.GetIncompleteExecution());
    }

    [Fact]
    public async Task RunAsync_ReintentaErrores_HastaExito()
    {
        // "malo.pdf" falla en el ingest las 2 primeras veces y funciona a la tercera.
        var failures = 0;
        _backend.OnIngest = request =>
        {
            if (request.Documento.Name.StartsWith("b1-d2") && failures < 2)
            {
                failures++;
                throw new InvalidOperationException("fallo transitorio");
            }

            return new IngestResponse
            {
                InstanceId = "inst",
                StatusQueryUri = "https://backend/" + request.Documento.Name
            };
        };

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDocs(execution.ExecutionId, 3, batchNumber: 1);

        await NewEngine(new LiteConfig { MaxRetries = 3 }).RunAsync(execution.ExecutionId, CancellationToken.None);

        var docs = _repository.GetDocuments(execution.ExecutionId);
        Assert.All(docs, d => Assert.Equal(LiteDocumentStatus.Succeeded, d.Status));
        var retried = docs.Single(d => d.FileName.StartsWith("b1-d2"));
        Assert.Equal(2, retried.RetryCount);
    }

    [Fact]
    public async Task RunAsync_AgotaReintentos_MarcaDefinitiveError()
    {
        _backend.OnIngest = request => request.Documento.Name.StartsWith("b1-d1")
            ? throw new InvalidOperationException("fallo permanente")
            : new IngestResponse { InstanceId = "i", StatusQueryUri = "https://backend/" + request.Documento.Name };

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDocs(execution.ExecutionId, 2, batchNumber: 1);

        await NewEngine(new LiteConfig { MaxRetries = 3 }).RunAsync(execution.ExecutionId, CancellationToken.None);

        var docs = _repository.GetDocuments(execution.ExecutionId);
        var failed = docs.Single(d => d.FileName.StartsWith("b1-d1"));
        Assert.Equal(LiteDocumentStatus.DefinitiveError, failed.Status);
        Assert.Equal(3, failed.RetryCount);
        Assert.Contains("fallo permanente", failed.ErrorMessage);
        Assert.Equal(LiteDocumentStatus.Succeeded, docs.Single(d => d.FileName.StartsWith("b1-d2")).Status);
    }

    [Fact]
    public async Task RunAsync_MaxRetriesCero_NoReintenta()
    {
        _backend.OnIngest = _ => throw new InvalidOperationException("fallo");
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDocs(execution.ExecutionId, 1, batchNumber: 1);

        await NewEngine(new LiteConfig { MaxRetries = 0 }).RunAsync(execution.ExecutionId, CancellationToken.None);

        var doc = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.DefinitiveError, doc.Status);
        Assert.Equal(0, doc.RetryCount);
        Assert.Equal(1, _backend.IngestCalls);
    }

    [Fact]
    public async Task RunAsync_Cancelado_MarcaEjecucionCancelled()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDocs(execution.ExecutionId, 2, batchNumber: 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await NewEngine().RunAsync(execution.ExecutionId, cts.Token);

        var stored = _repository.GetIncompleteExecution();
        Assert.Null(stored);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación (`RunAsync` no existe).

- [ ] **Step 3: Implementar**

Añadir a `LiteEngine` (justo después de `ProcessDocumentsAsync`):

```csharp
    public async Task RunAsync(string executionId, CancellationToken ct)
    {
        try
        {
            foreach (var batchNumber in _repository.GetBatchNumbers(executionId))
            {
                ct.ThrowIfCancellationRequested();

                var pending = _repository.GetPendingBatch(executionId, batchNumber);
                if (pending.Count > 0)
                {
                    await ProcessDocumentsAsync(pending, ct);
                }

                for (var retry = 1; retry <= _config.MaxRetries; retry++)
                {
                    ct.ThrowIfCancellationRequested();

                    var errors = _repository.GetErrorsInBatch(executionId, batchNumber);
                    if (errors.Count == 0)
                    {
                        break;
                    }

                    foreach (var error in errors)
                    {
                        error.RetryCount = retry;
                        error.Status = LiteDocumentStatus.Pending;
                        _repository.UpdateDocument(error);
                    }

                    await ProcessDocumentsAsync(errors, ct);
                }

                foreach (var definitive in _repository.GetErrorsInBatch(executionId, batchNumber))
                {
                    definitive.Status = LiteDocumentStatus.DefinitiveError;
                    _repository.UpdateDocument(definitive);
                }

                ProgressChanged?.Invoke();
            }

            _repository.UpdateExecutionStatus(executionId, LiteExecutionStatus.Completed, setCompletedAt: true);
        }
        catch (OperationCanceledException)
        {
            _repository.UpdateExecutionStatus(executionId, LiteExecutionStatus.Cancelled, setCompletedAt: true);
        }
        finally
        {
            ProgressChanged?.Invoke();
        }
    }
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): lotes internos y reintentos batch-completion"
```

---

### Task 11: `LiteEngine` — re-enganche de documentos en vuelo y auto-pausa por 401

**Files:**
- Modify: `src/DocumentIA.Batch.ClassificationLite/Engine/LiteEngine.cs`
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/LiteEngineRecoveryTests.cs`

**Interfaces:**
- Consumes: Tasks 9-10.
- Produces en `LiteEngine`:
  - `Task ReattachInFlightAsync(string executionId, CancellationToken ct)` — para cada doc `InFlight` con `StatusQueryUri`, retoma el polling SIN reenviar el documento. Docs `InFlight` sin `StatusQueryUri` → `Pending`.
  - `event Action<string>? AutoPaused` — se dispara al auto-pausar (mensaje para la UI).
  - Auto-pausa por 401: contador de errores consecutivos cuyo mensaje contenga `"401"`; al llegar a 5, `Pause()` + `AutoPaused?.Invoke(...)`. Cualquier documento que termine en `Succeeded` resetea el contador.

- [ ] **Step 1: Escribir tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/LiteEngineRecoveryTests.cs`:

```csharp
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Tests.Fakes;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteEngineRecoveryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _docsDir;
    private readonly LiteRepository _repository;
    private readonly FakeIngestBackend _backend = new();

    public LiteEngineRecoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-recovery-tests-" + Guid.NewGuid().ToString("N"));
        _docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(_docsDir);
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private LiteEngine NewEngine(LiteConfig? config = null)
        => new(_repository, _backend, config ?? new LiteConfig(), delay: (_, _) => Task.CompletedTask);

    private LiteDocument SeedDoc(string executionId, string name, string status, string? statusUri)
    {
        var path = Path.Combine(_docsDir, name);
        File.WriteAllText(path, "pdf");
        var info = new FileInfo(path);
        _repository.InsertDocuments(new[]
        {
            new LiteDocument
            {
                ExecutionId = executionId,
                FileName = name,
                FullPath = path,
                FileSize = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc.ToString("O"),
                Status = status,
                StatusQueryUri = statusUri,
                InstanceId = statusUri is null ? null : "inst",
                BatchNumber = 1
            }
        });
        return _repository.GetDocuments(executionId).Single(d => d.FileName == name);
    }

    [Fact]
    public async Task ReattachInFlight_RetomaPollingSinReenviar()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDoc(execution.ExecutionId, "envuelo.pdf", LiteDocumentStatus.InFlight, "https://backend/inst-1");

        await NewEngine().ReattachInFlightAsync(execution.ExecutionId, CancellationToken.None);

        Assert.Equal(0, _backend.IngestCalls);
        Assert.True(_backend.StatusCalls > 0);
        var doc = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.Succeeded, doc.Status);
        Assert.Equal("T01", doc.Tdn1);
    }

    [Fact]
    public async Task ReattachInFlight_SinStatusUri_VuelveAPending()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDoc(execution.ExecutionId, "huerfano.pdf", LiteDocumentStatus.InFlight, statusUri: null);

        await NewEngine().ReattachInFlightAsync(execution.ExecutionId, CancellationToken.None);

        Assert.Equal(0, _backend.StatusCalls);
        Assert.Equal(LiteDocumentStatus.Pending, _repository.GetDocuments(execution.ExecutionId).Single().Status);
    }

    [Fact]
    public async Task Errores401Consecutivos_AutoPausanElMotor()
    {
        _backend.OnIngest = _ => throw new InvalidOperationException(
            "Error 401: La Function Key no es valida o ha expirado.");

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var docs = new List<LiteDocument>();
        for (var i = 1; i <= 5; i++)
        {
            docs.Add(SeedDoc(execution.ExecutionId, $"k{i}.pdf", LiteDocumentStatus.Pending, null));
        }

        var engine = NewEngine(new LiteConfig { ParallelQueries = 1 });
        string? aviso = null;
        engine.AutoPaused += message => aviso = message;

        foreach (var doc in docs)
        {
            await engine.ProcessDocumentAsync(doc, CancellationToken.None);
        }

        Assert.True(engine.IsPaused);
        Assert.NotNull(aviso);
        Assert.Contains("401", aviso);
    }

    [Fact]
    public async Task UnExitoResetea_ElContadorDe401()
    {
        var calls = 0;
        _backend.OnIngest = request =>
        {
            calls++;
            // Falla con 401 en las llamadas 1-4, funciona en la 5, vuelve a fallar despues.
            if (calls == 5)
            {
                return new DocumentIA.Batch.Services.IngestResponse
                {
                    InstanceId = "i",
                    StatusQueryUri = "https://backend/ok"
                };
            }

            throw new InvalidOperationException("Error 401: La Function Key no es valida.");
        };

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var docs = new List<LiteDocument>();
        for (var i = 1; i <= 6; i++)
        {
            docs.Add(SeedDoc(execution.ExecutionId, $"m{i}.pdf", LiteDocumentStatus.Pending, null));
        }

        var engine = NewEngine(new LiteConfig { ParallelQueries = 1 });
        foreach (var doc in docs)
        {
            await engine.ProcessDocumentAsync(doc, CancellationToken.None);
        }

        Assert.False(engine.IsPaused);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación (`ReattachInFlightAsync`/`AutoPaused` no existen).

- [ ] **Step 3: Implementar**

En `LiteEngine`, añadir el campo y el evento junto a `_paused`:

```csharp
    private const int ConsecutiveUnauthorizedLimit = 5;

    private int _consecutiveUnauthorized;

    public event Action<string>? AutoPaused;
```

Añadir el método de re-enganche (tras `RunAsync`):

```csharp
    public async Task ReattachInFlightAsync(string executionId, CancellationToken ct)
    {
        var inFlight = _repository.GetInFlight(executionId);
        var reattachable = new List<LiteDocument>();

        foreach (var document in inFlight)
        {
            if (string.IsNullOrWhiteSpace(document.StatusQueryUri))
            {
                document.Status = LiteDocumentStatus.Pending;
                _repository.UpdateDocument(document);
                continue;
            }

            reattachable.Add(document);
        }

        if (reattachable.Count == 0)
        {
            ProgressChanged?.Invoke();
            return;
        }

        using var semaphore = new SemaphoreSlim(_config.ParallelQueries);
        await Task.WhenAll(reattachable.Select(async document =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                await PollUntilTerminalAsync(document, stopwatch, ct);
            }
            catch (OperationCanceledException)
            {
                document.Status = LiteDocumentStatus.Cancelled;
                _repository.UpdateDocument(document);
            }
            catch (Exception ex)
            {
                MarkError(document, ex.Message);
            }
            finally
            {
                semaphore.Release();
            }
        }));

        ProgressChanged?.Invoke();
    }
```

Modificar `MarkError` para contar 401 consecutivos:

```csharp
    private void MarkError(LiteDocument document, string message)
    {
        document.Status = LiteDocumentStatus.Error;
        document.ErrorMessage = message;
        _repository.UpdateDocument(document);

        if (message.Contains("401", StringComparison.Ordinal))
        {
            var consecutive = Interlocked.Increment(ref _consecutiveUnauthorized);
            if (consecutive >= ConsecutiveUnauthorizedLimit && !_paused)
            {
                Pause();
                AutoPaused?.Invoke(
                    $"Ejecucion pausada automaticamente tras {consecutive} errores 401 consecutivos. " +
                    "Revisa la Function Key del entorno en Configuracion y pulsa Reanudar.");
            }
        }

        ProgressChanged?.Invoke();
    }
```

En `PollUntilTerminalAsync`, al marcar `Succeeded` (justo antes de `_repository.UpdateDocument(document)` del bloque Completed), resetear el contador:

```csharp
                Interlocked.Exchange(ref _consecutiveUnauthorized, 0);
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): re-enganche de documentos en vuelo y auto-pausa por 401"
```

---

### Task 12: `LiteExportService` — exportación CSV y Excel

**Files:**
- Create: `src/DocumentIA.Batch.ClassificationLite/Services/LiteExportService.cs`
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/LiteExportServiceTests.cs`

**Interfaces:**
- Consumes: `LiteDocument` (Task 2).
- Produces: `LiteExportService` (estático) con:
  - `static readonly string[] Headers` = `{ "FileName", "Status", "PagesIncluded", "Pages", "TDN1", "TDN2", "Confidence", "ProcessDate", "TotalDurationMs" }`
  - `static void ExportCsv(IEnumerable<LiteDocument> documents, string path)` — separador `;`, BOM UTF-8, valores con `;`/`"`/salto de línea entre comillas dobles duplicando comillas internas.
  - `static void ExportExcel(IEnumerable<LiteDocument> documents, string path)` — paquete OOXML mínimo (ZIP con `[Content_Types].xml`, `_rels/.rels`, `xl/workbook.xml`, `xl/_rels/workbook.xml.rels`, `xl/worksheets/sheet1.xml`), celdas inline strings.
  - `static string[] ToRow(LiteDocument document)` — `Confidence` con `InvariantCulture` a 4 decimales, nulos como cadena vacía.

- [ ] **Step 1: Escribir tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/LiteExportServiceTests.cs`:

```csharp
using System.IO.Compression;
using System.Text;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteExportServiceTests : IDisposable
{
    private readonly string _tempDir;

    public LiteExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-export-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static LiteDocument SampleDoc() => new()
    {
        FileName = "informe.pdf",
        Status = LiteDocumentStatus.Succeeded,
        PagesIncluded = "1-5",
        Pages = 8,
        Tdn1 = "T01",
        Tdn2 = "T01.02",
        Confidence = 0.9123,
        ProcessDate = "2026-07-20T12:00:00Z",
        DurationMs = 4321
    };

    [Fact]
    public void ToRow_MapeaLasNueveColumnas()
    {
        var row = LiteExportService.ToRow(SampleDoc());

        Assert.Equal(9, row.Length);
        Assert.Equal(new[] { "informe.pdf", "Succeeded", "1-5", "8", "T01", "T01.02", "0,9123".Replace(',', '.'), "2026-07-20T12:00:00Z", "4321" }, row);
    }

    [Fact]
    public void ToRow_ConNulos_DevuelveCadenasVacias()
    {
        var row = LiteExportService.ToRow(new LiteDocument { FileName = "x.pdf", Status = LiteDocumentStatus.Pending });

        Assert.Equal("x.pdf", row[0]);
        Assert.Equal("Pending", row[1]);
        Assert.Equal(string.Empty, row[2]);
        Assert.Equal(string.Empty, row[3]);
        Assert.Equal(string.Empty, row[6]);
        Assert.Equal(string.Empty, row[8]);
    }

    [Fact]
    public void ExportCsv_EscribeCabeceraYFilas_ConBom()
    {
        var path = Path.Combine(_tempDir, "salida.csv");

        LiteExportService.ExportCsv(new[] { SampleDoc() }, path);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.Equal("FileName;Status;PagesIncluded;Pages;TDN1;TDN2;Confidence;ProcessDate;TotalDurationMs", lines[0]);
        Assert.Contains("informe.pdf;Succeeded;1-5;8;T01;T01.02;0.9123;", lines[1]);
    }

    [Fact]
    public void ExportCsv_EntrecomillaValoresConSeparador()
    {
        var path = Path.Combine(_tempDir, "comillas.csv");
        var doc = SampleDoc();
        doc.FileName = "raro;con \"comillas\".pdf";

        LiteExportService.ExportCsv(new[] { doc }, path);

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.StartsWith("\"raro;con \"\"comillas\"\".pdf\";", lines[1]);
    }

    [Fact]
    public void ExportExcel_GeneraPaqueteOoxmlValido()
    {
        var path = Path.Combine(_tempDir, "salida.xlsx");

        LiteExportService.ExportExcel(new[] { SampleDoc() }, path);

        using var archive = ZipFile.OpenRead(path);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("_rels/.rels"));
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/_rels/workbook.xml.rels"));
        var sheet = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(sheet);
        using var reader = new StreamReader(sheet!.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("FileName", xml);
        Assert.Contains("informe.pdf", xml);
        Assert.Contains("T01.02", xml);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar**

`src/DocumentIA.Batch.ClassificationLite/Services/LiteExportService.cs`:

```csharp
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.Services;

public static class LiteExportService
{
    public static readonly string[] Headers =
    {
        "FileName", "Status", "PagesIncluded", "Pages", "TDN1", "TDN2", "Confidence", "ProcessDate", "TotalDurationMs"
    };

    public static string[] ToRow(LiteDocument document) => new[]
    {
        document.FileName,
        document.Status,
        document.PagesIncluded ?? string.Empty,
        document.Pages?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        document.Tdn1 ?? string.Empty,
        document.Tdn2 ?? string.Empty,
        document.Confidence?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
        document.ProcessDate ?? string.Empty,
        document.DurationMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
    };

    public static void ExportCsv(IEnumerable<LiteDocument> documents, string path)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(string.Join(';', Headers));

        foreach (var document in documents)
        {
            writer.WriteLine(string.Join(';', ToRow(document).Select(EscapeCsv)));
        }
    }

    public static void ExportExcel(IEnumerable<LiteDocument> documents, string path)
    {
        var rows = new List<string[]> { Headers };
        rows.AddRange(documents.Select(ToRow));

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);

        WriteEntry(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

        WriteEntry(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="Resultados" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);

        WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);

        WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildSheetXml(rows));
    }

    private static string BuildSheetXml(IReadOnlyList<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        builder.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            builder.Append($"<row r=\"{rowIndex + 1}\">");
            var cells = rows[rowIndex];
            for (var columnIndex = 0; columnIndex < cells.Length; columnIndex++)
            {
                var reference = $"{ColumnName(columnIndex)}{rowIndex + 1}";
                builder.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
                builder.Append(SecurityElement.Escape(cells[columnIndex]) ?? string.Empty);
                builder.Append("</t></is></c>");
            }

            builder.Append("</row>");
        }

        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        var current = index;
        do
        {
            name = (char)('A' + (current % 26)) + name;
            current = (current / 26) - 1;
        }
        while (current >= 0);

        return name;
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(';') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): exportacion a CSV y Excel"
```

---

### Task 13: `RelayCommand` + `LiteMainViewModel` (estado observable y contadores)

**Files:**
- Create: `src/DocumentIA.Batch.ClassificationLite/ViewModels/RelayCommand.cs`
- Create: `src/DocumentIA.Batch.ClassificationLite/ViewModels/LiteDocumentRow.cs`
- Create: `src/DocumentIA.Batch.ClassificationLite/ViewModels/LiteMainViewModel.cs`
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/LiteMainViewModelTests.cs`

**Interfaces:**
- Consumes: `LiteRepository`, `LiteConfigService`, `LiteConfig`, `LiteCounters`, `LiteDocument`, `LiteExportService`.
- Produces:
  - `RelayCommand : ICommand` — ctor `(Action<object?> execute, Func<object?, bool>? canExecute = null)` + `void RaiseCanExecuteChanged()`.
  - `LiteDocumentRow : INotifyPropertyChanged` — proyección de `LiteDocument` para el grid: `long Id`, `string FileName`, `string Status`, `string PagesIncluded`, `string Pages`, `string Tdn1`, `string Tdn2`, `string Confidence`, `string ProcessDate`, `string TotalDurationMs`, más `string? RequestJson`, `string? ResponseJson`, `string? ErrorMessage` (para el detalle). `static LiteDocumentRow From(LiteDocument document)`.
  - `LiteMainViewModel : INotifyPropertyChanged` — ctor `(LiteRepository repository, LiteConfigService configService)`. Miembros de esta task:
    - `LiteConfig Config { get; }`
    - `ObservableCollection<LiteDocumentRow> Rows { get; }`
    - `ICollectionView RowsView { get; }` (filtro por `FilterText` sobre `FileName` y por `StatusFilter`)
    - `string FilterText { get; set; }`, `string StatusFilter { get; set; }` (valor "Todos" = sin filtro), `IReadOnlyList<string> StatusFilterOptions`
    - Contadores: `int TotalFound`, `int PendingCount`, `int InFlightCount`, `int SucceededCount`, `int DefinitiveErrorCount`, `int SkippedCount`
    - `string? CurrentExecutionId { get; set; }`
    - `void ReloadRows()` — recarga `Rows` desde `GetDocuments(CurrentExecutionId)`
    - `void RefreshCounters()` — desde `GetCounters(CurrentExecutionId)`
    - `void ExportCsv(string path)` / `void ExportExcel(string path)` — exportan las filas VISIBLES si hay filtro activo, todas si no.

- [ ] **Step 1: Escribir tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/LiteMainViewModelTests.cs`:

```csharp
using System.Windows.Data;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using DocumentIA.Batch.ClassificationLite.ViewModels;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteMainViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LiteRepository _repository;
    private readonly LiteMainViewModel _viewModel;
    private readonly string _executionId;

    public LiteMainViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-vm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
        _viewModel = new LiteMainViewModel(_repository, new LiteConfigService(Path.Combine(_tempDir, "config.json")));

        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _executionId = execution.ExecutionId;
        _repository.InsertDocuments(new[]
        {
            new LiteDocument { ExecutionId = _executionId, FileName = "alfa.pdf", FullPath = @"c:\docs\alfa.pdf", FileSize = 1, LastModifiedUtc = "x", Status = LiteDocumentStatus.Succeeded, Tdn1 = "T01", Tdn2 = "T01.02", Confidence = 0.9, Pages = 5, PagesIncluded = "1-5", ProcessDate = "2026-07-20", DurationMs = 1000 },
            new LiteDocument { ExecutionId = _executionId, FileName = "beta.pdf", FullPath = @"c:\docs\beta.pdf", FileSize = 1, LastModifiedUtc = "x", Status = LiteDocumentStatus.Pending },
            new LiteDocument { ExecutionId = _executionId, FileName = "gamma.pdf", FullPath = @"c:\docs\gamma.pdf", FileSize = 1, LastModifiedUtc = "x", Status = LiteDocumentStatus.DefinitiveError, ErrorMessage = "kaput" },
            new LiteDocument { ExecutionId = _executionId, FileName = "delta.pdf", FullPath = @"c:\docs\delta.pdf", FileSize = 1, LastModifiedUtc = "x", Status = LiteDocumentStatus.SkippedHistory, Tdn1 = "T09" }
        });
        _viewModel.CurrentExecutionId = _executionId;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void ReloadRows_ProyectaLosDocumentos()
    {
        _viewModel.ReloadRows();

        Assert.Equal(4, _viewModel.Rows.Count);
        var alfa = _viewModel.Rows.Single(r => r.FileName == "alfa.pdf");
        Assert.Equal("Succeeded", alfa.Status);
        Assert.Equal("T01", alfa.Tdn1);
        Assert.Equal("1-5", alfa.PagesIncluded);
        Assert.Equal("5", alfa.Pages);
        Assert.Equal("1000", alfa.TotalDurationMs);
    }

    [Fact]
    public void RefreshCounters_CalculaLosSeisIndicadores()
    {
        _viewModel.RefreshCounters();

        Assert.Equal(4, _viewModel.TotalFound);
        Assert.Equal(1, _viewModel.PendingCount);
        Assert.Equal(0, _viewModel.InFlightCount);
        Assert.Equal(1, _viewModel.SucceededCount);
        Assert.Equal(1, _viewModel.DefinitiveErrorCount);
        Assert.Equal(1, _viewModel.SkippedCount);
    }

    [Fact]
    public void FilterText_FiltraPorNombre()
    {
        _viewModel.ReloadRows();
        _viewModel.FilterText = "amm";

        var visible = _viewModel.RowsView.Cast<LiteDocumentRow>().ToList();

        Assert.Single(visible);
        Assert.Equal("gamma.pdf", visible[0].FileName);
    }

    [Fact]
    public void StatusFilter_FiltraPorEstado_YTodosLoQuita()
    {
        _viewModel.ReloadRows();
        _viewModel.StatusFilter = LiteDocumentStatus.Pending;
        Assert.Single(_viewModel.RowsView.Cast<LiteDocumentRow>());

        _viewModel.StatusFilter = "Todos";
        Assert.Equal(4, _viewModel.RowsView.Cast<LiteDocumentRow>().Count());
    }

    [Fact]
    public void ExportCsv_ConFiltroActivo_ExportaSoloLoVisible()
    {
        _viewModel.ReloadRows();
        _viewModel.StatusFilter = LiteDocumentStatus.Succeeded;
        var path = Path.Combine(_tempDir, "export.csv");

        _viewModel.ExportCsv(path);

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("alfa.pdf", lines[1]);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar**

`src/DocumentIA.Batch.ClassificationLite/ViewModels/RelayCommand.cs`:

```csharp
using System.Windows.Input;

namespace DocumentIA.Batch.ClassificationLite.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

`src/DocumentIA.Batch.ClassificationLite/ViewModels/LiteDocumentRow.cs`:

```csharp
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.ViewModels;

public class LiteDocumentRow : INotifyPropertyChanged
{
    private string _status = string.Empty;

    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public string PagesIncluded { get; set; } = string.Empty;
    public string Pages { get; set; } = string.Empty;
    public string Tdn1 { get; set; } = string.Empty;
    public string Tdn2 { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public string ProcessDate { get; set; } = string.Empty;
    public string TotalDurationMs { get; set; } = string.Empty;

    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }
    public string? ErrorMessage { get; set; }

    public static LiteDocumentRow From(LiteDocument document) => new()
    {
        Id = document.Id,
        FileName = document.FileName,
        Status = document.Status,
        PagesIncluded = document.PagesIncluded ?? string.Empty,
        Pages = document.Pages?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        Tdn1 = document.Tdn1 ?? string.Empty,
        Tdn2 = document.Tdn2 ?? string.Empty,
        Confidence = document.Confidence?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
        ProcessDate = document.ProcessDate ?? string.Empty,
        TotalDurationMs = document.DurationMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        RequestJson = document.RequestJson,
        ResponseJson = document.ResponseJson,
        ErrorMessage = document.ErrorMessage
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

`src/DocumentIA.Batch.ClassificationLite/ViewModels/LiteMainViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;

namespace DocumentIA.Batch.ClassificationLite.ViewModels;

public partial class LiteMainViewModel : INotifyPropertyChanged
{
    private readonly LiteRepository _repository;
    private readonly LiteConfigService _configService;

    private string _filterText = string.Empty;
    private string _statusFilter = "Todos";
    private int _totalFound;
    private int _pendingCount;
    private int _inFlightCount;
    private int _succeededCount;
    private int _definitiveErrorCount;
    private int _skippedCount;

    public LiteMainViewModel(LiteRepository repository, LiteConfigService configService)
    {
        _repository = repository;
        _configService = configService;
        Config = _configService.Load();
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = FilterRow;
    }

    public LiteConfig Config { get; private set; }

    public ObservableCollection<LiteDocumentRow> Rows { get; } = new();

    public ICollectionView RowsView { get; }

    public IReadOnlyList<string> StatusFilterOptions { get; } = new[]
    {
        "Todos",
        LiteDocumentStatus.Pending,
        LiteDocumentStatus.InFlight,
        LiteDocumentStatus.Succeeded,
        LiteDocumentStatus.Error,
        LiteDocumentStatus.DefinitiveError,
        LiteDocumentStatus.SkippedHistory,
        LiteDocumentStatus.Cancelled
    };

    public string? CurrentExecutionId { get; set; }

    public string FilterText
    {
        get => _filterText;
        set { _filterText = value ?? string.Empty; OnPropertyChanged(); RowsView.Refresh(); }
    }

    public string StatusFilter
    {
        get => _statusFilter;
        set { _statusFilter = value ?? "Todos"; OnPropertyChanged(); RowsView.Refresh(); }
    }

    public int TotalFound { get => _totalFound; private set { _totalFound = value; OnPropertyChanged(); } }
    public int PendingCount { get => _pendingCount; private set { _pendingCount = value; OnPropertyChanged(); } }
    public int InFlightCount { get => _inFlightCount; private set { _inFlightCount = value; OnPropertyChanged(); } }
    public int SucceededCount { get => _succeededCount; private set { _succeededCount = value; OnPropertyChanged(); } }
    public int DefinitiveErrorCount { get => _definitiveErrorCount; private set { _definitiveErrorCount = value; OnPropertyChanged(); } }
    public int SkippedCount { get => _skippedCount; private set { _skippedCount = value; OnPropertyChanged(); } }

    public void ReloadRows()
    {
        if (string.IsNullOrWhiteSpace(CurrentExecutionId))
        {
            return;
        }

        Rows.Clear();
        foreach (var document in _repository.GetDocuments(CurrentExecutionId))
        {
            Rows.Add(LiteDocumentRow.From(document));
        }

        RowsView.Refresh();
    }

    public void RefreshCounters()
    {
        if (string.IsNullOrWhiteSpace(CurrentExecutionId))
        {
            return;
        }

        var counters = _repository.GetCounters(CurrentExecutionId);
        TotalFound = counters.Total;
        PendingCount = counters.Pending;
        InFlightCount = counters.InFlight;
        SucceededCount = counters.Succeeded;
        DefinitiveErrorCount = counters.DefinitiveError;
        SkippedCount = counters.SkippedHistory;
    }

    public void ExportCsv(string path) => LiteExportService.ExportCsv(GetExportDocuments(), path);

    public void ExportExcel(string path) => LiteExportService.ExportExcel(GetExportDocuments(), path);

    private List<LiteDocument> GetExportDocuments()
    {
        if (string.IsNullOrWhiteSpace(CurrentExecutionId))
        {
            return new List<LiteDocument>();
        }

        var all = _repository.GetDocuments(CurrentExecutionId);
        var hasFilter = !string.IsNullOrWhiteSpace(FilterText)
            || !string.Equals(StatusFilter, "Todos", StringComparison.OrdinalIgnoreCase);

        if (!hasFilter)
        {
            return all;
        }

        var visibleIds = RowsView.Cast<LiteDocumentRow>().Select(r => r.Id).ToHashSet();
        return all.Where(d => visibleIds.Contains(d.Id)).ToList();
    }

    private bool FilterRow(object item)
    {
        if (item is not LiteDocumentRow row)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(FilterText)
            && row.FileName.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return string.Equals(StatusFilter, "Todos", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Status, StatusFilter, StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

Nota: la clase es `partial` porque la Task 14 añade la orquestación en un segundo fichero.

Los tests que usan `CollectionViewSource` necesitan STA. Añadir al proyecto de tests el fichero `tests/DocumentIA.Batch.ClassificationLite.Tests/xunit.runner.json` y referenciarlo:

`tests/DocumentIA.Batch.ClassificationLite.Tests/xunit.runner.json`:

```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "appDomain": "denied"
}
```

y en el `.csproj` de tests, dentro del primer `ItemGroup`:

```xml
    <None Update="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />
```

Si algún test de ViewModel falla por hilo no-STA, decorar la clase de test con `[Collection("STA")]` no basta: usar en su lugar ejecución explícita en un hilo STA dentro del test problemático:

```csharp
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
```

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): viewmodel principal con filtros, contadores y exportacion"
```

---

### Task 14: `LiteMainViewModel` — orquestación (escanear, ejecutar, pausar, cancelar, recuperar)

**Files:**
- Create: `src/DocumentIA.Batch.ClassificationLite/ViewModels/LiteMainViewModel.Orchestration.cs`
- Create: `src/DocumentIA.Batch.ClassificationLite/Services/SleepBlocker.cs`
- Test: `tests/DocumentIA.Batch.ClassificationLite.Tests/LiteMainViewModelOrchestrationTests.cs`

**Interfaces:**
- Consumes: Task 13 (`LiteMainViewModel` partial), `FolderScanner`, `LiteEngine`, `IIngestBackend`, `IngestBackendAdapter`, `DocumentIaBackendClient`, `LiteExecutionStatus`.
- Produces en `LiteMainViewModel` (segundo fichero partial):
  - `IReadOnlyList<string> SelectedPaths { get; set; }`, `bool IncludeSubfolders { get; set; }`, `bool IsRunning { get; }`, `bool IsPaused { get; }`, `string StatusMessage { get; }`
  - `Func<IIngestBackend>? BackendFactory { get; set; }` — inyectable en tests; por defecto crea `IngestBackendAdapter` con el entorno seleccionado.
  - `Task StartAsync(CancellationToken externalToken = default)` — crea ejecución, escanea, `RunAsync`, refresca UI cada segundo.
  - `void PauseExecution()`, `void ResumeExecution()`, `void CancelExecution()`
  - `LiteExecution? GetPendingRecovery()` — expone `repository.GetIncompleteExecution()`
  - `Task ResumeExecutionAsync(LiteExecution execution)` — re-engancha en vuelo y continúa
  - `void DiscardExecution(LiteExecution execution)` — marca `Aborted`
  - `SleepBlocker` estático: `static void PreventSleep()` / `static void AllowSleep()` (P/Invoke `SetThreadExecutionState`).

- [ ] **Step 1: Escribir tests que fallan**

`tests/DocumentIA.Batch.ClassificationLite.Tests/LiteMainViewModelOrchestrationTests.cs`:

```csharp
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using DocumentIA.Batch.ClassificationLite.Tests.Fakes;
using DocumentIA.Batch.ClassificationLite.ViewModels;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteMainViewModelOrchestrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _docsDir;
    private readonly LiteRepository _repository;
    private readonly FakeIngestBackend _backend = new();
    private readonly LiteMainViewModel _viewModel;

    public LiteMainViewModelOrchestrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-orch-tests-" + Guid.NewGuid().ToString("N"));
        _docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(_docsDir);
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
        _viewModel = new LiteMainViewModel(_repository, new LiteConfigService(Path.Combine(_tempDir, "config.json")))
        {
            BackendFactory = () => _backend
        };
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private void CreatePdfs(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            File.WriteAllText(Path.Combine(_docsDir, $"doc{i}.pdf"), "pdf");
        }
    }

    [Fact]
    public async Task StartAsync_EscaneaYProcesaTodo()
    {
        CreatePdfs(3);
        _viewModel.SelectedPaths = new[] { _docsDir };

        await _viewModel.StartAsync();

        Assert.False(_viewModel.IsRunning);
        Assert.Equal(3, _viewModel.TotalFound);
        Assert.Equal(3, _viewModel.SucceededCount);
        Assert.Equal(3, _backend.IngestCalls);
        Assert.Null(_repository.GetIncompleteExecution());
    }

    [Fact]
    public async Task StartAsync_SegundaVez_OmitePorHistorico()
    {
        CreatePdfs(2);
        _viewModel.SelectedPaths = new[] { _docsDir };
        await _viewModel.StartAsync();
        var callsAfterFirst = _backend.IngestCalls;

        await _viewModel.StartAsync();

        Assert.Equal(callsAfterFirst, _backend.IngestCalls);
        Assert.Equal(2, _viewModel.SkippedCount);
        Assert.Equal(0, _viewModel.PendingCount);
    }

    [Fact]
    public void GetPendingRecovery_DevuelveEjecucionIncompleta_YDiscardLaAborta()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");

        var pending = _viewModel.GetPendingRecovery();
        Assert.NotNull(pending);
        Assert.Equal(execution.ExecutionId, pending!.ExecutionId);

        _viewModel.DiscardExecution(pending);

        Assert.Null(_repository.GetIncompleteExecution());
    }

    [Fact]
    public async Task ResumeExecutionAsync_ReenganchaEnVueloYTerminaPendientes()
    {
        CreatePdfs(2);
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var enVuelo = new LiteDocument
        {
            ExecutionId = execution.ExecutionId,
            FileName = "doc1.pdf",
            FullPath = Path.Combine(_docsDir, "doc1.pdf"),
            FileSize = 3,
            LastModifiedUtc = "x",
            Status = LiteDocumentStatus.InFlight,
            InstanceId = "inst-1",
            StatusQueryUri = "https://backend/inst-1",
            BatchNumber = 1
        };
        var pendiente = new LiteDocument
        {
            ExecutionId = execution.ExecutionId,
            FileName = "doc2.pdf",
            FullPath = Path.Combine(_docsDir, "doc2.pdf"),
            FileSize = 3,
            LastModifiedUtc = "x",
            Status = LiteDocumentStatus.Pending,
            BatchNumber = 1
        };
        _repository.InsertDocuments(new[] { enVuelo, pendiente });

        await _viewModel.ResumeExecutionAsync(execution);

        var docs = _repository.GetDocuments(execution.ExecutionId);
        Assert.All(docs, d => Assert.Equal(LiteDocumentStatus.Succeeded, d.Status));
        Assert.Equal(1, _backend.IngestCalls); // solo el pendiente se reenvia
    }

    [Fact]
    public async Task CancelExecution_DetieneLaEjecucion()
    {
        CreatePdfs(20);
        _viewModel.SelectedPaths = new[] { _docsDir };
        _backend.OnIngest = request =>
        {
            Thread.Sleep(20);
            return new DocumentIA.Batch.Services.IngestResponse
            {
                InstanceId = "i",
                StatusQueryUri = "https://backend/" + request.Documento.Name
            };
        };

        var run = _viewModel.StartAsync();
        await Task.Delay(80);
        _viewModel.CancelExecution();
        await run;

        Assert.False(_viewModel.IsRunning);
        Assert.Null(_repository.GetIncompleteExecution());
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que falla**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar**

`src/DocumentIA.Batch.ClassificationLite/Services/SleepBlocker.cs`:

```csharp
using System.Runtime.InteropServices;

namespace DocumentIA.Batch.ClassificationLite.Services;

/// <summary>
/// Impide la suspension del equipo durante ejecuciones desatendidas (la pantalla si puede apagarse).
/// </summary>
public static class SleepBlocker
{
    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(ExecutionState flags);

    public static void PreventSleep()
        => SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired);

    public static void AllowSleep()
        => SetThreadExecutionState(ExecutionState.Continuous);
}
```

`src/DocumentIA.Batch.ClassificationLite/ViewModels/LiteMainViewModel.Orchestration.cs`:

```csharp
using System.Net.Http;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using DocumentIA.Batch.Models;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.ClassificationLite.ViewModels;

public partial class LiteMainViewModel
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    private CancellationTokenSource? _cts;
    private LiteEngine? _engine;
    private bool _isRunning;
    private bool _isPaused;
    private string _statusMessage = string.Empty;

    public IReadOnlyList<string> SelectedPaths { get; set; } = Array.Empty<string>();

    public bool IncludeSubfolders { get; set; } = true;

    public bool IsRunning { get => _isRunning; private set { _isRunning = value; OnPropertyChanged(); } }

    public bool IsPaused { get => _isPaused; private set { _isPaused = value; OnPropertyChanged(); } }

    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }

    /// <summary>Inyectable en tests; en produccion crea el adaptador sobre el entorno seleccionado.</summary>
    public Func<IIngestBackend>? BackendFactory { get; set; }

    public LiteExecution? GetPendingRecovery() => _repository.GetIncompleteExecution();

    public void DiscardExecution(LiteExecution execution)
    {
        _repository.UpdateExecutionStatus(execution.ExecutionId, LiteExecutionStatus.Aborted, setCompletedAt: true);
        StatusMessage = "Ejecucion anterior descartada.";
    }

    public async Task StartAsync(CancellationToken externalToken = default)
    {
        if (IsRunning || SelectedPaths.Count == 0)
        {
            return;
        }

        var configSnapshot = System.Text.Json.JsonSerializer.Serialize(Config);
        var execution = _repository.CreateExecution(SelectedPaths[0], IncludeSubfolders, configSnapshot);
        CurrentExecutionId = execution.ExecutionId;

        var scanner = new FolderScanner(_repository);
        var paths = SelectedPaths;
        var includeSubfolders = IncludeSubfolders;
        var config = Config;

        await RunExecutionAsync(execution, externalToken, async (engine, ct) =>
        {
            StatusMessage = "Escaneando documentos...";
            await Task.Run(() => scanner.Scan(
                execution.ExecutionId, paths, includeSubfolders,
                config.SkipAlreadyProcessed, config.ForceReprocess, config.InternalBatchSize, ct), ct);

            ReloadRows();
            RefreshCounters();

            StatusMessage = "Procesando...";
            await engine.RunAsync(execution.ExecutionId, ct);
        });
    }

    public async Task ResumeExecutionAsync(LiteExecution execution)
    {
        if (IsRunning)
        {
            return;
        }

        CurrentExecutionId = execution.ExecutionId;
        _repository.UpdateExecutionStatus(execution.ExecutionId, LiteExecutionStatus.Running);

        await RunExecutionAsync(execution, CancellationToken.None, async (engine, ct) =>
        {
            StatusMessage = "Recuperando documentos en vuelo...";
            await engine.ReattachInFlightAsync(execution.ExecutionId, ct);

            StatusMessage = "Procesando pendientes...";
            await engine.RunAsync(execution.ExecutionId, ct);
        });
    }

    private async Task RunExecutionAsync(
        LiteExecution execution,
        CancellationToken externalToken,
        Func<LiteEngine, CancellationToken, Task> body)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var backend = (BackendFactory ?? CreateDefaultBackend)();
        _engine = new LiteEngine(_repository, backend, Config);
        _engine.AutoPaused += message =>
        {
            IsPaused = true;
            StatusMessage = message;
        };

        IsRunning = true;
        IsPaused = false;
        SleepBlocker.PreventSleep();

        using var refreshTimer = new System.Threading.Timer(
            _ => { ReloadRows(); RefreshCounters(); }, null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        try
        {
            await body(_engine, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Ejecucion cancelada.";
        }
        finally
        {
            SleepBlocker.AllowSleep();
            IsRunning = false;
            IsPaused = false;
            ReloadRows();
            RefreshCounters();
            if (string.IsNullOrEmpty(StatusMessage) || StatusMessage.StartsWith("Procesando", StringComparison.Ordinal))
            {
                StatusMessage = "Ejecucion finalizada.";
            }

            _cts?.Dispose();
            _cts = null;
            _engine = null;
        }
    }

    public void PauseExecution()
    {
        _engine?.Pause();
        IsPaused = true;
        _repository.UpdateExecutionStatus(CurrentExecutionId!, LiteExecutionStatus.Paused);
        StatusMessage = "Ejecucion pausada.";
    }

    public void ResumeExecution()
    {
        _engine?.Resume();
        IsPaused = false;
        _repository.UpdateExecutionStatus(CurrentExecutionId!, LiteExecutionStatus.Running);
        StatusMessage = "Ejecucion reanudada.";
    }

    public void CancelExecution()
    {
        _engine?.Resume();
        _cts?.Cancel();
        StatusMessage = "Cancelando...";
    }

    private IIngestBackend CreateDefaultBackend()
    {
        var environment = Config.Environments.FirstOrDefault(e =>
            string.Equals(e.Name, Config.SelectedEnvironment, StringComparison.OrdinalIgnoreCase))
            ?? Config.Environments[0];

        return new IngestBackendAdapter(new DocumentIaBackendClient(SharedHttpClient), environment);
    }
}
```

`_repository` y `_configService` ya son accesibles desde este fichero: ambos partials son la MISMA clase, así que los campos privados declarados en Task 13 se ven aquí sin cambios.

- [ ] **Step 4: Ejecutar y verificar que pasa**

Run: `dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite tests/DocumentIA.Batch.ClassificationLite.Tests
git commit -m "feat(classification-lite): orquestacion de ejecucion, pausa, cancelacion y recuperacion"
```

---

### Task 15: Ventana principal — barra de acciones, contadores, grid y drag & drop

**Files:**
- Modify: `src/DocumentIA.Batch.ClassificationLite/Views/MainWindow.xaml`
- Modify: `src/DocumentIA.Batch.ClassificationLite/Views/MainWindow.xaml.cs`
- Modify: `src/DocumentIA.Batch.ClassificationLite/App.xaml.cs`

**Interfaces:**
- Consumes: `LiteMainViewModel` (Tasks 13-14), `LiteRepository.GetDefaultDbPath()`, `LiteConfigService`.
- Produces: ventana funcional. Sin tests automáticos (UI WPF); verificación manual documentada en los pasos.

- [ ] **Step 1: Escribir el XAML de la ventana**

`src/DocumentIA.Batch.ClassificationLite/Views/MainWindow.xaml`:

```xml
<Window x:Class="DocumentIA.Batch.ClassificationLite.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Batch Classification Lite" Height="760" Width="1240"
        AllowDrop="True" Drop="Window_Drop" DragOver="Window_DragOver">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Barra de acciones -->
        <WrapPanel Grid.Row="0" Margin="0,0,0,8">
            <Button x:Name="SelectFolderButton" Content="Seleccionar carpeta" Padding="10,4" Margin="0,0,6,0" Click="SelectFolder_Click"/>
            <CheckBox x:Name="IncludeSubfoldersCheck" Content="Incluir subcarpetas" IsChecked="True" VerticalAlignment="Center" Margin="0,0,12,0"/>
            <Button x:Name="ConfigButton" Content="Configuracion" Padding="10,4" Margin="0,0,6,0" Click="Config_Click"/>
            <Button x:Name="RunButton" Content="Ejecutar" Padding="10,4" Margin="0,0,6,0" Click="Run_Click"/>
            <Button x:Name="PauseButton" Content="Pausar" Padding="10,4" Margin="0,0,6,0" Click="Pause_Click"/>
            <Button x:Name="ResumeButton" Content="Reanudar" Padding="10,4" Margin="0,0,6,0" Click="Resume_Click"/>
            <Button x:Name="CancelButton" Content="Cancelar" Padding="10,4" Margin="0,0,12,0" Click="Cancel_Click"/>
            <Button x:Name="ExportExcelButton" Content="Exportar Excel" Padding="10,4" Margin="0,0,6,0" Click="ExportExcel_Click"/>
            <Button x:Name="ExportCsvButton" Content="Exportar CSV" Padding="10,4" Click="ExportCsv_Click"/>
        </WrapPanel>

        <!-- Ruta seleccionada -->
        <TextBlock Grid.Row="1" x:Name="SelectedPathText" Margin="0,0,0,8" Foreground="#555"
                   Text="Arrastra ficheros o carpetas aqui, o pulsa Seleccionar carpeta."/>

        <!-- Indicadores -->
        <UniformGrid Grid.Row="2" Columns="6" Margin="0,0,0,8">
            <StackPanel Margin="0,0,8,0">
                <TextBlock Text="Total encontrados" FontSize="11" Foreground="#666"/>
                <TextBlock Text="{Binding TotalFound}" FontSize="18" FontWeight="Bold"/>
            </StackPanel>
            <StackPanel Margin="0,0,8,0">
                <TextBlock Text="Pendientes" FontSize="11" Foreground="#666"/>
                <TextBlock Text="{Binding PendingCount}" FontSize="18" FontWeight="Bold"/>
            </StackPanel>
            <StackPanel Margin="0,0,8,0">
                <TextBlock Text="En ejecucion" FontSize="11" Foreground="#666"/>
                <TextBlock Text="{Binding InFlightCount}" FontSize="18" FontWeight="Bold"/>
            </StackPanel>
            <StackPanel Margin="0,0,8,0">
                <TextBlock Text="Procesados OK" FontSize="11" Foreground="#666"/>
                <TextBlock Text="{Binding SucceededCount}" FontSize="18" FontWeight="Bold" Foreground="#1B7F3B"/>
            </StackPanel>
            <StackPanel Margin="0,0,8,0">
                <TextBlock Text="Errores definitivos" FontSize="11" Foreground="#666"/>
                <TextBlock Text="{Binding DefinitiveErrorCount}" FontSize="18" FontWeight="Bold" Foreground="#B3261E"/>
            </StackPanel>
            <StackPanel>
                <TextBlock Text="Omitidos por historico" FontSize="11" Foreground="#666"/>
                <TextBlock Text="{Binding SkippedCount}" FontSize="18" FontWeight="Bold" Foreground="#8A6D00"/>
            </StackPanel>
        </UniformGrid>

        <!-- Grid -->
        <DockPanel Grid.Row="3">
            <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,6">
                <TextBlock Text="Filtro:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                <TextBox Width="220" Margin="0,0,12,0" Text="{Binding FilterText, UpdateSourceTrigger=PropertyChanged, Delay=300}"/>
                <TextBlock Text="Estado:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                <ComboBox Width="160" ItemsSource="{Binding StatusFilterOptions}" SelectedItem="{Binding StatusFilter}"/>
            </StackPanel>
            <DataGrid x:Name="ResultsGrid" ItemsSource="{Binding RowsView}" AutoGenerateColumns="False"
                      IsReadOnly="True" EnableRowVirtualization="True" EnableColumnVirtualization="True"
                      VirtualizingPanel.IsVirtualizing="True" VirtualizingPanel.VirtualizationMode="Recycling"
                      MouseDoubleClick="ResultsGrid_MouseDoubleClick">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="FileName" Binding="{Binding FileName}" Width="2*"/>
                    <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="110"/>
                    <DataGridTextColumn Header="PagesIncluded" Binding="{Binding PagesIncluded}" Width="100"/>
                    <DataGridTextColumn Header="Pages" Binding="{Binding Pages}" Width="70"/>
                    <DataGridTextColumn Header="TDN1" Binding="{Binding Tdn1}" Width="110"/>
                    <DataGridTextColumn Header="TDN2" Binding="{Binding Tdn2}" Width="130"/>
                    <DataGridTextColumn Header="Confidence" Binding="{Binding Confidence}" Width="90"/>
                    <DataGridTextColumn Header="ProcessDate" Binding="{Binding ProcessDate}" Width="160"/>
                    <DataGridTextColumn Header="TotalDurationMs" Binding="{Binding TotalDurationMs}" Width="120"/>
                </DataGrid.Columns>
            </DataGrid>
        </DockPanel>

        <StatusBar Grid.Row="4" Margin="0,8,0,0">
            <StatusBarItem><TextBlock Text="{Binding StatusMessage}"/></StatusBarItem>
        </StatusBar>
    </Grid>
</Window>
```

- [ ] **Step 2: Escribir el code-behind**

`src/DocumentIA.Batch.ClassificationLite/Views/MainWindow.xaml.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Services;
using DocumentIA.Batch.ClassificationLite.ViewModels;
using Microsoft.Win32;

namespace DocumentIA.Batch.ClassificationLite.Views;

public partial class MainWindow : Window
{
    private readonly LiteMainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new LiteMainViewModel(
            new LiteRepository(LiteRepository.GetDefaultDbPath()),
            new LiteConfigService());
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var pending = _viewModel.GetPendingRecovery();
        if (pending is null)
        {
            return;
        }

        var counters = new LiteRepository(LiteRepository.GetDefaultDbPath()).GetCounters(pending.ExecutionId);
        var answer = MessageBox.Show(
            $"Hay una ejecucion incompleta sobre '{pending.RootPath}'.\n\n" +
            $"Pendientes: {counters.Pending}\nEn vuelo: {counters.InFlight}\nCompletados: {counters.Succeeded}\n\n" +
            "¿Quieres continuarla? (No = descartarla)",
            "Ejecucion incompleta",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            await _viewModel.ResumeExecutionAsync(pending);
        }
        else
        {
            _viewModel.DiscardExecution(pending);
        }
    }

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Selecciona la carpeta con los documentos" };
        if (dialog.ShowDialog() == true)
        {
            SetPaths(new[] { dialog.FolderName });
        }
    }

    private void SetPaths(IReadOnlyList<string> paths)
    {
        _viewModel.SelectedPaths = paths;
        _viewModel.IncludeSubfolders = IncludeSubfoldersCheck.IsChecked == true;
        SelectedPathText.Text = paths.Count == 1
            ? paths[0]
            : $"{paths.Count} elementos seleccionados";
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped && dropped.Length > 0)
        {
            SetPaths(dropped);
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPaths.Count == 0)
        {
            MessageBox.Show("Selecciona antes una carpeta o arrastra ficheros.", "Batch Classification Lite",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _viewModel.IncludeSubfolders = IncludeSubfoldersCheck.IsChecked == true;
        await _viewModel.StartAsync();
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => _viewModel.PauseExecution();

    private void Resume_Click(object sender, RoutedEventArgs e) => _viewModel.ResumeExecution();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _viewModel.CancelExecution();

    private void Config_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfigDialog(_viewModel.Config) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.SaveConfig();
        }
    }

    private void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "clasificacion.xlsx" };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.ExportExcel(dialog.FileName);
            MessageBox.Show("Exportacion completada.", "Batch Classification Lite");
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "clasificacion.csv" };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.ExportCsv(dialog.FileName);
            MessageBox.Show("Exportacion completada.", "Batch Classification Lite");
        }
    }

    private void ResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is LiteDocumentRow row)
        {
            new DetailDialog(row) { Owner = this }.ShowDialog();
        }
    }
}
```

- [ ] **Step 3: Añadir `SaveConfig` al ViewModel y ajustar `App.xaml.cs`**

En `src/DocumentIA.Batch.ClassificationLite/ViewModels/LiteMainViewModel.cs`, añadir tras `RefreshCounters()`:

```csharp
    public void SaveConfig() => _configService.Save(Config);
```

`src/DocumentIA.Batch.ClassificationLite/App.xaml.cs` (manejo global de excepciones no controladas):

```csharp
using System.Windows;
using System.Windows.Threading;

namespace DocumentIA.Batch.ClassificationLite;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Se ha producido un error inesperado:\n\n" + e.Exception.Message,
            "Batch Classification Lite",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
```

Nota: `ConfigDialog` y `DetailDialog` se crean en la Task 16; hasta entonces el proyecto NO compila. Por eso los pasos 4-5 de esta tarea se hacen tras la 16 — este es el único punto del plan donde dos tareas comparten un ciclo de build. Si prefieres verificar antes, crea stubs vacíos de ambos diálogos y sustitúyelos en la Task 16.

- [ ] **Step 4: Crear stubs de los diálogos para poder compilar**

`src/DocumentIA.Batch.ClassificationLite/Views/ConfigDialog.xaml`:

```xml
<Window x:Class="DocumentIA.Batch.ClassificationLite.Views.ConfigDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Configuracion" Height="200" Width="300">
    <Grid/>
</Window>
```

`src/DocumentIA.Batch.ClassificationLite/Views/ConfigDialog.xaml.cs`:

```csharp
using System.Windows;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.Views;

public partial class ConfigDialog : Window
{
    public ConfigDialog(LiteConfig config)
    {
        InitializeComponent();
        DataContext = config;
    }
}
```

`src/DocumentIA.Batch.ClassificationLite/Views/DetailDialog.xaml`:

```xml
<Window x:Class="DocumentIA.Batch.ClassificationLite.Views.DetailDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Detalle" Height="200" Width="300">
    <Grid/>
</Window>
```

`src/DocumentIA.Batch.ClassificationLite/Views/DetailDialog.xaml.cs`:

```csharp
using System.Windows;
using DocumentIA.Batch.ClassificationLite.ViewModels;

namespace DocumentIA.Batch.ClassificationLite.Views;

public partial class DetailDialog : Window
{
    public DetailDialog(LiteDocumentRow row)
    {
        InitializeComponent();
        DataContext = row;
    }
}
```

- [ ] **Step 5: Compilar y verificar**

```bash
dotnet build DocumentIA.Batch.sln -v minimal
dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal
```

Esperado: build OK, tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite
git commit -m "feat(classification-lite): ventana principal con acciones, contadores, grid y drag and drop"
```

---

### Task 16: Diálogos de Configuración y Detalle

**Files:**
- Modify: `src/DocumentIA.Batch.ClassificationLite/Views/ConfigDialog.xaml` y `.xaml.cs`
- Modify: `src/DocumentIA.Batch.ClassificationLite/Views/DetailDialog.xaml` y `.xaml.cs`

**Interfaces:**
- Consumes: `LiteConfig` (Task 2), `LiteDocumentRow` (Task 13), `LiteConfigService.Normalize`.
- Produces: los dos diálogos completos que la Task 15 ya invoca (`new ConfigDialog(config)`, `new DetailDialog(row)`).

- [ ] **Step 1: Implementar el diálogo de configuración**

`src/DocumentIA.Batch.ClassificationLite/Views/ConfigDialog.xaml`:

```xml
<Window x:Class="DocumentIA.Batch.ClassificationLite.Views.ConfigDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Configuracion" Height="620" Width="620"
        WindowStartupLocation="CenterOwner" ResizeMode="CanResize">
    <Grid Margin="14">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <ScrollViewer Grid.Row="0" VerticalScrollBarVisibility="Auto">
            <StackPanel>
                <GroupBox Header="Procesamiento" Padding="8" Margin="0,0,0,10">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="200"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>

                        <TextBlock Grid.Row="0" Grid.Column="0" Text="Environment" VerticalAlignment="Center" Margin="0,4"/>
                        <ComboBox Grid.Row="0" Grid.Column="1" x:Name="EnvironmentCombo" Margin="0,4"
                                  DisplayMemberPath="Name" SelectionChanged="EnvironmentCombo_SelectionChanged"/>

                        <TextBlock Grid.Row="1" Grid.Column="0" Text="Backend URL" VerticalAlignment="Center" Margin="0,4"/>
                        <TextBox Grid.Row="1" Grid.Column="1" x:Name="BackendUrlBox" Margin="0,4"/>

                        <TextBlock Grid.Row="2" Grid.Column="0" Text="Function Key" VerticalAlignment="Center" Margin="0,4"/>
                        <TextBox Grid.Row="2" Grid.Column="1" x:Name="FunctionKeyBox" Margin="0,4"/>

                        <TextBlock Grid.Row="3" Grid.Column="0" Text="Parallel Queries (1-10)" VerticalAlignment="Center" Margin="0,4"/>
                        <TextBox Grid.Row="3" Grid.Column="1" x:Name="ParallelBox" Margin="0,4"/>

                        <TextBlock Grid.Row="4" Grid.Column="0" Text="Internal Batch Size" VerticalAlignment="Center" Margin="0,4"/>
                        <TextBox Grid.Row="4" Grid.Column="1" x:Name="BatchSizeBox" Margin="0,4"/>

                        <TextBlock Grid.Row="5" Grid.Column="0" Text="Polling Interval (segundos)" VerticalAlignment="Center" Margin="0,4"/>
                        <TextBox Grid.Row="5" Grid.Column="1" x:Name="PollingBox" Margin="0,4"/>
                    </Grid>
                </GroupBox>

                <GroupBox Header="Clasificacion" Padding="8" Margin="0,0,0,10">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="200"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>

                        <TextBlock Grid.Row="0" Grid.Column="0" Text="Classification Level" VerticalAlignment="Center" Margin="0,4"/>
                        <ComboBox Grid.Row="0" Grid.Column="1" x:Name="LevelCombo" Margin="0,4">
                            <ComboBoxItem Content="TDN1_TDN2"/>
                            <ComboBoxItem Content="TDN1"/>
                            <ComboBoxItem Content="DEFAULT"/>
                        </ComboBox>

                        <TextBlock Grid.Row="1" Grid.Column="0" Text="Provider" VerticalAlignment="Center" Margin="0,4"/>
                        <ComboBox Grid.Row="1" Grid.Column="1" x:Name="ProviderCombo" IsEditable="True" Margin="0,4">
                            <ComboBoxItem Content="auto"/>
                            <ComboBoxItem Content="hybrid"/>
                            <ComboBoxItem Content="hybrid-rules-gpt-di"/>
                            <ComboBoxItem Content="hybrid-rules-di-gpt"/>
                            <ComboBoxItem Content="hybrid-tdn"/>
                            <ComboBoxItem Content="rules"/>
                            <ComboBoxItem Content="gpt"/>
                            <ComboBoxItem Content="di"/>
                        </ComboBox>

                        <TextBlock Grid.Row="2" Grid.Column="0" Text="Model" VerticalAlignment="Center" Margin="0,4"/>
                        <TextBox Grid.Row="2" Grid.Column="1" x:Name="ModelBox" Margin="0,4"/>

                        <CheckBox Grid.Row="3" Grid.Column="1" x:Name="OnlyClassificationCheck"
                                  Content="Only Classification" Margin="0,8"/>
                    </Grid>
                </GroupBox>

                <GroupBox Header="Reprocesado" Padding="8" Margin="0,0,0,10">
                    <StackPanel>
                        <CheckBox x:Name="ForceReprocessCheck" Content="Force Reprocess (ignora historico local y fuerza reproceso en el backend)" Margin="0,4"/>
                        <StackPanel Orientation="Horizontal" Margin="0,4">
                            <TextBlock Text="Max Retries" Width="200" VerticalAlignment="Center"/>
                            <TextBox x:Name="MaxRetriesBox" Width="80"/>
                        </StackPanel>
                        <TextBlock Text="Retry Strategy: Batch Completion (fija)" Foreground="#666" Margin="0,4"/>
                    </StackPanel>
                </GroupBox>

                <GroupBox Header="Gestion de historicos" Padding="8">
                    <CheckBox x:Name="SkipProcessedCheck" Content="Skip Already Processed (omite documentos ya clasificados con exito)" Margin="0,4"/>
                </GroupBox>
            </StackPanel>
        </ScrollViewer>

        <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button Content="Guardar" Width="100" Margin="0,0,8,0" Click="Save_Click" IsDefault="True"/>
            <Button Content="Cancelar" Width="100" Click="Cancel_Click" IsCancel="True"/>
        </StackPanel>
    </Grid>
</Window>
```

`src/DocumentIA.Batch.ClassificationLite/Views/ConfigDialog.xaml.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.ClassificationLite.Views;

public partial class ConfigDialog : Window
{
    private readonly LiteConfig _config;

    public ConfigDialog(LiteConfig config)
    {
        InitializeComponent();
        _config = config;
        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        EnvironmentCombo.ItemsSource = _config.Environments;
        EnvironmentCombo.SelectedItem = _config.Environments.FirstOrDefault(e =>
            string.Equals(e.Name, _config.SelectedEnvironment, StringComparison.OrdinalIgnoreCase))
            ?? _config.Environments.FirstOrDefault();

        ParallelBox.Text = _config.ParallelQueries.ToString(CultureInfo.InvariantCulture);
        BatchSizeBox.Text = _config.InternalBatchSize.ToString(CultureInfo.InvariantCulture);
        PollingBox.Text = _config.PollingIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        MaxRetriesBox.Text = _config.MaxRetries.ToString(CultureInfo.InvariantCulture);
        ModelBox.Text = _config.Model;
        ProviderCombo.Text = _config.Provider;
        LevelCombo.Text = _config.ClassificationLevel;
        OnlyClassificationCheck.IsChecked = _config.OnlyClassification;
        ForceReprocessCheck.IsChecked = _config.ForceReprocess;
        SkipProcessedCheck.IsChecked = _config.SkipAlreadyProcessed;
    }

    private void EnvironmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EnvironmentCombo.SelectedItem is EnvironmentConfig environment)
        {
            BackendUrlBox.Text = environment.BackendUrl;
            FunctionKeyBox.Text = environment.FunctionKey;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (EnvironmentCombo.SelectedItem is EnvironmentConfig environment)
        {
            environment.BackendUrl = BackendUrlBox.Text.Trim();
            environment.FunctionKey = FunctionKeyBox.Text.Trim();
            _config.SelectedEnvironment = environment.Name;
        }

        _config.ParallelQueries = ParseInt(ParallelBox.Text, _config.ParallelQueries);
        _config.InternalBatchSize = ParseInt(BatchSizeBox.Text, _config.InternalBatchSize);
        _config.PollingIntervalSeconds = ParseInt(PollingBox.Text, _config.PollingIntervalSeconds);
        _config.MaxRetries = ParseInt(MaxRetriesBox.Text, _config.MaxRetries);
        _config.Model = string.IsNullOrWhiteSpace(ModelBox.Text) ? "auto" : ModelBox.Text.Trim();
        _config.Provider = string.IsNullOrWhiteSpace(ProviderCombo.Text) ? "auto" : ProviderCombo.Text.Trim();
        _config.ClassificationLevel = string.IsNullOrWhiteSpace(LevelCombo.Text) ? "TDN1_TDN2" : LevelCombo.Text.Trim();
        _config.OnlyClassification = OnlyClassificationCheck.IsChecked == true;
        _config.ForceReprocess = ForceReprocessCheck.IsChecked == true;
        _config.SkipAlreadyProcessed = SkipProcessedCheck.IsChecked == true;

        LiteConfigService.Normalize(_config);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static int ParseInt(string text, int fallback)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
```

- [ ] **Step 2: Implementar el diálogo de detalle**

`src/DocumentIA.Batch.ClassificationLite/Views/DetailDialog.xaml`:

```xml
<Window x:Class="DocumentIA.Batch.ClassificationLite.Views.DetailDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Detalle del documento" Height="700" Width="900"
        WindowStartupLocation="CenterOwner">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" Margin="0,0,0,10">
            <TextBlock Text="{Binding FileName}" FontSize="16" FontWeight="Bold" Margin="0,0,0,6"/>
            <UniformGrid Columns="5">
                <StackPanel><TextBlock Text="Status" FontSize="11" Foreground="#666"/><TextBlock Text="{Binding Status}"/></StackPanel>
                <StackPanel><TextBlock Text="TDN1" FontSize="11" Foreground="#666"/><TextBlock Text="{Binding Tdn1}"/></StackPanel>
                <StackPanel><TextBlock Text="TDN2" FontSize="11" Foreground="#666"/><TextBlock Text="{Binding Tdn2}"/></StackPanel>
                <StackPanel><TextBlock Text="Confidence" FontSize="11" Foreground="#666"/><TextBlock Text="{Binding Confidence}"/></StackPanel>
                <StackPanel><TextBlock Text="TotalDurationMs" FontSize="11" Foreground="#666"/><TextBlock Text="{Binding TotalDurationMs}"/></StackPanel>
            </UniformGrid>
            <TextBlock Margin="0,6,0,0" Foreground="#B3261E" Text="{Binding ErrorMessage}"
                       TextWrapping="Wrap"/>
        </StackPanel>

        <TabControl Grid.Row="1">
            <TabItem Header="Request enviada">
                <TextBox Text="{Binding RequestJson, Mode=OneWay}" IsReadOnly="True" FontFamily="Consolas"
                         VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto"
                         TextWrapping="NoWrap" AcceptsReturn="True"/>
            </TabItem>
            <TabItem Header="Response recibida">
                <TextBox Text="{Binding ResponseJson, Mode=OneWay}" IsReadOnly="True" FontFamily="Consolas"
                         VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto"
                         TextWrapping="NoWrap" AcceptsReturn="True"/>
            </TabItem>
        </TabControl>

        <Button Grid.Row="2" Content="Cerrar" Width="100" HorizontalAlignment="Right" Margin="0,10,0,0"
                IsCancel="True" Click="Close_Click"/>
    </Grid>
</Window>
```

`src/DocumentIA.Batch.ClassificationLite/Views/DetailDialog.xaml.cs`:

```csharp
using System.Windows;
using DocumentIA.Batch.ClassificationLite.ViewModels;

namespace DocumentIA.Batch.ClassificationLite.Views;

public partial class DetailDialog : Window
{
    public DetailDialog(LiteDocumentRow row)
    {
        InitializeComponent();
        DataContext = row;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 3: Compilar y ejecutar tests**

```bash
dotnet build DocumentIA.Batch.sln -v minimal
dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal
```

Esperado: build OK, tests PASS.

- [ ] **Step 4: Verificación manual de la UI**

```bash
dotnet run --project src/DocumentIA.Batch.ClassificationLite
```

Comprobar: la ventana abre; "Configuracion" muestra los 4 grupos con los defaults del spec (PRO, 2, 1000, 60, TDN1_TDN2, auto, auto, Only Classification marcado, Force Reprocess desmarcado, Max Retries 3, Skip Already Processed marcado); Guardar persiste en `config.json` junto al exe; arrastrar una carpeta actualiza la ruta mostrada. Cerrar sin ejecutar.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.ClassificationLite
git commit -m "feat(classification-lite): dialogos de configuracion y detalle request/response"
```

---

### Task 17: Script de publish self-contained y README

**Files:**
- Create: `scripts/publish-classification-lite.ps1`
- Create: `src/DocumentIA.Batch.ClassificationLite/README.md`

**Interfaces:**
- Consumes: proyecto completo (Tasks 1-16).
- Produces: artefacto distribuible en `artifacts/publish/classification-lite-win-x64/`.

- [ ] **Step 1: Crear el script de publish**

`scripts/publish-classification-lite.ps1`:

```powershell
<#
.SYNOPSIS
    Publica Batch Classification Lite como ejecutable autocontenido single-file (win-x64).

.EXAMPLE
    pwsh ./scripts/publish-classification-lite.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputPath = "artifacts/publish/classification-lite-win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/DocumentIA.Batch.ClassificationLite/DocumentIA.Batch.ClassificationLite.csproj"
$output = Join-Path $repoRoot $OutputPath

Write-Host "Publicando $project -> $output" -ForegroundColor Cyan

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "El publish ha fallado con codigo $LASTEXITCODE"
}

$configPath = Join-Path $output "config.json"
if (-not (Test-Path $configPath)) {
    @{
        SelectedEnvironment = "PRO"
        Environments = @(
            @{
                Name = "PRO"
                BackendUrl = "https://srbappprodocai.azurewebsites.net"
                FunctionKey = ""
            }
        )
        ParallelQueries = 2
        InternalBatchSize = 1000
        PollingIntervalSeconds = 60
        ClassificationLevel = "TDN1_TDN2"
        Provider = "auto"
        Model = "auto"
        OnlyClassification = $true
        ForceReprocess = $false
        MaxRetries = 3
        SkipAlreadyProcessed = $true
    } | ConvertTo-Json -Depth 5 | Set-Content -Path $configPath -Encoding UTF8
    Write-Host "config.json inicial generado (Function Key vacia: rellenar antes de distribuir)." -ForegroundColor Yellow
}

Write-Host "Publicacion completada en $output" -ForegroundColor Green
```

- [ ] **Step 2: Crear el README del proyecto**

`src/DocumentIA.Batch.ClassificationLite/README.md`:

```markdown
# Batch Classification Lite

Herramienta de clasificacion masiva simplificada (hasta 100.000 documentos por ejecucion).

## Que hace

Recorre una carpeta (local o de red, con o sin subcarpetas), envia cada PDF a DocumentIA
para clasificacion (TDN1/TDN2) y guarda el resultado en una base SQLite local.

## Ejecucion desde codigo

```bash
dotnet run --project src/DocumentIA.Batch.ClassificationLite
```

## Publicacion

```bash
pwsh ./scripts/publish-classification-lite.ps1
```

Genera `artifacts/publish/classification-lite-win-x64/` con el ejecutable autocontenido
y un `config.json`. Rellenar la Function Key del entorno PRO antes de distribuir.

## Ficheros de estado

| Fichero | Ubicacion | Contenido |
|---------|-----------|-----------|
| `config.json` | Junto al ejecutable | Entornos, paralelismo, lotes, polling, opciones de clasificacion |
| `lite.db` | `%LocalAppData%\DocumentIA.BatchLite\` | Historico permanente de ejecuciones y documentos |

La base de datos NO se borra automaticamente: permite omitir documentos ya clasificados
con exito (clave: nombre + tamano + fecha de modificacion) y recuperar ejecuciones
interrumpidas por un cierre inesperado.

## Configuracion por defecto

| Opcion | Valor |
|--------|-------|
| Environment | PRO |
| Parallel Queries | 2 |
| Internal Batch Size | 1000 |
| Polling Interval | 60 s (adaptativo: 3/5/10/20/30 s y despues el intervalo) |
| Classification Level | TDN1_TDN2 |
| Provider / Model | auto / auto |
| Only Classification | true |
| Force Reprocess | false |
| Max Retries | 3 (estrategia Batch Completion) |
| Skip Already Processed | true |

## Recomendaciones operativas

- Mantener `Parallel Queries = 2` en horario laboral para no degradar PRO.
- Para volumenes grandes, lanzar la ejecucion al final de la jornada y dejar el equipo
  encendido: la aplicacion impide la suspension mientras procesa.
- Con `Skip Already Processed` activo se puede relanzar la misma carpeta cada dia:
  solo se procesan los documentos nuevos.

## Tests

```bash
dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests
```
```

- [ ] **Step 3: Ejecutar el publish y verificar**

```bash
pwsh ./scripts/publish-classification-lite.ps1
ls artifacts/publish/classification-lite-win-x64/DocumentIA.Batch.ClassificationLite.exe
```

Esperado: el ejecutable existe y `config.json` está junto a él.

- [ ] **Step 4: Verificación final completa**

```bash
dotnet build DocumentIA.Batch.sln -v minimal
dotnet test tests/DocumentIA.Batch.ClassificationLite.Tests -v minimal
dotnet test tests/DocumentIA.Batch.Tests -v minimal
dotnet test tests/DocumentIA.Batch.Classification.Tests -v minimal
```

Esperado: todo en verde (los tests de los proyectos existentes NO deben haberse visto afectados).

- [ ] **Step 5: Commit**

```bash
git add scripts/publish-classification-lite.ps1 src/DocumentIA.Batch.ClassificationLite/README.md
git commit -m "chore(classification-lite): script de publish self-contained y README"
```

---

## Verificación final del plan

Al terminar las 17 tareas:

```bash
dotnet build DocumentIA.Batch.sln -v minimal
dotnet test DocumentIA.Batch.sln -v minimal
git log --oneline feature/batch-classification-lite ^main
```

Smoke E2E manual (requiere Function Key de PRO válida en `config.json`): lote pequeño de 5-10 PDFs
reales, verificar que el grid se llena con TDN1/TDN2/Confidence, que el doble click muestra request
y response, que la exportación Excel abre en Excel, y que relanzar la misma carpeta omite todo por
histórico.

## Cobertura del spec

| Sección del spec | Tareas |
|------------------|--------|
| §2 Arquitectura general | 1 |
| §3 Modelo de datos SQLite | 3, 4 |
| §4 Escaneo progresivo | 8 |
| §4 Lotes internos y ciclo por documento | 9, 10 |
| §4 Polling adaptativo | 6, 9 |
| §4 Reintentos batch-completion | 10 |
| §4 Pausar / Reanudar / Cancelar | 9, 14 |
| §4 Anti-suspensión desatendida | 14 |
| §4 Auto-pausa por 401 | 11 |
| §5 UI (barra, contadores, grid, filtros) | 13, 15 |
| §5 Detalle request/response | 5, 16 |
| §5 Configuración (4 grupos) | 2, 16 |
| §6 Recuperación ante fallos | 11, 14, 15 |
| §7 Exportación Excel/CSV | 12, 13 |
| §8 Distribución | 17 |
| §9 Testing | 2-14 (TDD en cada tarea) |
| §11 Recomendaciones operativas | 17 (README) |
