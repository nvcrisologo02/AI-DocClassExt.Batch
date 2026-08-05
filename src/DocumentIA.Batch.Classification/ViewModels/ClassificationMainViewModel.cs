using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using DocumentIA.Batch.Classification.Models;
using DocumentIA.Batch.Classification.Services;
using DocumentIA.Batch.Models;
using DocumentIA.Batch.Services;
using DocumentIA.Batch.ViewModels;
using Microsoft.Win32;

namespace DocumentIA.Batch.Classification.ViewModels;

public class ClassificationMainViewModel : ObservableObject
{
    /// <summary>Etiqueta de este ejecutable dentro de trazabilidad.submittedBy.</summary>
    public const string ProgramaSolicitante = "DocumentIA.Batch.Classification";

    private readonly SettingsService _settingsService;
    private readonly DocumentIaBackendClient _backendClient;
    private readonly BatchRunStorageService _runStorageService;
    private readonly ClassificationExportService _exportService;
    private readonly BatchOutputAuditExtractor _auditExtractor;

    private string _backendUrl = string.Empty;
    private string _functionKey = string.Empty;
    private int _numeroColas = 4;
    private bool _forceReprocess;
    private string _solicitante = ProgramaSolicitante;
    private bool _classificationOnly;
    private bool _ejecutarIntegridad;
    private bool _forzarResumenPorDefecto = true;
    private string _classificationProvider = "auto";
    private string _classificationModel = "auto";
    private string _classificationLevelOption = "DEFAULT";
    private int _maxPagesForClassificationOnly = 10;
    private bool _isProcessing;
    private string _processStatus = "Ready";
    private ClassificationDocumentItem? _selectedFile;
    private CancellationTokenSource? _processingCts;
    private string? _currentRunFolder;
    private bool _hasBatchRun;
    private bool? _selectAllFiles = false;
    private bool _isUpdatingSelection;

    public ClassificationMainViewModel()
        : this(new SettingsService(), new DocumentIaBackendClient(), new BatchRunStorageService(), new ClassificationExportService(), new BatchOutputAuditExtractor())
    {
    }

    public ClassificationMainViewModel(
        SettingsService settingsService,
        DocumentIaBackendClient backendClient,
        BatchRunStorageService runStorageService,
        ClassificationExportService exportService,
        BatchOutputAuditExtractor auditExtractor)
    {
        _settingsService = settingsService;
        _backendClient = backendClient;
        _runStorageService = runStorageService;
        _exportService = exportService;
        _auditExtractor = auditExtractor;

        Files = new ObservableCollection<ClassificationDocumentItem>();
        FilesView = CollectionViewSource.GetDefaultView(Files);

        PickFilesCommand = new RelayCommand(_ => PickFiles(), _ => !IsProcessing);
        StartProcessingCommand = new RelayCommand(_ => _ = StartProcessingAsync(), _ => CanProcess());
        ReprocessCommand = new RelayCommand(_ => _ = ReprocessAsync(), _ => CanReprocess());
        CancelProcessingCommand = new RelayCommand(_ => CancelProcessing(), _ => IsProcessing);
        ClearBatchCommand = new RelayCommand(_ => ClearBatch(), _ => CanClearBatch());
        SaveConfigCommand = new RelayCommand(_ => SaveConfig());
        ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => Files.Count > 0 && !IsProcessing);
        ExportExcelCommand = new RelayCommand(_ => ExportExcel(), _ => Files.Count > 0 && !IsProcessing);
        ExportSimplifiedExcelCommand = new RelayCommand(_ => ExportSimplifiedExcel(), _ => Files.Count > 0 && !IsProcessing);

        Files.CollectionChanged += Files_CollectionChanged;

