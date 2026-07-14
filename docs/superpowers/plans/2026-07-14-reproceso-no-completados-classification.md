# Reproceso de documentos no completados (Batch.Classification) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir reprocesar manualmente, desde la sesión actual de DocumentIA.Batch.Classification, los documentos que quedaron sin resultado (`Error`, `Cancelled`, o `Pendiente` tras un lote), con selección por checkbox por fila.

**Architecture:** Se añade un servicio de lógica pura (`ClassificationReprocessPolicy`) que decide elegibilidad y limpia la traza de un documento. El `ClassificationDocumentItem` gana `IsSelected` para la selección en la grilla. `ClassificationMainViewModel` expone un `ReprocessCommand` que resuelve candidatos (marcados vs. todos los elegibles), los resetea y los reenvía por el pipeline existente, previamente refactorizado en un `ProcessFilesAsync` común reutilizado por "Procesar" y "Reprocesar".

**Tech Stack:** C# / .NET 8, WPF (MVVM), xUnit para tests.

## Global Constraints

- **Repo:** `c:/temp/MVP/DocumentIA.Batch` — proyecto `src/DocumentIA.Batch.Classification`.
- **Entorno:** Windows 11 + Git Bash. Comandos `dotnet` desde la raíz del repo. Rutas Windows / forward slashes.
- **Tests:** xUnit, proyecto `tests/DocumentIA.Batch.Classification.Tests`.
- **Work items:** referenciar en cada commit el AB# de la task (`AB#99904`, `AB#99905`, `AB#99906`, `AB#99907`).
- **Autoría:** sin líneas `Co-Authored-By`, sin "Generated with…", sin mención de modelos en commits ni código.
- **Rama:** trabajar sobre `feature/reproceso-no-completados-classification` (ya creada).
- **Elegibilidad (verbatim de la spec):** reprocesable si `Status == "Error"`, o `RuntimeStatus` es `Failed`/`Terminated`, o `Status == "Cancelled"`, o (`Status == "Pendiente"` **y** ya se corrió un lote en la sesión). No reprocesable: `OK`, `Completado`, `REVISION`, `VALIDACION_CON_ERRORES`, `BAJA_CONFIANZA`, y estados en curso (`En cola`, `Enviando`, `Processing`, `En ejecución`). Comparaciones `OrdinalIgnoreCase`.

## File Structure

- **Create:** `src/DocumentIA.Batch.Classification/Services/ClassificationReprocessPolicy.cs` — lógica pura de elegibilidad y reset.
- **Create:** `tests/DocumentIA.Batch.Classification.Tests/ClassificationReprocessPolicyTests.cs` — tests de la política.
- **Create:** `tests/DocumentIA.Batch.Classification.Tests/ClassificationMainViewModelReprocessTests.cs` — tests de VM (candidatos, CanReprocess).
- **Modify:** `src/DocumentIA.Batch.Classification/Models/ClassificationDocumentItem.cs` — propiedad `IsSelected`.
- **Modify:** `src/DocumentIA.Batch.Classification/ViewModels/ClassificationMainViewModel.cs` — `ReprocessCommand`, `SelectAllFiles`, flag `_hasBatchRun`, refactor `ProcessFilesAsync`.
- **Modify:** `src/DocumentIA.Batch.Classification/MainWindow.xaml` — columna de checkbox + botón "Reprocesar".

---

## Task 1: `ClassificationReprocessPolicy` (AB#99904)

Servicio de lógica pura: decide elegibilidad y resetea la traza. Sin dependencias de UI.

**Files:**
- Create: `src/DocumentIA.Batch.Classification/Services/ClassificationReprocessPolicy.cs`
- Test: `tests/DocumentIA.Batch.Classification.Tests/ClassificationReprocessPolicyTests.cs`

**Interfaces:**
- Consumes: `ClassificationDocumentItem` de `DocumentIA.Batch.Classification.Models` (propiedades `Status`, `RuntimeStatus` y todos los campos de traza; `IsSelected` se añade en Task 2, no se referencia aquí).
- Produces:
  - `static bool ClassificationReprocessPolicy.IsReprocessable(ClassificationDocumentItem file, bool hasBatchRun)`
  - `static void ClassificationReprocessPolicy.ResetForReprocess(ClassificationDocumentItem file)`

