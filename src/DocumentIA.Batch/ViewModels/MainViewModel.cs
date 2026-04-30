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
    private CancellationTokenSource? _processingCts;
    private Dictionary<string, PromptOverride> _promptOverrides = new(StringComparer.OrdinalIgnoreCase);

    public MainViewModel() : this(new SettingsService(), new DocumentIaBackendClient(), new BatchRunStorageService())
    {
    }

    public MainViewModel(
        SettingsService settingsService,
        DocumentIaBackendClient backendClient,
        BatchRunStorageService runStorageService)
    {
        _settingsService = settingsService;
        _backendClient = backendClient;
        _runStorageService = runStorageService;

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
        Files.CollectionChanged += (_, _) => StartProcessingCommand.RaiseCanExecuteChanged();

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
            }
        }
    }

    public string ProcessStatus
    {
        get => _processStatus;
        private set => SetProperty(ref _processStatus, value);
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
            StartProcessingCommand.RaiseCanExecuteChanged();
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
            && Files.Count > 0
            && SelectedTipologia is not null
            && !string.IsNullOrWhiteSpace(BackendUrl);
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
        if (!CanProcess())
        {
            return;
        }

        IsProcessing = true;
        _processingCts = new CancellationTokenSource();
        var cancellationToken = _processingCts.Token;
        var stopwatch = Stopwatch.StartNew();
        var total = Files.Count;
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
            await SetProcessStatusAsync($"Iniciando procesamiento de {total} fichero(s) con {maxParallelism} cola(s).");

            var tasks = Files.Select(file => ProcessFileWithSemaphoreAsync(
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
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            await SetProcessStatusAsync(
                $"Lote cancelado en {stopwatch.Elapsed:mm\\:ss}. " +
                $"Procesados {processed}/{total}. Revisión: {revisions}. Error: {errors}. Cancelados: {canceled}.");
        }
        finally
        {
            IsProcessing = false;
            _processingCts?.Dispose();
            _processingCts = null;
        }
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
                var finalStatus = await WaitForFinalStatusAsync(ingestResponse.StatusQueryUri, cancellationToken);
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

                    if (string.Equals(estadoCalidad, "REVISION", StringComparison.OrdinalIgnoreCase))
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

    private async Task<DurableStatusResponse> WaitForFinalStatusAsync(string statusQueryUri, CancellationToken cancellationToken)
    {
        var maxAttempts = 180;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var status = await _backendClient.GetDurableStatusAsync(statusQueryUri, cancellationToken);
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

    private static string TryGetEstadoCalidad(JsonElement? output)
    {
        if (!output.HasValue || output.Value.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!output.Value.TryGetProperty("resultado", out var resultado) || resultado.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!resultado.TryGetProperty("estadoCalidad", out var estadoCalidad))
        {
            return string.Empty;
        }

        return estadoCalidad.GetString() ?? string.Empty;
    }

    private static double? TryGetConfianzaGlobal(JsonElement? output)
    {
        if (!output.HasValue || output.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!output.Value.TryGetProperty("resultado", out var resultado) || resultado.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!resultado.TryGetProperty("confianzaGlobal", out var confianzaGlobal))
        {
            return null;
        }

        return confianzaGlobal.ValueKind == JsonValueKind.Number && confianzaGlobal.TryGetDouble(out var value)
            ? value
            : null;
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
        });
    }

    private async Task ResetFileTraceAsync(BatchFileItem file)
    {
        await SetFileTraceAsync(file, item =>
        {
            item.InstanceId = string.Empty;
            item.CorrelationId = string.Empty;
            item.RuntimeStatus = string.Empty;
            item.EstadoCalidad = string.Empty;
            item.ConfianzaGlobal = null;
            item.MensajeError = string.Empty;
            item.FechaInicio = null;
            item.FechaFin = null;
            item.OutputJsonPath = string.Empty;
        });
    }

    private async Task SetFileTraceAsync(BatchFileItem file, Action<BatchFileItem> update)
    {
        await RunOnUiAsync(() =>
        {
            update(file);
            FilesView.Refresh();
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