        LoadConfig();
        _ = InicializarSolicitanteAsync();
    }

    /// <summary>
    /// Resuelve el solicitante fuera del hilo de UI: la consulta del UPN puede necesitar el
    /// controlador de dominio y no debe bloquear el arranque.
    /// </summary>
    private async Task InicializarSolicitanteAsync()
    {
        Solicitante = await Task.Run(() => SolicitanteProvider.ObtenerSolicitante(ProgramaSolicitante));
    }

    // Suscribe a PropertyChanged de cada item para refrescar contadores al cambiar estado
    private void Files_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (ClassificationDocumentItem item in e.OldItems)
            {
                item.PropertyChanged -= File_PropertyChanged;
            }
        }
        if (e.NewItems != null)
        {
            foreach (ClassificationDocumentItem item in e.NewItems)
            {
                item.PropertyChanged += File_PropertyChanged;
            }
        }
        RefreshCommandStates();
    }

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

    public ObservableCollection<ClassificationDocumentItem> Files { get; }

    public ICollectionView FilesView { get; }

    public RelayCommand PickFilesCommand { get; }

    public RelayCommand StartProcessingCommand { get; }

    public RelayCommand ReprocessCommand { get; }

    public RelayCommand CancelProcessingCommand { get; }

    public RelayCommand ClearBatchCommand { get; }

    public RelayCommand SaveConfigCommand { get; }

    public RelayCommand ExportCsvCommand { get; }

    public RelayCommand ExportExcelCommand { get; }

    public RelayCommand ExportSimplifiedExcelCommand { get; }

    public string BackendUrl
    {
        get => _backendUrl;
        set => SetProperty(ref _backendUrl, value);
    }

    public string FunctionKey
    {
        get => _functionKey;
        set => SetProperty(ref _functionKey, value);
    }

    public int NumeroColas
    {
        get => _numeroColas;
        set => SetProperty(ref _numeroColas, value);
    }

    public bool ClassificationOnly
    {
        get => _classificationOnly;
        set => SetProperty(ref _classificationOnly, value);
    }

    public bool ForceReprocess
    {
        get => _forceReprocess;
        set => SetProperty(ref _forceReprocess, value);
    }

    /// <summary>
    /// Valor que viaja en trazabilidad.submittedBy. Se precarga con "programa/usuario" y es
    /// editable; si se deja vacío se restaura el valor calculado. No se persiste en la
    /// configuración: se recalcula en cada arranque para que no quede pegado a otro usuario.
    /// </summary>
    public string Solicitante
    {
        get => _solicitante;
        set
        {
            var normalizado = SolicitanteProvider.NormalizarEdicion(ProgramaSolicitante, value);

            if (!SetProperty(ref _solicitante, normalizado) &&
                !string.Equals(value, normalizado, StringComparison.Ordinal))
            {
                // El texto entrante se ha normalizado hasta coincidir con el valor actual:
                // hay que notificar igualmente para que el cuadro recupere lo que se enviará.
                OnPropertyChanged();
            }
        }
    }

    public bool EjecutarIntegridad
    {
        get => _ejecutarIntegridad;
        set => SetProperty(ref _ejecutarIntegridad, value);
    }

    public bool ForzarResumenPorDefecto
    {
        get => _forzarResumenPorDefecto;
        set => SetProperty(ref _forzarResumenPorDefecto, value);
    }

    public IReadOnlyList<string> ClassificationModeOptions { get; } =
    [
        "auto",
        "hybrid",
        "hybrid-rules-gpt-di",
        "hybrid-rules-di-gpt",
        "hybrid-tdn",
        "rules",
        "gpt",
        "di"
    ];

    public IReadOnlyList<string> ClassificationLevelOptions { get; } =
    [
        "DEFAULT",
        "TDN1",
        "TDN1_TDN2"
    ];

    public string ClassificationProvider
    {
        get => _classificationProvider;
        set => SetProperty(ref _classificationProvider, string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim());
    }

    public string ClassificationModel
    {
        get => _classificationModel;
        set => SetProperty(ref _classificationModel, string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim());
    }

    public string ClassificationLevelOption
    {
        get => _classificationLevelOption;
        set => SetProperty(ref _classificationLevelOption, string.IsNullOrWhiteSpace(value) ? "DEFAULT" : value.Trim().ToUpperInvariant());
    }

    public int MaxPagesForClassificationOnly
    {
        get => _maxPagesForClassificationOnly;
        set => SetProperty(ref _maxPagesForClassificationOnly, Math.Max(0, value));
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        private set
        {
            if (SetProperty(ref _isProcessing, value))
            {
                StartProcessingCommand.RaiseCanExecuteChanged();
                ReprocessCommand.RaiseCanExecuteChanged();
                CancelProcessingCommand.RaiseCanExecuteChanged();
                ClearBatchCommand.RaiseCanExecuteChanged();
                PickFilesCommand.RaiseCanExecuteChanged();
                ExportCsvCommand.RaiseCanExecuteChanged();
                ExportExcelCommand.RaiseCanExecuteChanged();
                ExportSimplifiedExcelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProcessStatus
    {
        get => _processStatus;
        private set => SetProperty(ref _processStatus, value);
    }

    public ClassificationDocumentItem? SelectedFile
    {
        get => _selectedFile;
        set => SetProperty(ref _selectedFile, value);
    }

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

    public int TotalFiles => Files.Count;

    public int RunningFiles => Files.Count(IsRunningFile);

    public int CompletedFiles => Files.Count(IsCompletedFile);

    public int ErrorFiles => Files.Count(IsErrorFile);

    private void LoadConfig()
    {
        var config = _settingsService.Load();
        BackendUrl = config.BackendUrl;
        FunctionKey = config.FunctionKey;
        NumeroColas = config.NumeroColas;
        ForceReprocess = config.ForceReprocess;
        ClassificationOnly = config.ClassificationOnly;
        EjecutarIntegridad = config.EjecutarIntegridad;
        ForzarResumenPorDefecto = config.ForzarResumenPorDefecto;
        ClassificationProvider = string.IsNullOrWhiteSpace(config.ClassificationProvider) ? "auto" : config.ClassificationProvider;
        ClassificationModel = string.IsNullOrWhiteSpace(config.ClassificationModel) ? "auto" : config.ClassificationModel;
        ClassificationLevelOption = string.IsNullOrWhiteSpace(config.ClassificationLevel) ? "DEFAULT" : config.ClassificationLevel;
        MaxPagesForClassificationOnly = config.MaxPagesForClassificationOnly;
    }

    private void SaveConfig()
    {
        var config = _settingsService.Load();
        config.BackendUrl = BackendUrl;
        config.FunctionKey = FunctionKey;
        config.NumeroColas = NumeroColas;
        config.ForceReprocess = ForceReprocess;
        config.ClassificationOnly = ClassificationOnly;
        config.EjecutarIntegridad = EjecutarIntegridad;
        config.ForzarResumenPorDefecto = ForzarResumenPorDefecto;
        config.ClassificationProvider = ClassificationProvider;
        config.ClassificationModel = ClassificationModel;
        config.ClassificationLevel = ResolveClassificationLevelForRequest() ?? string.Empty;
        config.MaxPagesForClassificationOnly = MaxPagesForClassificationOnly;
        _settingsService.Save(config);
        ProcessStatus = "Configuration saved.";
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        ClassificationDocumentItem? firstAdded = null;

        foreach (var path in paths)
        {
            if (!File.Exists(path) || !path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Files.Any(x => string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var fileInfo = new FileInfo(path);
            var item = new ClassificationDocumentItem
            {
                FileName = fileInfo.Name,
                FullPath = fileInfo.FullName
            };

            Files.Add(item);
            firstAdded ??= item;
        }

        if (SelectedFile is null)
        {
            SelectedFile = firstAdded ?? Files.FirstOrDefault();
        }

        RefreshCommandStates();
    }

    private void PickFiles()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Multiselect = true,
            Title = "Select PDF files for classification"
        };

        if (dialog.ShowDialog() == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    private bool CanProcess()
    {
        return !IsProcessing
            && Files.Any()
            && !string.IsNullOrWhiteSpace(BackendUrl);
    }

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

        await ProcessFilesAsync(candidatos, "reproceso");

        if (omitidos > 0)
        {
            ProcessStatus += $" ({omitidos} omitidos por no ser reprocesables)";
        }
    }

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
            ProcessStatus = $"Ejecutando {operationName} de {filesToProcess.Count} documento(s) con {maxParallelism} cola(s)...";
            using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);

            var tasks = filesToProcess.Select(file => ProcessFileAsync(file, runFolder, semaphore, cancellationToken));
            await Task.WhenAll(tasks);
            ProcessStatus = $"Lote de {operationName} finalizado. Correctos: {CompletedFiles}. Errores: {ErrorFiles}.";
        }
        catch (OperationCanceledException)
        {
            ProcessStatus = $"{operationName} cancelado por el usuario.";
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

    private async Task ProcessFileAsync(
        ClassificationDocumentItem file,
        string runFolder,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            file.Status = "En cola";
            file.CorrelationId = Guid.NewGuid().ToString();
            file.FechaInicio = DateTime.Now;
            file.Status = "Enviando";

            var request = BuildIngestRequest(file, file.CorrelationId);
            var ingestResponse = await _backendClient.IngestAsync(BackendUrl, FunctionKey, request, cancellationToken);
            file.InstanceId = ingestResponse.InstanceId;
            file.Status = "Processing";

            file.StatusQueryUri = ingestResponse.StatusQueryUri;
            var finalStatus = await WaitForFinalStatusAsync(file, ingestResponse.StatusQueryUri, cancellationToken);
            file.RuntimeStatus = finalStatus.RuntimeStatus;

            if (string.Equals(finalStatus.RuntimeStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                var output = finalStatus.Output;
                if (output.HasValue)
                {
                    file.OutputJsonPath = _runStorageService.SaveOutputJson(runFolder, MapToOutputFile(file), output.Value);
                }

                var audit = !string.IsNullOrWhiteSpace(file.OutputJsonPath)
                    ? _auditExtractor.Extract(file.OutputJsonPath)
                    : BatchOutputAuditColumns.Empty("NoOutput", string.Empty);

                file.IdentificacionDocumento = audit.IdentificacionTipoDocumento;
                file.TipologiaIdentificada = audit.IdentificacionTipologiaDetectada;
                file.ConfianzaGlobal = audit.ResultadoConfianzaGlobal;
                file.Status = string.IsNullOrWhiteSpace(audit.ResultadoEstadoCalidad) ? "OK" : audit.ResultadoEstadoCalidad;

                if (output.HasValue)
                {
                    ApplySummaryFromOutput(file, output.Value);
                }
            }
            else
            {
                file.Status = "Error";
                file.MensajeError = finalStatus.RuntimeStatus;
            }
        }
        catch (OperationCanceledException)
        {
            file.Status = "Cancelled";
            throw;
        }
        catch (Exception ex)
        {
            file.Status = "Error";
            file.MensajeError = ex.Message;
        }
        finally
        {
            file.FechaFin = DateTime.Now;
            semaphore.Release();
            await RunOnUiAsync(() => FilesView.Refresh());
        }
    }

    private IngestRequest BuildIngestRequest(ClassificationDocumentItem file, string correlationId)
    {
        var effectiveClassificationOnly = ClassificationOnly || IsTdn1LevelSelected();

        return new IngestRequest
        {
            Instrucciones = new IngestInstrucciones
            {
                ExpectedType = string.Empty,
                ClassificationOnly = effectiveClassificationOnly,
                ExecuteIntegrarWhenClassificationOnly = effectiveClassificationOnly ? EjecutarIntegridad : null,
                MaxPagesForClassificationOnly = effectiveClassificationOnly ? MaxPagesForClassificationOnly : 0,
                ForzarResumenPorDefecto = ForzarResumenPorDefecto,
                SkipDuplicateCheck = false,
                ForceReprocess = ForceReprocess,
                SkipGdcUpload = true,
                Classification = new IngestIaConfig
                {
                    Provider = ClassificationProvider,
                    Model = ClassificationModel,
                    NivelClasificacion = ResolveClassificationLevelForRequest()
                },
                Extraction = new IngestIaConfig
                {
                    Provider = "auto",
                    Model = "auto"
                }
            },
            Documento = new IngestDocumento
            {
                Name = file.FileName,
                Content = new IngestDocumentoContent
                {
                    Base64 = Convert.ToBase64String(File.ReadAllBytes(file.FullPath))
                }
            },
            Trazabilidad = new IngestTrazabilidad
            {
                CorrelationId = correlationId,
                SubmittedBy = Solicitante
            }
        };
    }

    private string? ResolveClassificationLevelForRequest()
    {
        return string.Equals(ClassificationLevelOption, "DEFAULT", StringComparison.OrdinalIgnoreCase)
            ? null
            : ClassificationLevelOption;
    }

    private bool IsTdn1LevelSelected()
    {
        return string.Equals(ClassificationLevelOption, "TDN1", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<DurableStatusResponse> WaitForFinalStatusAsync(ClassificationDocumentItem file, string statusQueryUri, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 180; attempt++)
        {
            var status = await _backendClient.GetDurableStatusAsync(statusQueryUri, FunctionKey, cancellationToken);

            await RunOnUiAsync(() => ApplyLiveStatus(file, status));

            if (string.Equals(status.RuntimeStatus, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status.RuntimeStatus, "Failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status.RuntimeStatus, "Terminated", StringComparison.OrdinalIgnoreCase))
            {
                return status;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException("Timeout waiting for orchestration final state.");
    }

    private void CancelProcessing()
    {
        if (!IsProcessing || _processingCts is null)
        {
            return;
        }

        _processingCts.Cancel();
    }

    private bool CanClearBatch()
    {
        return !IsProcessing && (Files.Count > 0 || !string.IsNullOrWhiteSpace(_currentRunFolder));
    }

    private void ClearBatch()
    {
        if (IsProcessing)
        {
            return;
        }

        var runFolder = _currentRunFolder;

        Files.Clear();
        SelectedFile = null;
        _currentRunFolder = null;
        _hasBatchRun = false;
        ProcessStatus = "Ready";

        if (!string.IsNullOrWhiteSpace(runFolder) && Directory.Exists(runFolder))
        {
            try
            {
                Directory.Delete(runFolder, recursive: true);
                ProcessStatus = "Resultados y artefactos del lote eliminados. Listo para un nuevo proceso.";
            }
            catch (Exception ex)
            {
                ProcessStatus = $"Lote limpiado, pero no se pudo borrar la carpeta de artefactos: {ex.Message}";
            }
        }

        RefreshCommandStates();
    }

    private void ExportCsv()
    {
        if (Files.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Classification to CSV",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"DocumentIA_Classification_{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _exportService.ExportCsv(dialog.FileName, Files.ToList());
        ProcessStatus = $"CSV exported: {dialog.FileName}";
    }

    private void ExportExcel()
    {
        if (Files.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Classification to Excel",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"DocumentIA_Classification_{DateTime.Now:yyyyMMdd-HHmmss}.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _exportService.ExportExcel(dialog.FileName, Files.ToList());
        ProcessStatus = $"Excel exported: {dialog.FileName}";
    }

    private void ExportSimplifiedExcel()
    {
        if (Files.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Simplified Classification to Excel",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"DocumentIA_Classification_Simple_{DateTime.Now:yyyyMMdd-HHmmss}.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _exportService.ExportSimplifiedExcel(dialog.FileName, Files.ToList());
        ProcessStatus = $"Simplified Excel exported: {dialog.FileName}";
    }

    private static BatchFileItem MapToOutputFile(ClassificationDocumentItem file)
    {
        return new BatchFileItem
        {
            FileName = file.FileName,
            FullPath = file.FullPath,
            SizeBytes = 0,
            Estado = file.Status,
            InstanceId = file.InstanceId,
            CorrelationId = file.CorrelationId,
            RuntimeStatus = file.RuntimeStatus,
            MensajeError = file.MensajeError,
            FechaInicio = file.FechaInicio,
            FechaFin = file.FechaFin,
            OutputJsonPath = file.OutputJsonPath
        };
    }

    private static string NormalizeTipologiaCode(string identificador)
    {
        if (string.IsNullOrWhiteSpace(identificador))
        {
            return string.Empty;
        }

        var parts = identificador.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0].Trim() : identificador.Trim();
    }

    private void RefreshCommandStates()
    {
        OnPropertyChanged(nameof(TotalFiles));
        OnPropertyChanged(nameof(RunningFiles));
        OnPropertyChanged(nameof(CompletedFiles));
        OnPropertyChanged(nameof(ErrorFiles));
        OnPropertyChanged(nameof(Files)); // Asegura que la vista de archivos se actualice correctamente.
        StartProcessingCommand.RaiseCanExecuteChanged();
        ReprocessCommand.RaiseCanExecuteChanged();
        ClearBatchCommand.RaiseCanExecuteChanged();
        ExportCsvCommand.RaiseCanExecuteChanged();
        ExportExcelCommand.RaiseCanExecuteChanged();
        ExportSimplifiedExcelCommand.RaiseCanExecuteChanged();
    }

    private static bool IsRunningFile(ClassificationDocumentItem file)
    {
        return string.Equals(file.Status, "En cola", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "Enviando", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "Processing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "En ejecución", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompletedFile(ClassificationDocumentItem file)
    {
        return string.Equals(file.Status, "OK", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "Completado", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "REVISION", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "VALIDACION_CON_ERRORES", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "BAJA_CONFIANZA", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsErrorFile(ClassificationDocumentItem file)
    {
        return string.Equals(file.Status, "Error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.RuntimeStatus, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.RuntimeStatus, "Terminated", StringComparison.OrdinalIgnoreCase);
    }

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private static void ApplySummaryFromOutput(ClassificationDocumentItem file, JsonElement output)
    {
        var identificacion = GetPropertyValue(output, "Identificacion", "identificacion");
        var resultado = GetPropertyValue(output, "Resultado", "resultado");
        var detalle = GetPropertyValue(output, "DetalleEjecucion", "detalleEjecucion");
        var clasificacion = detalle.HasValue ? GetPropertyValue(detalle.Value, "Clasificacion", "clasificacion") : null;
        var datosExtraidos = GetPropertyValue(output, "DatosExtraidos", "datosExtraidos");
        var seguimiento = detalle.HasValue ? GetPropertyValue(detalle.Value, "Seguimiento", "seguimiento") : null;
        var detalleProveedores = clasificacion.HasValue
            ? GetPropertyValue(clasificacion.Value, "DetalleProveedores", "detalleProveedores")
            : null;

        file.ResultadoEstado = GetStringValue(resultado, "Estado", "estado");
        file.IdentificacionDocumento = FirstNonEmpty(
            file.IdentificacionDocumento,
            GetStringValue(identificacion, "Documento", "documento"));
        file.IdentificacionGuid = GetStringValue(identificacion, "Guid", "guid");
        file.TipologiaIdentificada = GetStringValue(identificacion, "Tipologia", "tipologia");
        file.TipologiaFamilia = GetStringValue(identificacion, "TipologiaFamilia", "tipologiaFamilia");
        file.TipologiaVersion = GetStringValue(identificacion, "TipologiaVersion", "tipologiaVersion");
        file.FechaProceso = GetStringValue(identificacion, "FechaProceso", "fechaProceso");
        file.Paginas = GetStringValue(identificacion, "Paginas", "paginas");
        file.Tdn1 = GetStringValue(identificacion, "Tdn1", "tdn1");
        // Fallback a Clasificacion.Tdn2Detectado: en tipologías virtuales el backend informa
        // ahí el TDN2 elegido en Phase 2 aunque no exista tipología publicada que lo mapee.
        file.Tdn2 = FirstNonEmpty(
            GetStringValue(identificacion, "Tdn2", "tdn2"),
            GetStringValue(clasificacion, "Tdn2Detectado", "tdn2Detectado"));
        file.Matricula = GetStringValue(identificacion, "Matricula", "matricula");
        file.TipologiaNombre = GetStringValue(identificacion, "TipologiaNombre", "tipologiaNombre");
        file.TipologiaMgdcMatricula = GetStringValue(identificacion, "TipologiaMGDCMatricula", "tipologiaMGDCMatricula");
        file.GdcTipoDocumento = GetStringValue(identificacion, "GdcTipoDocumento", "gdcTipoDocumento");
        file.GdcSubtipoDocumento = GetStringValue(identificacion, "GdcSubtipoDocumento", "gdcSubtipoDocumento");
        file.GdcSerie = GetStringValue(identificacion, "GdcSerie", "gdcSerie");
        file.GptDescripcion = GetStringValue(identificacion, "GptDescripcion", "gptDescripcion");
        file.ClassificationOnlyOutput = GetStringValue(detalle, "ClassificationOnly", "classificationOnly");
        file.Clasificador = GetStringValue(clasificacion, "Clasificador", "clasificador", "Modelo", "modelo");
        file.ConfianzaGlobal = ChooseConfidence(file.ConfianzaGlobal, GetStringValue(clasificacion, "Confianza", "confianza"));
        file.FallbackLlm = GetStringValue(clasificacion, "FallbackLLM", "fallbackLLM");
        file.FallbackRazon = GetStringValue(clasificacion, "FallbackRazon", "fallbackRazon");
        file.RecorteAplicado = GetStringValue(detalle, "RecorteAplicado", "recorteAplicado");
        file.PaginasIncluidas = GetStringValue(detalle, "PaginasIncluidas", "paginasIncluidas");
        file.MarkdownGenerado = GetStringValue(detalle, "MarkdownGenerado", "markdownGenerado");
        file.OrigenMarkdown = GetStringValue(detalle, "OrigenMarkdown", "origenMarkdown");
        file.ModeloLlmUsado = GetStringValue(detalle, "ModeloLLMUsado", "modeloLLMUsado");
        file.ReutilizadaPorDuplicado = GetBooleanValue(resultado, "ReutilizadaPorDuplicado", "reutilizadaPorDuplicado");
        file.MensajeReutilizacion = GetStringValue(resultado, "MensajeReutilizacion", "mensajeReutilizacion");
        file.Resumen = FirstNonEmpty(
            GetStringValue(datosExtraidos, "Resumen", "resumen"),
            GetStringValue(clasificacion, "ResumenCombinado", "resumenCombinado", "Resumen", "resumen"));
        file.TimelineActividades = BuildTimelineText(seguimiento);
        file.JustificacionClasificacion = BuildClassificationJustification(output, seguimiento);

        file.DetalleProveedores.Clear();

        if (detalleProveedores.HasValue && detalleProveedores.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var proveedor in detalleProveedores.Value.EnumerateArray())
            {
                file.DetalleProveedores.Add(new PropuestaProveedor
                {
                    Proveedor = GetStringValue(proveedor, "Proveedor", "proveedor"),
                    Tipologia = GetStringValue(proveedor, "Tipologia", "tipologia"),
                    Confianza = GetDoubleValue(proveedor, "Confianza", "confianza"),
                    MotivoDescarte = GetStringValue(proveedor, "MotivoDescarte", "motivoDescarte")
                });
            }
        }

        if (file.DetalleProveedores.Any())
        {
            var detalles = string.Join("; ", file.DetalleProveedores.Select(dp => $"Proveedor: {dp.Proveedor}, Tipología: {dp.Tipologia}, Confianza: {dp.Confianza:P}, Motivo: {dp.MotivoDescarte}"));
            file.JustificacionClasificacion += $"\nDetalle de Proveedores: {detalles}";
        }
    }

    private static void ApplyLiveStatus(ClassificationDocumentItem file, DurableStatusResponse status)
    {
        var customStatus = status.CustomStatus;
        if (!customStatus.HasValue)
        {
            return;
        }

        file.ActividadActual = GetStringValue(customStatus, "actividadActual", "ActividadActual", "currentActivity");
        file.ActividadesCompletadas = GetCompletedActivitiesCount(customStatus).ToString();
        file.ActividadesTotales = GetStringValue(customStatus, "actividadesTotales", "ActividadesTotales", "totalActivities");
        file.DuracionTotalMs = GetStringValue(customStatus, "duracionTotalMs", "DuracionTotalMs", "elapsedMs");
        file.TimelineActividades = BuildTimelineText(customStatus);
    }

    private static string ChooseConfidence(string current, string candidate)
    {
        return string.IsNullOrWhiteSpace(candidate) ? current : candidate;
    }

    private static string BuildClassificationJustification(JsonElement output, JsonElement? seguimiento)
    {
        var detalle = GetPropertyValue(output, "DetalleEjecucion", "detalleEjecucion");
        var clasificacion = detalle.HasValue ? GetPropertyValue(detalle.Value, "Clasificacion", "clasificacion") : null;
        var identificacion = GetPropertyValue(output, "Identificacion", "identificacion");

        var lines = new List<string>();

        var tipologia = GetStringValue(identificacion, "Tipologia", "tipologia");
        var clasificador = GetStringValue(clasificacion, "Clasificador", "clasificador", "Modelo", "modelo");
        var proveedor = GetStringValue(clasificacion, "ProveedorClasif", "proveedorClasif");
        var confianza = GetStringValue(clasificacion, "Confianza", "confianza");
        var fallback = GetStringValue(clasificacion, "FallbackLLM", "fallbackLLM");
        var fallbackReason = GetStringValue(clasificacion, "FallbackRazon", "fallbackRazon");
        var classificationOnly = GetStringValue(detalle, "ClassificationOnly", "classificationOnly");
        var recorteAplicado = GetStringValue(detalle, "RecorteAplicado", "recorteAplicado");
        var paginasIncluidas = GetStringValue(detalle, "PaginasIncluidas", "paginasIncluidas");
        var totalPaginas = GetStringValue(identificacion, "Paginas", "paginas");
        var markdownGenerado = GetStringValue(detalle, "MarkdownGenerado", "markdownGenerado");
        var origenMarkdown = GetStringValue(detalle, "OrigenMarkdown", "origenMarkdown");
        var modeloLlm = GetStringValue(detalle, "ModeloLLMUsado", "modeloLLMUsado");

        lines.Add($"Tipología final: {tipologia}");
        lines.Add($"Clasificador final: {clasificador} | Proveedor final: {proveedor} | Confianza: {confianza}");
        lines.Add($"ClassificationOnly salida: {classificationOnly}");

        var clasificacionParcial = GetStringValue(clasificacion, "ClasificacionParcial", "clasificacionParcial");
        if (bool.TryParse(clasificacionParcial, out var esParcial) && esParcial)
        {
            var tdn2Detectado = FirstNonEmpty(
                GetStringValue(identificacion, "Tdn2", "tdn2"),
                GetStringValue(clasificacion, "Tdn2Detectado", "tdn2Detectado"));
            lines.Add(string.IsNullOrWhiteSpace(tdn2Detectado)
                ? "Clasificación parcial TDN1 (tipología virtual): sin TDN2 propuesto."
                : $"Clasificación parcial TDN1 (tipología virtual). TDN2 propuesto por Phase 2: {tdn2Detectado} (sin tipología publicada que lo mapee).");
        }

        if (string.Equals(clasificador, "RuleBasedTDN", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("Decisión: heurística aceptada (reglas superaron el umbral de confianza).");
        }
        else if (string.Equals(clasificador, "DocumentIntelligence", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("Decisión: heurística insuficiente; DI resolvió la clasificación con confianza suficiente.");
        }
        else if (string.Equals(clasificador, "FoundryRescue", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("Decisión: heurística y DI no resolvieron con calidad requerida; se usó rescate LLM.");
        }
        else if (string.Equals(clasificador, "expectedtype-input", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("Decisión: clasificación forzada por ExpectedType de entrada.");
        }

        if (bool.TryParse(fallback, out var fallbackEnabled) && fallbackEnabled)
        {
            lines.Add("Fallback activado: SI");
            lines.Add($"Motivo fallback: {DecodeFallbackReason(fallbackReason)}");
        }
        else if (!string.IsNullOrWhiteSpace(fallbackReason))
        {
            lines.Add("Fallback activado: NO");
            lines.Add($"Motivo reportado por pipeline: {DecodeFallbackReason(fallbackReason)}");
        }

        if (!string.IsNullOrWhiteSpace(recorteAplicado) || !string.IsNullOrWhiteSpace(paginasIncluidas))
        {
            lines.Add($"Recorte clasificación: aplicado={recorteAplicado} | páginas incluidas={paginasIncluidas} | total={totalPaginas}");
        }

        if (bool.TryParse(markdownGenerado, out var markdownFlag) && markdownFlag)
        {
            lines.Add($"Markdown generado: SI | origen={origenMarkdown}");
        }

        if (!string.IsNullOrWhiteSpace(modeloLlm))
        {
            lines.Add($"Modelo LLM usado: {modeloLlm}");
        }

        var mensajeClasificar = GetClasificarActivityMessage(seguimiento);
        if (!string.IsNullOrWhiteSpace(mensajeClasificar))
        {
            lines.Add($"Mensaje actividad Clasificar: {mensajeClasificar}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildTimelineText(JsonElement? seguimiento)
    {
        if (!seguimiento.HasValue)
        {
            return string.Empty;
        }

        var actividades = GetPropertyValue(seguimiento.Value, "Actividades", "actividades", "activityTimeline");
        if (!actividades.HasValue || actividades.Value.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var actividad in actividades.Value.EnumerateArray())
        {
            var nombre = GetStringValue(actividad, "Nombre", "nombre");
            var estado = GetStringValue(actividad, "Estado", "estado");
            var duracion = GetStringValue(actividad, "DuracionMs", "duracionMs");
            var fallback = GetStringValue(actividad, "FallbackActivado", "fallbackActivado");
            var fallbackRazon = GetStringValue(actividad, "FallbackRazon", "fallbackRazon");
            var mensaje = GetStringValue(actividad, "Mensaje", "mensaje");

            builder.Append("- ")
                .Append(nombre)
                .Append(" | estado=")
                .Append(estado)
                .Append(" | duracionMs=")
                .Append(duracion)
                .Append(" | fallback=")
                .Append(fallback)
                .AppendLine();

            if (!string.IsNullOrWhiteSpace(fallbackRazon))
            {
                builder.Append("  razon fallback: ")
                    .AppendLine(fallbackRazon);
            }

            if (!string.IsNullOrWhiteSpace(mensaje))
            {
                builder.Append("  mensaje: ")
                    .AppendLine(mensaje);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static int GetCompletedActivitiesCount(JsonElement? customStatus)
    {
        if (!customStatus.HasValue)
        {
            return 0;
        }

        var completed = GetPropertyValue(customStatus.Value, "actividadesCompletadas", "ActividadesCompletadas", "completedActivities");
        if (!completed.HasValue || completed.Value.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return completed.Value.GetArrayLength();
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string GetClasificarActivityMessage(JsonElement? seguimiento)
    {
        if (!seguimiento.HasValue)
        {
            return string.Empty;
        }

        var actividades = GetPropertyValue(seguimiento.Value, "Actividades", "actividades");
        if (!actividades.HasValue || actividades.Value.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var actividad in actividades.Value.EnumerateArray())
        {
            var nombre = GetStringValue(actividad, "Nombre", "nombre");
            if (!string.Equals(nombre, "Clasificar", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return GetStringValue(actividad, "Mensaje", "mensaje");
        }

        return string.Empty;
    }

    private static string DecodeFallbackReason(string fallbackReason)
    {
        if (string.IsNullOrWhiteSpace(fallbackReason))
        {
            return "Sin fallback explícito";
        }

        if (fallbackReason.StartsWith("low_confidence:", StringComparison.OrdinalIgnoreCase))
        {
            return "DI devolvió baja confianza y se activó fallback al siguiente clasificador";
        }

        if (fallbackReason.StartsWith("resto_classification:", StringComparison.OrdinalIgnoreCase))
        {
            return "DI clasificó como RESTO, por lo que se forzó fallback";
        }

        if (fallbackReason.StartsWith("exception:", StringComparison.OrdinalIgnoreCase))
        {
            return "La clasificación principal lanzó excepción y se activó fallback";
        }

        if (fallbackReason.StartsWith("fallback_attempt_failed:", StringComparison.OrdinalIgnoreCase))
        {
            return "Se intentó fallback pero falló; se mantuvo el resultado anterior";
        }

        if (string.Equals(fallbackReason, "fallback_unclassified", StringComparison.OrdinalIgnoreCase))
        {
            return "No se logró clasificar de forma concluyente";
        }

        if (string.Equals(fallbackReason, "Tipologia Virtual", StringComparison.OrdinalIgnoreCase))
        {
            return "Tipología virtual TDN1: el TDN2 elegido no tiene tipología publicada que lo mapee";
        }

        if (string.Equals(fallbackReason, "fase2_parsing_error", StringComparison.OrdinalIgnoreCase))
        {
            return "Phase 2 no devolvió un TDN2 parseable; se conservó el TDN1 como tipología virtual";
        }

        return $"Fallback informado por el pipeline: {fallbackReason}";
    }

    private static JsonElement? GetPropertyValue(JsonElement source, params string[] names)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            foreach (var property in source.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value;
                }
            }
        }

        return null;
    }

    private static string GetStringValue(JsonElement? source, params string[] names)
    {
        if (!source.HasValue)
        {
            return string.Empty;
        }

        if (source.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                foreach (var property in source.Value.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return JsonElementToString(property.Value);
                    }
                }
            }
        }

        return string.Empty;
    }

    private static string GetStringValue(JsonElement source, params string[] names)
    {
        return GetStringValue((JsonElement?)source, names);
    }

    private static bool GetBooleanValue(JsonElement? source, params string[] names)
    {
        if (!source.HasValue || source.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var name in names)
        {
            foreach (var property in source.Value.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return property.Value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String when bool.TryParse(property.Value.GetString(), out var parsed) => parsed,
                    _ => false
                };
            }
        }

        return false;
    }

    private static double GetDoubleValue(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value))
            {
                return value;
            }
        }
        return 0.0; // Valor predeterminado si no se encuentra
    }

    private static string JsonElementToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            _ => value.ToString()
        };
    }
}