- [ ] **Step 1: Escribir el test que falla**

Create `tests/DocumentIA.Batch.Classification.Tests/ClassificationReprocessPolicyTests.cs`:

```csharp
using DocumentIA.Batch.Classification.Models;
using DocumentIA.Batch.Classification.Services;
using Xunit;

namespace DocumentIA.Batch.Classification.Tests;

public class ClassificationReprocessPolicyTests
{
    [Theory]
    [InlineData("Error", null, true, true)]
    [InlineData("Error", null, false, true)]
    [InlineData("Cancelled", null, true, true)]
    [InlineData("Cancelled", null, false, true)]
    [InlineData("Pendiente", null, true, true)]
    [InlineData("Pendiente", null, false, false)]
    [InlineData("OK", null, true, false)]
    [InlineData("Completado", null, true, false)]
    [InlineData("REVISION", null, true, false)]
    [InlineData("VALIDACION_CON_ERRORES", null, true, false)]
    [InlineData("BAJA_CONFIANZA", null, true, false)]
    [InlineData("En cola", null, true, false)]
    [InlineData("Enviando", null, true, false)]
    [InlineData("Processing", null, true, false)]
    [InlineData("En ejecución", null, true, false)]
    [InlineData("OK", "Failed", true, true)]
    [InlineData("OK", "Terminated", true, true)]
    [InlineData("OK", "Completed", true, false)]
    public void IsReprocessable_MatrizDeEstados(string status, string? runtimeStatus, bool hasBatchRun, bool expected)
    {
        var file = new ClassificationDocumentItem
        {
            Status = status,
            RuntimeStatus = runtimeStatus ?? string.Empty
        };

        Assert.Equal(expected, ClassificationReprocessPolicy.IsReprocessable(file, hasBatchRun));
    }

    [Fact]
    public void ResetForReprocess_LimpiaTraza_YDejaPendiente()
    {
        var file = new ClassificationDocumentItem
        {
            FileName = "doc.pdf",
            FullPath = @"C:\docs\doc.pdf",
            Status = "Error",
            CorrelationId = "corr-1",
            InstanceId = "inst-1",
            RuntimeStatus = "Failed",
            StatusQueryUri = "http://status",
            MensajeError = "boom",
            FechaInicio = DateTime.Now,
            FechaFin = DateTime.Now,
            OutputJsonPath = @"C:\runs\out.json",
            IdentificacionDocumento = "id-doc",
            TipologiaIdentificada = "tip",
            ConfianzaGlobal = "0.9",
            ResultadoEstado = "REVISION",
            IdentificacionGuid = "guid",
            FechaProceso = "2026-07-14",
            Paginas = "3",
            Tdn1 = "t1",
            Tdn2 = "t2",
            Matricula = "mat",
            Clasificador = "clas",
            FallbackLlm = "fb",
            FallbackRazon = "razon",
            JustificacionClasificacion = "just",
            ClassificationOnlyOutput = "co",
            TipologiaFamilia = "fam",
            TipologiaVersion = "v1",
            TipologiaNombre = "nombre",
            TipologiaMgdcMatricula = "mgdc",
            GdcTipoDocumento = "gtd",
            GdcSubtipoDocumento = "gstd",
            GdcSerie = "serie",
            GptDescripcion = "desc",
            Resumen = "resumen",
            RecorteAplicado = "si",
            PaginasIncluidas = "1-3",
            MarkdownGenerado = "md",
            OrigenMarkdown = "pdfpig",
            ModeloLlmUsado = "modelo",
            ActividadActual = "act",
            ActividadesCompletadas = "2",
            ActividadesTotales = "3",
            DuracionTotalMs = "1234",
            TimelineActividades = "timeline",
            Proveedor = "prov",
            MotivoDescarte = "motivo",
            ReutilizadaPorDuplicado = true,
            MensajeReutilizacion = "reutil"
        };
        file.DetalleProveedores.Add(new PropuestaProveedor { Proveedor = "p" });

        ClassificationReprocessPolicy.ResetForReprocess(file);

        Assert.Equal("Pendiente", file.Status);
        Assert.Equal("doc.pdf", file.FileName);
        Assert.Equal(@"C:\docs\doc.pdf", file.FullPath);
        Assert.Equal(string.Empty, file.CorrelationId);
        Assert.Equal(string.Empty, file.InstanceId);
        Assert.Equal(string.Empty, file.RuntimeStatus);
        Assert.Equal(string.Empty, file.StatusQueryUri);
        Assert.Equal(string.Empty, file.MensajeError);
        Assert.Null(file.FechaInicio);
        Assert.Null(file.FechaFin);
        Assert.Equal(string.Empty, file.OutputJsonPath);
        Assert.Equal(string.Empty, file.TipologiaIdentificada);
        Assert.Equal(string.Empty, file.ConfianzaGlobal);
        Assert.Equal(string.Empty, file.Resumen);
        Assert.Equal(string.Empty, file.TimelineActividades);
        Assert.False(file.ReutilizadaPorDuplicado);
        Assert.Empty(file.DetalleProveedores);
    }
}
```

