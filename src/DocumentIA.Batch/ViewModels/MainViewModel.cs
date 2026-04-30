using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using DocumentIA.Batch.Models;
using DocumentIA.Batch.Services;
using DocumentIA.Batch.Views;
using Microsoft.Win32;

namespace DocumentIA.Batch.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly DocumentIaBackendClient _backendClient;
    private readonly BatchRunStorageService _runStorageService;
    private readonly BatchCsvExportService _csvExportService;
    private readonly BatchExcelExportService _excelExportService;

    private string _backendUrl = string.Empty;
    private string _functionKey = string.Empty;
    private TipologiaOption? _selectedTipologia;
    private bool _promptingEnabled;
    private int _umbralConfianza;
    private int _numeroColas;
    private bool _ejecutarConAssetResolver;
    private bool _subirAGdc;
    private bool _isProcessing;
    private string _processStatus = "Sin ejecuciones";
    private BatchRunSummary? _lastRunSummary;
    private CancellationTokenSource? _processingCts;
    private Dictionary<string, PromptOverride> _promptOverrides = new(StringComparer.OrdinalIgnoreCase);

    public MainViewModel() : this(new SettingsService(), new DocumentIaBackendClient(), new BatchRunStorageService(), new BatchCsvExportService(), new BatchExcelExportService())
    {
    }

    public MainViewModel(
        SettingsService settingsService,
        DocumentIaBackendClient backendClient,
        BatchRunStorageService runStorageService,
        BatchCsvExportService csvExportService,
        BatchExcelExportService excelExportService)
    {
        _settingsService = settingsService;
        _backendClient = backendClient;
        _runStorageService = runStorageService;
        _csvExportService = csvExportService;
        _excelExportService = excelExportService;

        Files = new ObservableCollection<BatchFileItem>();
        AvailableTipologias = new ObservableCollection<TipologiaOption>(
            new[]
            {
                new TipologiaOption { Code = "nota.simple.1_0" },
                new TipologiaOption { Code = "nota.simple.1_2" },
                new TipologiaOption { Code = "nota.simple.1_3" },
                new TipologiaOption { Code = "nota.simple.1_4" }
            });

        RemoveFileCommand = new RelayCommand(RemoveFile, _ => !IsProcessing);
        PickFilesCommand = new RelayCommand(_ => PickFiles(), _ => !IsProcessing);
        SaveConfigCommand = new RelayCommand(_ => SaveConfig());
        EditPromptCommand = new RelayCommand(_ => EditPrompt(), _ => SelectedTipologia is not null);
        RefreshTipologiasCommand = new RelayCommand(_ => _ = RefreshTipologiasAsync(), _ => !IsProcessing && !string.IsNullOrWhiteSpace(BackendUrl));
        StartProcessingCommand = new RelayCommand(_ => _ = StartProcessingAsync(), _ => CanProcess());
        CancelProcessingCommand = new RelayCommand(_ => CancelProcessing(), _ => IsProcessing);
        RetryFailedCommand = new RelayCommand(_ => _ = RetryFailedAsync(), _ => CanRetryFailed());
        RetryFileCommand = new RelayCommand(_ => _ = RetryFileAsync(_ as BatchFileItem), file => !IsProcessing && file is BatchFileItem item && RetryPolicy.IsRetryable(item));
        ClearFileDataCommand = new RelayCommand(ClearFileData, file => !IsProcessing && file is BatchFileItem);
        ShowFileOutputCommand = new RelayCommand(_ => ShowFileOutput(_ as BatchFileItem), file => file is BatchFileItem);
        ShowBatchSummaryCommand = new RelayCommand(_ => ShowBatchSummary(), _ => !IsProcessing && Files.Count > 0);
        ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => !IsProcessing && Files.Count > 0);
        ExportExcelCommand = new RelayCommand(_ => ExportExcel(), _ => !IsProcessing && Files.Count > 0);
        Files.CollectionChanged += (_, _) =>
        {
            RaiseFileCommandStates();
            RefreshBatchKpis();
        };

        FilesView = CollectionViewSource.GetDefaultView(Files);

        LoadConfig();
        _ = RefreshTipologiasAsync();
    }

    public ObservableCollection<BatchFileItem> Files { get; }

    public ICollectionView FilesView { get; }

    public ObservableCollection<TipologiaOption> AvailableTipologias { get; }

    public RelayCommand RemoveFileCommand { get; }

    public RelayCommand PickFilesCommand { get; }

    public RelayCommand SaveConfigCommand { get; }

    public RelayCommand EditPromptCommand { get; }

    public RelayCommand RefreshTipologiasCommand { get; }

    public RelayCommand StartProcessingCommand { get; }

    public RelayCommand CancelProcessingCommand { get; }

    public RelayCommand RetryFailedCommand { get; }

    public RelayCommand RetryFileCommand { get; }

    public RelayCommand ClearFileDataCommand { get; }

    public RelayCommand ShowFileOutputCommand { get; }

    public RelayCommand ShowBatchSummaryCommand { get; }

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

    public TipologiaOption? SelectedTipologia
    {
        get => _selectedTipologia;
        set
        {
            if (SetProperty(ref _selectedTipologia, value))
            {
                EditPromptCommand.RaiseCanExecuteChanged();
                StartProcessingCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasPromptOverride));
            }
        }
    }

    public bool HasPromptOverride => SelectedTipologia is not null &&
        _promptOverrides.ContainsKey(SelectedTipologia.Code);

    public bool PromptingEnabled
    {
        get => _promptingEnabled;
        set => SetProperty(ref _promptingEnabled, value);
    }

    public int UmbralConfianza
    {
        get => _umbralConfianza;
        set => SetProperty(ref _umbralConfianza, value);
    }

    public int NumeroColas
    {
        get => _numeroColas;
        set => SetProperty(ref _numeroColas, value);
    }

    public bool EjecutarConAssetResolver
    {
        get => _ejecutarConAssetResolver;
        set => SetProperty(ref _ejecutarConAssetResolver, value);
    }

    public bool SubirAGdc
    {
        get => _subirAGdc;
        set => SetProperty(ref _subirAGdc, value);
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
                RefreshTipologiasCommand.RaiseCanExecuteChanged();
                PickFilesCommand.RaiseCanExecuteChanged();
                RemoveFileCommand.RaiseCanExecuteChanged();
                RetryFailedCommand.RaiseCanExecuteChanged();
                RetryFileCommand.RaiseCanExecuteChanged();
                ClearFileDataCommand.RaiseCanExecuteChanged();
                ShowBatchSummaryCommand.RaiseCanExecuteChanged();
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

    public int PendingFiles => Files.Count(IsPendingFile);

    public int RunningFiles => Files.Count(IsRunningFile);

    public int CompletedFiles => Files.Count(IsCompletedFile);

    public int RevisionFiles => Files.Count(IsRevisionFile);

    public int ErrorFiles => Files.Count(IsErrorFile);

    public int CanceledFiles => Files.Count(IsCanceledFile);

    public int FinishedFiles => Files.Count(IsFinishedFile);

    public int OutputJsonFiles => Files.Count(file => !string.IsNullOrWhiteSpace(file.OutputJsonPath) && File.Exists(file.OutputJsonPath));

    public string SuccessRateDisplay => FinishedFiles > 0
        ? $"{CompletedFiles / (double)FinishedFiles:P1}"
        : "-";

    public string AverageConfidenceDisplay
    {
        get
        {
            var confidences = Files
                .Where(file => file.ConfianzaGlobal.HasValue)
                .Select(file => file.ConfianzaGlobal!.Value)
                .ToList();

            return confidences.Count > 0 ? $"{confidences.Average():P1}" : "-";
        }
    }

    public string AverageDurationDisplay
    {
        get
        {
            var durations = Files
                .Where(file => file.FechaInicio.HasValue && file.FechaFin.HasValue)
                .Select(file => file.FechaFin!.Value - file.FechaInicio!.Value)
                .ToList();

            return durations.Count > 0
                ? TimeSpan.FromMilliseconds(durations.Average(duration => duration.TotalMilliseconds)).ToString(@"mm\:ss")
                : "-";
        }
    }

    public string TotalDurationDisplay
    {
        get
        {
            var starts = Files.Where(file => file.FechaInicio.HasValue).Select(file => file.FechaInicio!.Value).ToList();
            var ends = Files.Where(file => file.FechaFin.HasValue).Select(file => file.FechaFin!.Value).ToList();

            return starts.Count > 0 && ends.Count > 0
                ? (ends.Max() - starts.Min()).ToString(@"mm\:ss")
                : "-";
        }
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Files.Any(x => string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var fileInfo = new FileInfo(path);
            Files.Add(new BatchFileItem
            {
                FileName = fileInfo.Name,
                FullPath = fileInfo.FullName,
                SizeBytes = fileInfo.Length,
                Estado = "Pendiente"
            });
        }

        StartProcessingCommand.RaiseCanExecuteChanged();
        RetryFailedCommand.RaiseCanExecuteChanged();
        RefreshBatchKpis();
    }

    private void PickFiles()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    private void RemoveFile(object? parameter)
    {
        if (parameter is BatchFileItem item)
        {
            Files.Remove(item);
            RaiseFileCommandStates();
        }
    }

    private void LoadConfig()
    {
        var config = _settingsService.Load();
        BackendUrl = config.BackendUrl;
        FunctionKey = config.FunctionKey;
        PromptingEnabled = config.PromptingEnabled;
        UmbralConfianza = config.UmbralConfianza;
        NumeroColas = config.NumeroColas;
        EjecutarConAssetResolver = config.EjecutarConAssetResolver;
        SubirAGdc = config.SubirAGdc;
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

    private void SaveConfig()
    {
        if (UmbralConfianza < 0 || UmbralConfianza > 100)
        {
            MessageBox.Show("El umbral debe estar entre 0 y 100.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (NumeroColas < 1 || NumeroColas > 10)
        {
            MessageBox.Show("El número de colas debe estar entre 1 y 10.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var config = new BatchConfig
        {
            BackendUrl = BackendUrl,
            FunctionKey = FunctionKey,
            SelectedTipologia = SelectedTipologia?.Code ?? "nota.simple.1_4",
            PromptingEnabled = PromptingEnabled,
            UmbralConfianza = UmbralConfianza,
            NumeroColas = NumeroColas,
            EjecutarConAssetResolver = EjecutarConAssetResolver,
            SubirAGdc = SubirAGdc,
            PromptOverrides = new Dictionary<string, PromptOverride>(_promptOverrides, StringComparer.OrdinalIgnoreCase)
        };

        _settingsService.Save(config);
        MessageBox.Show("Configuración guardada correctamente.", "DocumentIA.Batch", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void EditPrompt()
    {
        if (SelectedTipologia is null)
        {
            return;
        }

        _promptOverrides.TryGetValue(SelectedTipologia.Code, out var currentOverride);

        var dialog = new PromptEditorDialog(SelectedTipologia.Code, currentOverride)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.PromptOverride is null)
        {
            _promptOverrides.Remove(SelectedTipologia.Code);
        }
        else
        {
            _promptOverrides[SelectedTipologia.Code] = dialog.PromptOverride;
        }

        OnPropertyChanged(nameof(HasPromptOverride));
        SaveConfig();
    }

    private bool CanProcess()
    {
        return !IsProcessing
            && Files.Any(IsPendingFile)
            && SelectedTipologia is not null
            && !string.IsNullOrWhiteSpace(BackendUrl);
    }

    private bool CanRetryFailed()
    {
        return !IsProcessing
            && SelectedTipologia is not null
            && !string.IsNullOrWhiteSpace(BackendUrl)
            && Files.Any(RetryPolicy.IsRetryable);
    }

    private async Task RefreshTipologiasAsync()
    {
        if (IsProcessing || string.IsNullOrWhiteSpace(BackendUrl))
        {
            return;
        }

        try
        {
            var selectedCode = SelectedTipologia?.Code;
            var tipologias = await _backendClient.GetTipologiasAsync(BackendUrl, CancellationToken.None);
            if (tipologias.Count == 0)
            {
                ProcessStatus = "No se encontraron tipologías publicadas en backend.";
                return;
            }

            AvailableTipologias.Clear();
            foreach (var code in tipologias)
            {
                AvailableTipologias.Add(new TipologiaOption { Code = code });
            }

            SelectedTipologia = AvailableTipologias.FirstOrDefault(x =>
                string.Equals(x.Code, selectedCode, StringComparison.OrdinalIgnoreCase))
                ?? AvailableTipologias.FirstOrDefault(x =>
                    string.Equals(x.Code, "nota.simple.1_4", StringComparison.OrdinalIgnoreCase))
                ?? AvailableTipologias.First();

            ProcessStatus = $"Tipologías cargadas: {AvailableTipologias.Count}";
        }
        catch (Exception ex)
        {
            ProcessStatus = $"No se pudieron cargar tipologías del backend: {ex.Message}";
        }
    }

    private async Task StartProcessingAsync()
    {
        var pendingFiles = Files.Where(IsPendingFile).ToList();
        if (pendingFiles.Count == 0)
        {
            ProcessStatus = "No hay registros pendientes para procesar.";
            return;
        }

        await ProcessFilesAsync(pendingFiles, "procesamiento");
    }

    private async Task RetryFailedAsync()
    {
        var retryFiles = Files.Where(RetryPolicy.IsRetryable).ToList();
        if (retryFiles.Count == 0)
        {
            ProcessStatus = "No hay ficheros reintentables.";
            return;
        }

        await ProcessFilesAsync(retryFiles, "reintento");
    }

    private async Task RetryFileAsync(BatchFileItem? file)
    {
        if (file is null || !RetryPolicy.IsRetryable(file))
        {
            return;
        }

        await ProcessFilesAsync(new[] { file }, "reintento");
    }

    private async Task ProcessFilesAsync(IReadOnlyCollection<BatchFileItem> filesToProcess, string operationName)
    {
        if (filesToProcess.Count == 0 || !CanProcessFiles(filesToProcess))
        {
            return;
        }

        IsProcessing = true;
        _processingCts = new CancellationTokenSource();
        var cancellationToken = _processingCts.Token;
        var stopwatch = Stopwatch.StartNew();
        var total = filesToProcess.Count;
        var tipologiaCode = SelectedTipologia?.Code ?? "nota.simple.1_4";
        var promptOverridesSnapshot = new Dictionary<string, PromptOverride>(_promptOverrides, StringComparer.OrdinalIgnoreCase);
        var maxParallelism = Math.Clamp(NumeroColas, 1, 10);
        var runFolder = _runStorageService.CreateRunFolder();
        var processed = 0;
        var errors = 0;
        var revisions = 0;
        var canceled = 0;
        using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);

        try
        {
            await SetProcessStatusAsync($"Iniciando {operationName} de {total} fichero(s) con {maxParallelism} cola(s).");

            var tasks = filesToProcess.Select(file => ProcessFileWithSemaphoreAsync(
                file,
                tipologiaCode,
                promptOverridesSnapshot,
                runFolder,
                semaphore,
                total,
                () => Volatile.Read(ref processed),
                () => Volatile.Read(ref revisions),
                () => Volatile.Read(ref errors),
                () => Volatile.Read(ref canceled),
                () => Interlocked.Increment(ref processed),
                () => Interlocked.Increment(ref revisions),
                () => Interlocked.Increment(ref errors),
                () => Interlocked.Increment(ref canceled),
                cancellationToken));

            await Task.WhenAll(tasks);

            stopwatch.Stop();
            await SetProcessStatusAsync(
                $"Lote finalizado en {stopwatch.Elapsed:mm\\:ss}. " +
                $"Procesados {processed}/{total}. Revisión: {revisions}. Error: {errors}. Cancelados: {canceled}.");
            _lastRunSummary = BuildRunSummary(operationName);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            await SetProcessStatusAsync(
                $"Lote cancelado en {stopwatch.Elapsed:mm\\:ss}. " +
                $"Procesados {processed}/{total}. Revisión: {revisions}. Error: {errors}. Cancelados: {canceled}.");
            _lastRunSummary = BuildRunSummary($"{operationName} cancelado");
        }
        finally
        {
            IsProcessing = false;
            _processingCts?.Dispose();
            _processingCts = null;
            RaiseFileCommandStates();
            ShowBatchSummaryCommand.RaiseCanExecuteChanged();

            if (_lastRunSummary is not null)
            {
                ShowBatchSummary(_lastRunSummary);
            }
        }
    }

    private bool CanProcessFiles(IReadOnlyCollection<BatchFileItem> filesToProcess)
    {
        return !IsProcessing
            && filesToProcess.Count > 0
            && SelectedTipologia is not null
            && !string.IsNullOrWhiteSpace(BackendUrl);
    }

    private async Task ProcessFileWithSemaphoreAsync(
        BatchFileItem file,
        string tipologiaCode,
        Dictionary<string, PromptOverride> promptOverrides,
        string runFolder,
        SemaphoreSlim semaphore,
        int total,
        Func<int> getProcessed,
        Func<int> getRevisions,
        Func<int> getErrors,
        Func<int> getCanceled,
        Func<int> incrementProcessed,
        Func<int> incrementRevisions,
        Func<int> incrementErrors,
        Func<int> incrementCanceled,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ResetFileTraceAsync(file);
                await SetFileStatusAsync(file, "En cola");
                await SetFileStatusAsync(file, "Enviando a backend");

                var correlationId = Guid.NewGuid().ToString();
                await SetFileTraceAsync(file, item =>
                {
                    item.CorrelationId = correlationId;
                    item.FechaInicio = DateTime.Now;
                });

                var request = BuildIngestRequest(file, tipologiaCode, promptOverrides, correlationId);
                var ingestResponse = await _backendClient.IngestAsync(BackendUrl, FunctionKey, request, cancellationToken);
                await SetFileTraceAsync(file, item => item.InstanceId = ingestResponse.InstanceId);

                await SetFileStatusAsync(file, "En ejecución");
                var finalStatus = await WaitForFinalStatusAsync(
                    ingestResponse.StatusQueryUri,
                    status => UpdateFileActivityAsync(file, status, getProcessed, total),
                    cancellationToken);
                await SetFileTraceAsync(file, item => item.RuntimeStatus = finalStatus.RuntimeStatus);

                if (string.Equals(finalStatus.RuntimeStatus, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    var estadoCalidad = TryGetEstadoCalidad(finalStatus.Output);
                    var confianzaGlobal = TryGetConfianzaGlobal(finalStatus.Output);
                    var outputJsonPath = SaveOutputIfPresent(runFolder, file, finalStatus.Output);

                    await SetFileTraceAsync(file, item =>
                    {
                        item.EstadoCalidad = estadoCalidad;
                        item.ConfianzaGlobal = confianzaGlobal;
                        item.OutputJsonPath = outputJsonPath;
                        item.FechaFin = DateTime.Now;
                    });

                    if (IsRevisionQuality(estadoCalidad))
                    {
                        incrementRevisions();
                        await SetFileStatusAsync(file, "Revision");
                    }
                    else
                    {
                        await SetFileStatusAsync(file, "Completado");
                    }
                }
                else
                {
                    incrementErrors();
                    await SetFileTraceAsync(file, item =>
                    {
                        item.FechaFin = DateTime.Now;
                        item.MensajeError = finalStatus.RuntimeStatus;
                    });
                    await SetFileStatusAsync(file, "Error");
                }
            }
            catch (OperationCanceledException)
            {
                incrementCanceled();
                await SetFileTraceAsync(file, item => item.FechaFin = DateTime.Now);
                await SetFileStatusAsync(file, "Cancelado");
                throw;
            }
            catch (Exception ex)
            {
                incrementErrors();
                await SetFileTraceAsync(file, item =>
                {
                    item.FechaFin = DateTime.Now;
                    item.MensajeError = ex.Message;
                });
                await SetFileStatusAsync(file, $"Error: {TrimError(ex.Message)}");
            }

            incrementProcessed();
            await SetProcessStatusAsync(
                $"Procesados {getProcessed()}/{total}. Revisión: {getRevisions()}. " +
                $"Error: {getErrors()}. Cancelados: {getCanceled()}.");
        }
        finally
        {
            semaphore.Release();
        }
    }

    private void CancelProcessing()
    {
        if (!IsProcessing || _processingCts is null)
        {
            return;
        }

        _processingCts.Cancel();
        _ = SetProcessStatusAsync("Cancelación solicitada. Finalizando tareas en curso...");
    }

    private IngestRequest BuildIngestRequest(
        BatchFileItem file,
        string tipologiaCode,
        Dictionary<string, PromptOverride> promptOverrides,
        string correlationId)
    {
        promptOverrides.TryGetValue(tipologiaCode, out var promptOverride);
        var umbral = Math.Clamp(UmbralConfianza / 100d, 0d, 1d);

        var request = new IngestRequest
        {
            Instrucciones = new IngestInstrucciones
            {
                ExpectedType = tipologiaCode,
                SkipDuplicateCheck = false,
                ForceReprocess = false,
                SkipGdcUpload = !SubirAGdc,
                Classification = new IngestIaConfig
                {
                    Provider = "auto",
                    Model = "auto",
                    Umbral = umbral
                },
                Extraction = new IngestIaConfig
                {
                    Provider = "auto",
                    Model = "auto",
                    Umbral = umbral
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
                SubmittedBy = "DocumentIA.Batch"
            }
        };

        if (PromptingEnabled && promptOverride is not null &&
            (!string.IsNullOrWhiteSpace(promptOverride.SystemPrompt) || !string.IsNullOrWhiteSpace(promptOverride.UserPromptTemplate)))
        {
            request.Instrucciones.Prompt = new IngestPromptConfig
            {
                SystemPrompt = string.IsNullOrWhiteSpace(promptOverride.SystemPrompt) ? null : promptOverride.SystemPrompt,
                UserPromptTemplate = string.IsNullOrWhiteSpace(promptOverride.UserPromptTemplate) ? null : promptOverride.UserPromptTemplate
            };
        }

        return request;
    }

    private async Task<DurableStatusResponse> WaitForFinalStatusAsync(
        string statusQueryUri,
        Func<DurableStatusResponse, Task> onStatusUpdate,
        CancellationToken cancellationToken)
    {
        var maxAttempts = 180;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var status = await _backendClient.GetDurableStatusAsync(statusQueryUri, cancellationToken);
            await onStatusUpdate(status);

            if (string.Equals(status.RuntimeStatus, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status.RuntimeStatus, "Failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status.RuntimeStatus, "Terminated", StringComparison.OrdinalIgnoreCase))
            {
                return status;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException("Timeout esperando estado final de la orquestación.");
    }

    private async Task UpdateFileActivityAsync(
        BatchFileItem file,
        DurableStatusResponse status,
        Func<int> getProcessed,
        int total)
    {
        var progress = DurableCustomStatusReader.Read(status.CustomStatus);
        if (string.IsNullOrWhiteSpace(progress.ActivityName)
            && string.IsNullOrWhiteSpace(progress.ProgressText)
            && string.IsNullOrWhiteSpace(progress.Detail))
        {
            await SetFileTraceAsync(file, item => item.RuntimeStatus = status.RuntimeStatus);
            return;
        }

        await SetFileTraceAsync(file, item =>
        {
            item.RuntimeStatus = status.RuntimeStatus;
            item.ActividadActual = progress.ActivityName;
            item.ProgresoActividades = progress.ProgressText;
            item.DetalleActividad = progress.Detail;
        });

        var activityText = string.IsNullOrWhiteSpace(progress.ActivityName) ? status.RuntimeStatus : progress.ActivityName;
        var progressText = string.IsNullOrWhiteSpace(progress.ProgressText) ? string.Empty : $" ({progress.ProgressText})";
        await SetProcessStatusAsync($"Procesados {getProcessed()}/{total}. {file.FileName}: {activityText}{progressText}.");
    }

    private static string TryGetEstadoCalidad(JsonElement? output)
    {
        if (!output.HasValue || output.Value.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var estadoCalidad = BatchOutputJsonReader.GetPathValue(output.Value, "Resultado.EstadoCalidad");
        return !string.IsNullOrWhiteSpace(estadoCalidad)
            ? estadoCalidad
            : BatchOutputJsonReader.GetPathValue(output.Value, "Resultado.Estado");
    }

    private static double? TryGetConfianzaGlobal(JsonElement? output)
    {
        if (!output.HasValue || output.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return BatchOutputJsonReader.GetDoublePathValue(output.Value, "Resultado.ConfianzaGlobal");
    }

    private string SaveOutputIfPresent(string runFolder, BatchFileItem file, JsonElement? output)
    {
        if (!output.HasValue || output.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return _runStorageService.SaveOutputJson(runFolder, file, output.Value);
    }

    private void SetFileStatus(BatchFileItem file, string status)
    {
        file.Estado = status;
        FilesView.Refresh();
    }

    private async Task SetFileStatusAsync(BatchFileItem file, string status)
    {
        await RunOnUiAsync(() =>
        {
            file.Estado = status;
            FilesView.Refresh();
            RetryFailedCommand.RaiseCanExecuteChanged();
            RetryFileCommand.RaiseCanExecuteChanged();
            RefreshBatchKpis();
        });
    }

    private async Task ResetFileTraceAsync(BatchFileItem file)
    {
        await SetFileTraceAsync(file, item =>
        {
            RetryPolicy.ResetForRetry(item);
        });
    }

    private async Task SetFileTraceAsync(BatchFileItem file, Action<BatchFileItem> update)
    {
        await RunOnUiAsync(() =>
        {
            update(file);
            FilesView.Refresh();
            RefreshBatchKpis();
        });
    }

    private async Task SetProcessStatusAsync(string status)
    {
        await RunOnUiAsync(() => ProcessStatus = status);
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

    private void RaiseFileCommandStates()
    {
        StartProcessingCommand.RaiseCanExecuteChanged();
        RetryFailedCommand.RaiseCanExecuteChanged();
        RetryFileCommand.RaiseCanExecuteChanged();
        ClearFileDataCommand.RaiseCanExecuteChanged();
        ShowFileOutputCommand.RaiseCanExecuteChanged();
        ShowBatchSummaryCommand.RaiseCanExecuteChanged();
        ExportCsvCommand.RaiseCanExecuteChanged();
        ExportExcelCommand.RaiseCanExecuteChanged();
    }

    private void ShowBatchSummary()
    {
        var summary = _lastRunSummary ?? BuildRunSummary("estado actual");
        ShowBatchSummary(summary);
    }

    private void ShowBatchSummary(BatchRunSummary summary)
    {
        var dialog = new BatchSummaryDialog(summary, ExportCsv, ExportExcel)
        {
            Owner = Application.Current.MainWindow
        };

        dialog.ShowDialog();
    }

    private void ExportCsv()
    {
        if (IsProcessing || Files.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exportar resumen CSV",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"DocumentIA_Batch_{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _csvExportService.Export(
                dialog.FileName,
                Files.ToList(),
                SelectedTipologia?.Code ?? string.Empty,
                NumeroColas,
                UmbralConfianza,
                SubirAGdc,
                EjecutarConAssetResolver);

            ProcessStatus = $"CSV exportado: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo exportar el CSV: {ex.Message}", "Exportar CSV", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportExcel()
    {
        if (IsProcessing || Files.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exportar resumen Excel",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"DocumentIA_Batch_{DateTime.Now:yyyyMMdd-HHmmss}.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _excelExportService.Export(
                dialog.FileName,
                Files.ToList(),
                SelectedTipologia?.Code ?? string.Empty,
                NumeroColas,
                UmbralConfianza,
                SubirAGdc,
                EjecutarConAssetResolver);

            ProcessStatus = $"Excel exportado: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo exportar el Excel: {ex.Message}", "Exportar Excel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearFileData(object? parameter)
    {
        if (parameter is not BatchFileItem file || IsProcessing)
        {
            return;
        }

        RetryPolicy.ResetForRetry(file);
        FilesView.Refresh();
        RaiseFileCommandStates();
        RefreshBatchKpis();
        ProcessStatus = $"Registro limpiado: {file.FileName}. Listo para un nuevo lote.";
    }

    private void ShowFileOutput(BatchFileItem? file)
    {
        if (file is null)
        {
            return;
        }

        var outputText = BuildOutputText(file);
        var dialog = new BatchOutputDialog(file.FileName, outputText)
        {
            Owner = Application.Current.MainWindow
        };

        dialog.ShowDialog();
    }

    private static string BuildOutputText(BatchFileItem file)
    {
        if (!string.IsNullOrWhiteSpace(file.OutputJsonPath) && File.Exists(file.OutputJsonPath))
        {
            return File.ReadAllText(file.OutputJsonPath);
        }

        var fallback = new
        {
            FileName = file.FileName,
            Estado = file.Estado,
            RuntimeStatus = file.RuntimeStatus,
            EstadoCalidad = file.EstadoCalidad,
            ConfianzaGlobal = file.ConfianzaGlobal,
            CorrelationId = file.CorrelationId,
            InstanceId = file.InstanceId,
            ActividadActual = file.ActividadActual,
            ProgresoActividades = file.ProgresoActividades,
            DetalleActividad = file.DetalleActividad,
            MensajeError = file.MensajeError,
            OutputJsonPath = file.OutputJsonPath
        };

        return JsonSerializer.Serialize(fallback, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private BatchRunSummary BuildRunSummary(string operationName)
    {
        return new BatchRunSummary
        {
            OperationName = operationName,
            ProcessStatus = ProcessStatus,
            Tipologia = SelectedTipologia?.Code ?? string.Empty,
            NumeroColas = NumeroColas,
            GeneratedAt = DateTime.Now,
            TotalFiles = TotalFiles,
            FinishedFiles = FinishedFiles,
            CompletedFiles = CompletedFiles,
            RevisionFiles = RevisionFiles,
            ErrorFiles = ErrorFiles,
            CanceledFiles = CanceledFiles,
            OutputJsonFiles = OutputJsonFiles,
            SuccessRate = SuccessRateDisplay,
            AverageConfidence = AverageConfidenceDisplay,
            AverageDuration = AverageDurationDisplay,
            TotalDuration = TotalDurationDisplay,
            Issues = Files
                .Where(file => IsRevisionFile(file) || IsErrorFile(file) || IsCanceledFile(file))
                .Select(file => new BatchRunIssueSummary
                {
                    FileName = file.FileName,
                    Estado = file.Estado,
                    EstadoCalidad = file.EstadoCalidad,
                    Confidence = file.ConfidenceDisplay,
                    Duration = file.DurationDisplay,
                    MensajeError = file.MensajeError
                })
                .ToList()
        };
    }

    private void RefreshBatchKpis()
    {
        OnPropertyChanged(nameof(TotalFiles));
        OnPropertyChanged(nameof(PendingFiles));
        OnPropertyChanged(nameof(RunningFiles));
        OnPropertyChanged(nameof(CompletedFiles));
        OnPropertyChanged(nameof(RevisionFiles));
        OnPropertyChanged(nameof(ErrorFiles));
        OnPropertyChanged(nameof(CanceledFiles));
        OnPropertyChanged(nameof(FinishedFiles));
        OnPropertyChanged(nameof(OutputJsonFiles));
        OnPropertyChanged(nameof(SuccessRateDisplay));
        OnPropertyChanged(nameof(AverageConfidenceDisplay));
        OnPropertyChanged(nameof(AverageDurationDisplay));
        OnPropertyChanged(nameof(TotalDurationDisplay));
    }

    private static bool IsPendingFile(BatchFileItem file)
    {
        return string.Equals(file.Estado, "Pendiente", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRunningFile(BatchFileItem file)
    {
        return string.Equals(file.Estado, "En cola", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Estado, "Enviando a backend", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Estado, "En ejecución", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompletedFile(BatchFileItem file)
    {
        return string.Equals(file.Estado, "Completado", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRevisionFile(BatchFileItem file)
    {
        return string.Equals(file.Estado, "Revision", StringComparison.OrdinalIgnoreCase)
            || IsRevisionQuality(file.EstadoCalidad);
    }

    private static bool IsRevisionQuality(string value)
    {
        return string.Equals(value, "REVISION", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "VALIDACION_CON_ERRORES", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "BAJA_CONFIANZA", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsErrorFile(BatchFileItem file)
    {
        return file.Estado.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.RuntimeStatus, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.RuntimeStatus, "Terminated", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCanceledFile(BatchFileItem file)
    {
        return string.Equals(file.Estado, "Cancelado", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFinishedFile(BatchFileItem file)
    {
        return IsCompletedFile(file) || IsRevisionFile(file) || IsErrorFile(file) || IsCanceledFile(file);
    }

    private static string TrimError(string message)
    {
        const int max = 120;
        if (string.IsNullOrWhiteSpace(message) || message.Length <= max)
        {
            return message;
        }

        return message[..max] + "...";
    }
}
