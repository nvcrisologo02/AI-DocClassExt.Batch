using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
    private readonly SettingsService _settingsService;
    private readonly DocumentIaBackendClient _backendClient;
    private readonly BatchRunStorageService _runStorageService;
    private readonly ClassificationExportService _exportService;
    private readonly BatchOutputAuditExtractor _auditExtractor;

    private string _backendUrl = string.Empty;
    private string _functionKey = string.Empty;
    private int _numeroColas = 4;
    private bool _forceReprocess;
    private bool _classificationOnly;
    private bool _ejecutarIntegridad;
    private bool _isProcessing;
    private string _processStatus = "Ready";
    private CancellationTokenSource? _processingCts;

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
        CancelProcessingCommand = new RelayCommand(_ => CancelProcessing(), _ => IsProcessing);
        SaveConfigCommand = new RelayCommand(_ => SaveConfig());
        ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => Files.Count > 0 && !IsProcessing);
        ExportExcelCommand = new RelayCommand(_ => ExportExcel(), _ => Files.Count > 0 && !IsProcessing);

        Files.CollectionChanged += (_, _) => RefreshCommandStates();

        LoadConfig();
    }

    public ObservableCollection<ClassificationDocumentItem> Files { get; }

    public ICollectionView FilesView { get; }

    public RelayCommand PickFilesCommand { get; }

    public RelayCommand StartProcessingCommand { get; }

    public RelayCommand CancelProcessingCommand { get; }

    public RelayCommand SaveConfigCommand { get; }

    public RelayCommand ExportCsvCommand { get; }

    public RelayCommand ExportExcelCommand { get; }

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

    public bool EjecutarIntegridad
    {
        get => _ejecutarIntegridad;
        set => SetProperty(ref _ejecutarIntegridad, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        private set
        {
            if (SetProperty(ref _isProcessing, value))
            {
                StartProcessingCommand.RaiseCanExecuteChanged();
                CancelProcessingCommand.RaiseCanExecuteChanged();
                PickFilesCommand.RaiseCanExecuteChanged();
                ExportCsvCommand.RaiseCanExecuteChanged();
                ExportExcelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProcessStatus
    {
        get => _processStatus;
        private set => SetProperty(ref _processStatus, value);
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
        _settingsService.Save(config);
        ProcessStatus = "Configuration saved.";
    }

    public void AddFiles(IEnumerable<string> paths)
    {
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
            Files.Add(new ClassificationDocumentItem
            {
                FileName = fileInfo.Name,
                FullPath = fileInfo.FullName
            });
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

    private async Task StartProcessingAsync()
    {
        var pendingFiles = Files.Where(file => string.Equals(file.Status, "Pendiente", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pendingFiles.Count == 0)
        {
            ProcessStatus = "No pending files to process.";
            return;
        }

        IsProcessing = true;
        _processingCts = new CancellationTokenSource();
        var cancellationToken = _processingCts.Token;
        var runFolder = _runStorageService.CreateRunFolder();
        var maxParallelism = Math.Clamp(NumeroColas, 1, 10);

        try
        {
            ProcessStatus = $"Processing batch with {maxParallelism} queue(s)...";
            using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);

            var tasks = pendingFiles.Select(file => ProcessFileAsync(file, runFolder, semaphore, cancellationToken));
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

            var finalStatus = await WaitForFinalStatusAsync(ingestResponse.StatusQueryUri, cancellationToken);
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
                file.Status = string.IsNullOrWhiteSpace(audit.ResultadoEstadoCalidad) ? "Completed" : audit.ResultadoEstadoCalidad;
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
        return new IngestRequest
        {
            Instrucciones = new IngestInstrucciones
            {
                ExpectedType = string.Empty,
                ClassificationOnly = ClassificationOnly,
                ExecuteIntegrarWhenClassificationOnly = ClassificationOnly ? EjecutarIntegridad : null,
                SkipDuplicateCheck = false,
                ForceReprocess = ForceReprocess,
                SkipGdcUpload = true,
                Classification = new IngestIaConfig
                {
                    Provider = "auto",
                    Model = "auto"
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
                SubmittedBy = "DocumentIA.Batch.Classification"
            }
        };
    }

    private async Task<DurableStatusResponse> WaitForFinalStatusAsync(string statusQueryUri, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 180; attempt++)
        {
            var status = await _backendClient.GetDurableStatusAsync(statusQueryUri, FunctionKey, cancellationToken);

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
        StartProcessingCommand.RaiseCanExecuteChanged();
        ExportCsvCommand.RaiseCanExecuteChanged();
        ExportExcelCommand.RaiseCanExecuteChanged();
    }

    private static bool IsRunningFile(ClassificationDocumentItem file)
    {
        return string.Equals(file.Status, "En cola", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "Enviando", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "En ejecución", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompletedFile(ClassificationDocumentItem file)
    {
        return string.Equals(file.Status, "Completado", StringComparison.OrdinalIgnoreCase)
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
}