- [ ] **Step 2: Ejecutar el test y verificar que falla**

Run: `cd /c/temp/MVP/DocumentIA.Batch && dotnet test tests/DocumentIA.Batch.Classification.Tests --filter "FullyQualifiedName~ClassificationReprocessPolicyTests"`
Expected: FAIL de compilación — `ClassificationReprocessPolicy` no existe.

- [ ] **Step 3: Implementar el servicio**

Create `src/DocumentIA.Batch.Classification/Services/ClassificationReprocessPolicy.cs`:

```csharp
using DocumentIA.Batch.Classification.Models;

namespace DocumentIA.Batch.Classification.Services;

/// <summary>
/// Decide qué documentos de un lote de clasificación pueden reprocesarse
/// (los que no obtuvieron resultado) y limpia su traza para relanzarlos.
/// Lógica pura, sin dependencias de UI.
/// </summary>
public static class ClassificationReprocessPolicy
{
    public static bool IsReprocessable(ClassificationDocumentItem file, bool hasBatchRun)
    {
        if (string.Equals(file.Status, "Error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(file.RuntimeStatus, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.RuntimeStatus, "Terminated", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return hasBatchRun
            && string.Equals(file.Status, "Pendiente", StringComparison.OrdinalIgnoreCase);
    }

    public static void ResetForReprocess(ClassificationDocumentItem file)
    {
        file.CorrelationId = string.Empty;
        file.InstanceId = string.Empty;
        file.RuntimeStatus = string.Empty;
        file.StatusQueryUri = string.Empty;

        file.MensajeError = string.Empty;
        file.FechaInicio = null;
        file.FechaFin = null;

        file.OutputJsonPath = string.Empty;
        file.IdentificacionDocumento = string.Empty;
        file.TipologiaIdentificada = string.Empty;
        file.ConfianzaGlobal = string.Empty;
        file.ResultadoEstado = string.Empty;
        file.IdentificacionGuid = string.Empty;
        file.FechaProceso = string.Empty;
        file.Paginas = string.Empty;

        file.Tdn1 = string.Empty;
        file.Tdn2 = string.Empty;
        file.Matricula = string.Empty;
        file.Clasificador = string.Empty;
        file.FallbackLlm = string.Empty;
        file.FallbackRazon = string.Empty;
        file.JustificacionClasificacion = string.Empty;
        file.ClassificationOnlyOutput = string.Empty;
        file.TipologiaFamilia = string.Empty;
        file.TipologiaVersion = string.Empty;
        file.TipologiaNombre = string.Empty;
        file.TipologiaMgdcMatricula = string.Empty;
        file.GdcTipoDocumento = string.Empty;
        file.GdcSubtipoDocumento = string.Empty;
        file.GdcSerie = string.Empty;
        file.GptDescripcion = string.Empty;

        file.Resumen = string.Empty;
        file.RecorteAplicado = string.Empty;
        file.PaginasIncluidas = string.Empty;
        file.MarkdownGenerado = string.Empty;
        file.OrigenMarkdown = string.Empty;
        file.ModeloLlmUsado = string.Empty;
        file.ActividadActual = string.Empty;
        file.ActividadesCompletadas = string.Empty;
        file.ActividadesTotales = string.Empty;
        file.DuracionTotalMs = string.Empty;
        file.TimelineActividades = string.Empty;

        file.Proveedor = string.Empty;
        file.MotivoDescarte = string.Empty;
        file.ReutilizadaPorDuplicado = false;
        file.MensajeReutilizacion = string.Empty;
        file.DetalleProveedores.Clear();

        file.IsSelected = false;

        file.Status = "Pendiente";
    }
}
```

> **Nota:** `file.IsSelected` se añade en Task 2. Esta línea provoca error de compilación hasta que Task 2 esté hecha. Si se implementa en orden estricto, mueve **temporalmente** la línea `file.IsSelected = false;` a un comentario y actívala al llegar a Task 2. Si se ejecuta el plan completo antes de compilar, no hace falta.

- [ ] **Step 4: Ejecutar los tests y verificar que pasan**

Run: `cd /c/temp/MVP/DocumentIA.Batch && dotnet test tests/DocumentIA.Batch.Classification.Tests --filter "FullyQualifiedName~ClassificationReprocessPolicyTests"`
Expected: PASS (18 casos de `IsReprocessable` + 1 de `ResetForReprocess`).

> Si `IsSelected` aún no existe (Task 2 pendiente), este paso fallará al compilar. En ese caso ejecuta Task 2 antes de correr los tests, o comenta temporalmente la línea `file.IsSelected = false;`.

- [ ] **Step 5: Commit**

```bash
cd /c/temp/MVP/DocumentIA.Batch
git add src/DocumentIA.Batch.Classification/Services/ClassificationReprocessPolicy.cs tests/DocumentIA.Batch.Classification.Tests/ClassificationReprocessPolicyTests.cs
git commit -m "feat(batch-classification): politica de reproceso de documentos no completados (AB#99904)"
```

---

## Task 2: `IsSelected` en `ClassificationDocumentItem` + columna de checkbox (AB#99905)

**Files:**
- Modify: `src/DocumentIA.Batch.Classification/Models/ClassificationDocumentItem.cs`
- Modify: `src/DocumentIA.Batch.Classification/MainWindow.xaml:168-178`
- Test: `tests/DocumentIA.Batch.Classification.Tests/ClassificationReprocessPolicyTests.cs` (reutiliza el ya creado)

**Interfaces:**
- Produces: `ClassificationDocumentItem.IsSelected` (`bool`, con notificación de cambio vía `SetField`).
- La columna XAML consume `IsSelected` por fila y `DataContext.SelectAllFiles` (definido en Task 3) en la cabecera.

- [ ] **Step 1: Añadir el backing field**

En `src/DocumentIA.Batch.Classification/Models/ClassificationDocumentItem.cs`, junto al resto de campos privados (tras `private string _status = "Pendiente";`, línea 17):

```csharp
    private bool _isSelected;
```

- [ ] **Step 2: Añadir la propiedad**

En el mismo archivo, tras la propiedad `Status` (después de la línea 71, el cierre `}` de `Status`):

```csharp
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
```

- [ ] **Step 3: Compilar para verificar la propiedad**

Run: `cd /c/temp/MVP/DocumentIA.Batch && dotnet build src/DocumentIA.Batch.Classification/DocumentIA.Batch.Classification.csproj`
Expected: build correcto (si Task 1 ya introdujo `file.IsSelected = false;`, ahora compila).

- [ ] **Step 4: Añadir la columna de checkbox en el DataGrid**

En `src/DocumentIA.Batch.Classification/MainWindow.xaml`, dentro de `<DataGrid.Columns>` (línea 168), como **primera** columna antes de `Filename`:

```xml
                        <DataGridTemplateColumn Width="40" CanUserSort="False">
                            <DataGridTemplateColumn.HeaderTemplate>
                                <DataTemplate>
                                    <CheckBox IsThreeState="True"
                                              IsChecked="{Binding DataContext.SelectAllFiles, RelativeSource={RelativeSource AncestorType=Window}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                              HorizontalAlignment="Center"
                                              VerticalAlignment="Center"
                                              ToolTip="Seleccionar o deseleccionar todos los documentos" />
                                </DataTemplate>
                            </DataGridTemplateColumn.HeaderTemplate>
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate>
                                    <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                              HorizontalAlignment="Center"
                                              VerticalAlignment="Center"
                                              ToolTip="Incluir este documento en el reproceso" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
```

> El `DataGrid` tiene `IsReadOnly="True"`, pero un `CheckBox` dentro de `CellTemplate` (no `CellEditingTemplate`) sigue siendo interactivo — mismo patrón que `HistorialView` del batch de extracción.

- [ ] **Step 5: Compilar la app completa**

Run: `cd /c/temp/MVP/DocumentIA.Batch && dotnet build src/DocumentIA.Batch.Classification/DocumentIA.Batch.Classification.csproj`
Expected: build correcto. (El binding a `SelectAllFiles` no rompe la compilación aunque la propiedad aún no exista — se resuelve en runtime; se implementa en Task 3.)

- [ ] **Step 6: Commit**

```bash
cd /c/temp/MVP/DocumentIA.Batch
git add src/DocumentIA.Batch.Classification/Models/ClassificationDocumentItem.cs src/DocumentIA.Batch.Classification/MainWindow.xaml
git commit -m "feat(batch-classification): seleccion por checkbox de documentos en la grilla (AB#99905)"
```

---

## Task 3: `ReprocessCommand`, `SelectAllFiles` y refactor del pipeline (AB#99906)

**Files:**
- Modify: `src/DocumentIA.Batch.Classification/ViewModels/ClassificationMainViewModel.cs`
- Modify: `src/DocumentIA.Batch.Classification/MainWindow.xaml:88` (botón)

**Interfaces:**
- Consumes: `ClassificationReprocessPolicy.IsReprocessable/ResetForReprocess` (Task 1); `ClassificationDocumentItem.IsSelected` (Task 2).
- Produces:
  - `RelayCommand ReprocessCommand`
  - `bool? SelectAllFiles { get; set; }`
  - `Task ProcessFilesAsync(IReadOnlyList<ClassificationDocumentItem> filesToProcess, string operationName)`
  - `IReadOnlyList<ClassificationDocumentItem> ResolveReprocessCandidates()` (privado, pero probado por reflexión en Task 4)
  - `bool CanReprocess()` (privado, probado por reflexión en Task 4)

- [ ] **Step 1: Añadir campos de estado**

En `ClassificationMainViewModel.cs`, junto a los campos privados (tras `private string? _currentRunFolder;`, línea 41):

```csharp
    private bool _hasBatchRun;
    private bool? _selectAllFiles = false;
    private bool _isUpdatingSelection;
```

- [ ] **Step 2: Declarar el comando y suscribir la selección**

En la lista de propiedades de comando, tras `public RelayCommand StartProcessingCommand { get; }` (línea 115), añade:

```csharp
    public RelayCommand ReprocessCommand { get; }
```

En el constructor, tras la línea `StartProcessingCommand = new RelayCommand(_ => _ = StartProcessingAsync(), _ => CanProcess());` (línea 65):

```csharp
        ReprocessCommand = new RelayCommand(_ => _ = ReprocessAsync(), _ => CanReprocess());
```

En `File_PropertyChanged` (línea 98), amplía la condición para reaccionar también a `IsSelected`:

```csharp
    private void File_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClassificationDocumentItem.IsSelected))
        {
            RefreshSelectionState();
            return;
        }

        // Solo refrescar si cambia Status, RuntimeStatus o MensajeError (afectan los contadores)
        if (e.PropertyName == nameof(ClassificationDocumentItem.Status)
            || e.PropertyName == nameof(ClassificationDocumentItem.RuntimeStatus)
            || e.PropertyName == nameof(ClassificationDocumentItem.MensajeError))
        {
            RefreshCommandStates();
        }
    }
```

- [ ] **Step 3: Añadir la propiedad `SelectAllFiles` y helpers de selección**

Tras la propiedad `SelectedFile` (después de la línea 242), añade:

```csharp
    public bool? SelectAllFiles
    {
        get => _selectAllFiles;
        set
        {
            if (_selectAllFiles == value)
            {
                return;
            }

            _selectAllFiles = value;
            OnPropertyChanged();

            if (_isUpdatingSelection || value is null)
            {
                return;
            }

            SetAllSelection(value.Value);
        }
    }

    private void SetAllSelection(bool selected)
    {
        _isUpdatingSelection = true;
        try
        {
            foreach (var file in Files)
            {
                file.IsSelected = selected;
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }

        RefreshSelectionState();
    }

    private void RefreshSelectionState()
    {
        if (_isUpdatingSelection)
        {
            return;
        }

        var selectedCount = Files.Count(file => file.IsSelected);

        _isUpdatingSelection = true;
        try
        {
            SelectAllFiles = Files.Count switch
            {
                0 => false,
                _ when selectedCount == 0 => false,
                _ when selectedCount == Files.Count => true,
                _ => null
            };
        }
        finally
        {
            _isUpdatingSelection = false;
        }

        ReprocessCommand.RaiseCanExecuteChanged();
    }
```

- [ ] **Step 4: Añadir `CanReprocess`, `ResolveReprocessCandidates` y `ReprocessAsync`**

Tras el método `CanProcess()` (después de la línea 341, el cierre de `CanProcess`), añade:

```csharp
    private bool CanReprocess()
    {
        return !IsProcessing
            && !string.IsNullOrWhiteSpace(BackendUrl)
            && Files.Any(f => ClassificationReprocessPolicy.IsReprocessable(f, _hasBatchRun));
    }

    private IReadOnlyList<ClassificationDocumentItem> ResolveReprocessCandidates()
    {
        var marcados = Files.Where(f => f.IsSelected).ToList();
        if (marcados.Count > 0)
        {
            return marcados
                .Where(f => ClassificationReprocessPolicy.IsReprocessable(f, _hasBatchRun))
                .ToList();
        }

        return Files
            .Where(f => ClassificationReprocessPolicy.IsReprocessable(f, _hasBatchRun))
            .ToList();
    }

    private async Task ReprocessAsync()
    {
        var marcados = Files.Count(f => f.IsSelected);
        var candidatos = ResolveReprocessCandidates();

        if (candidatos.Count == 0)
        {
            ProcessStatus = marcados > 0
                ? "Ninguno de los documentos seleccionados es reprocesable."
                : "No hay documentos reprocesables.";
            return;
        }

        var omitidos = marcados > 0 ? marcados - candidatos.Count : 0;

        foreach (var file in candidatos)
        {
            ClassificationReprocessPolicy.ResetForReprocess(file);
        }

        RefreshSelectionState();

        var aviso = omitidos > 0
            ? $" ({omitidos} omitidos por no ser reprocesables)"
            : string.Empty;
        ProcessStatus = $"Reprocesando {candidatos.Count} documento(s){aviso}...";

        await ProcessFilesAsync(candidatos, "reproceso");
    }
```

- [ ] **Step 5: Refactorizar `StartProcessingAsync` en `ProcessFilesAsync`**

Reemplaza el método `StartProcessingAsync` completo (líneas 343-379) por estos dos métodos:

```csharp
    private async Task StartProcessingAsync()
    {
        var pendingFiles = Files
            .Where(file => string.Equals(file.Status, "Pendiente", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pendingFiles.Count == 0)
        {
            ProcessStatus = "No pending files to process.";
            return;
        }

        await ProcessFilesAsync(pendingFiles, "procesamiento");
    }

    private async Task ProcessFilesAsync(IReadOnlyList<ClassificationDocumentItem> filesToProcess, string operationName)
    {
        if (filesToProcess.Count == 0 || IsProcessing || string.IsNullOrWhiteSpace(BackendUrl))
        {
            return;
        }

        IsProcessing = true;
        _processingCts = new CancellationTokenSource();
        var cancellationToken = _processingCts.Token;
        var runFolder = _runStorageService.CreateRunFolder();
        _currentRunFolder = runFolder;
        var maxParallelism = Math.Clamp(NumeroColas, 1, 10);

        try
        {
            ProcessStatus = $"Processing batch with {maxParallelism} queue(s)...";
            using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);

            var tasks = filesToProcess.Select(file => ProcessFileAsync(file, runFolder, semaphore, cancellationToken));
            await Task.WhenAll(tasks);
            ProcessStatus = $"Batch completed. Successful: {CompletedFiles}. Errors: {ErrorFiles}.";
        }
        catch (OperationCanceledException)
        {
            ProcessStatus = "Processing cancelled by user.";
        }
        finally
        {
            IsProcessing = false;
            _hasBatchRun = true;
            _processingCts?.Dispose();
            _processingCts = null;
            RefreshCommandStates();
        }
    }
```

- [ ] **Step 6: Incluir `ReprocessCommand` en los refrescos de estado**

En `IsProcessing` setter, tras `StartProcessingCommand.RaiseCanExecuteChanged();` (línea 221), añade:

```csharp
                ReprocessCommand.RaiseCanExecuteChanged();
```

En `RefreshCommandStates()`, tras `StartProcessingCommand.RaiseCanExecuteChanged();` (línea 685), añade:

```csharp
        ReprocessCommand.RaiseCanExecuteChanged();
```

- [ ] **Step 7: Añadir el botón "Reprocesar" en el XAML**

En `src/DocumentIA.Batch.Classification/MainWindow.xaml`, tras el botón "▶ Start Classification" (línea 87), añade:

```xml
                    <Button Content="🔁 Reprocesar" Command="{Binding ReprocessCommand}" Margin="0,0,0,4" Padding="8" />
```

- [ ] **Step 8: Compilar**

Run: `cd /c/temp/MVP/DocumentIA.Batch && dotnet build DocumentIA.Batch.sln`
Expected: build correcto.

- [ ] **Step 9: Commit**

```bash
cd /c/temp/MVP/DocumentIA.Batch
git add src/DocumentIA.Batch.Classification/ViewModels/ClassificationMainViewModel.cs src/DocumentIA.Batch.Classification/MainWindow.xaml
git commit -m "feat(batch-classification): comando y boton de reproceso de documentos no completados (AB#99906)"
```

---

## Task 4: Tests de ViewModel para reproceso (AB#99907)

Valida la resolución de candidatos (marcados vs. todos, omisión de no elegibles) y `CanReprocess` (deshabilitado antes del primer lote cuando solo hay `Pendiente`). Se usa reflexión sobre miembros privados, igual que `ClassificationMainViewModelSummaryTests`.

**Files:**
- Test: `tests/DocumentIA.Batch.Classification.Tests/ClassificationMainViewModelReprocessTests.cs`

**Interfaces:**
- Consumes: `ClassificationMainViewModel` (`Files`, `BackendUrl`); miembros privados `_hasBatchRun`, `CanReprocess()`, `ResolveReprocessCandidates()` vía reflexión.

- [ ] **Step 1: Escribir los tests**

Create `tests/DocumentIA.Batch.Classification.Tests/ClassificationMainViewModelReprocessTests.cs`:

```csharp
using System.Collections.Generic;
using System.Reflection;
using DocumentIA.Batch.Classification.Models;
using DocumentIA.Batch.Classification.ViewModels;
using Xunit;

namespace DocumentIA.Batch.Classification.Tests;

public class ClassificationMainViewModelReprocessTests
{
    private static ClassificationMainViewModel NewVm(bool hasBatchRun)
    {
        var vm = new ClassificationMainViewModel();
        vm.BackendUrl = "http://localhost:7071";
        SetPrivateField(vm, "_hasBatchRun", hasBatchRun);
        return vm;
    }

    [Fact]
    public void ResolveReprocessCandidates_SinMarcas_DevuelveTodosLosElegibles()
    {
        var vm = NewVm(hasBatchRun: true);
        vm.Files.Add(new ClassificationDocumentItem { FileName = "err.pdf", Status = "Error" });
        vm.Files.Add(new ClassificationDocumentItem { FileName = "ok.pdf", Status = "OK" });
        vm.Files.Add(new ClassificationDocumentItem { FileName = "pend.pdf", Status = "Pendiente" });

        var candidatos = InvokeResolve(vm);

        Assert.Equal(2, candidatos.Count);
        Assert.Contains(candidatos, f => f.FileName == "err.pdf");
        Assert.Contains(candidatos, f => f.FileName == "pend.pdf");
    }

    [Fact]
    public void ResolveReprocessCandidates_ConMarcas_SoloMarcadosElegibles()
    {
        var vm = NewVm(hasBatchRun: true);
        var err = new ClassificationDocumentItem { FileName = "err.pdf", Status = "Error", IsSelected = true };
        var ok = new ClassificationDocumentItem { FileName = "ok.pdf", Status = "OK", IsSelected = true };
        var errNoMarcado = new ClassificationDocumentItem { FileName = "err2.pdf", Status = "Error", IsSelected = false };
        vm.Files.Add(err);
        vm.Files.Add(ok);
        vm.Files.Add(errNoMarcado);

        var candidatos = InvokeResolve(vm);

        Assert.Single(candidatos);
        Assert.Equal("err.pdf", candidatos[0].FileName);
    }

    [Fact]
    public void CanReprocess_SoloPendientes_AntesDelPrimerLote_EsFalse()
    {
        var vm = NewVm(hasBatchRun: false);
        vm.Files.Add(new ClassificationDocumentItem { FileName = "pend.pdf", Status = "Pendiente" });

        Assert.False(InvokeCanReprocess(vm));
    }

    [Fact]
    public void CanReprocess_ConError_EsTrue_AunSinLotePrevio()
    {
        var vm = NewVm(hasBatchRun: false);
        vm.Files.Add(new ClassificationDocumentItem { FileName = "err.pdf", Status = "Error" });

        Assert.True(InvokeCanReprocess(vm));
    }

    private static IReadOnlyList<ClassificationDocumentItem> InvokeResolve(ClassificationMainViewModel vm)
    {
        var method = typeof(ClassificationMainViewModel).GetMethod(
            "ResolveReprocessCandidates",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return (IReadOnlyList<ClassificationDocumentItem>)method!.Invoke(vm, null)!;
    }

    private static bool InvokeCanReprocess(ClassificationMainViewModel vm)
    {
        var method = typeof(ClassificationMainViewModel).GetMethod(
            "CanReprocess",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return (bool)method!.Invoke(vm, null)!;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
```

- [ ] **Step 2: Ejecutar los tests y verificar que pasan**

Run: `cd /c/temp/MVP/DocumentIA.Batch && dotnet test tests/DocumentIA.Batch.Classification.Tests --filter "FullyQualifiedName~ClassificationMainViewModelReprocessTests"`
Expected: PASS (4 tests).

> El constructor de `ClassificationMainViewModel` llama a `LoadConfig()` y crea `SettingsService`/`DocumentIaBackendClient` reales, pero no ejecuta red ni requiere STA — mismo enfoque que `ClassificationMainViewModelSummaryTests`, que ya instancian el VM. Si el runner de test se quejara por hilo STA con `CollectionViewSource`, marca la clase con `[Collection("STA")]` no aplica; en su lugar, verifica que el test host es el mismo que ya usa la suite existente.

- [ ] **Step 3: Ejecutar la suite completa**

Run: `cd /c/temp/MVP/DocumentIA.Batch && dotnet test DocumentIA.Batch.sln`
Expected: PASS de todos los tests (incluye los existentes + Task 1 + Task 4).

- [ ] **Step 4: Commit**

```bash
cd /c/temp/MVP/DocumentIA.Batch
git add tests/DocumentIA.Batch.Classification.Tests/ClassificationMainViewModelReprocessTests.cs
git commit -m "test(batch-classification): tests de VM para reproceso de documentos no completados (AB#99907)"
```

---

## Verificación final

- [ ] `dotnet build DocumentIA.Batch.sln` correcto.
- [ ] `dotnet test DocumentIA.Batch.sln` verde.
- [ ] Smoke manual (opcional, requiere backend): cargar PDFs, procesar, provocar un error o cancelar a mitad, comprobar que "Reprocesar" se habilita y relanza los no completados; verificar selección por checkbox y "seleccionar todos".
- [ ] Actualizar estado de las tasks AB#99904–99907 y del PBI AB#99903 en Azure DevOps.
