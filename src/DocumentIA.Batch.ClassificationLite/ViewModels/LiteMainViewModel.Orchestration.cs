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

    /// <summary>
    /// Dispatcher del hilo que construyo el ViewModel (el hilo de UI en produccion). El temporizador
    /// de refresco corre en un hilo del ThreadPool y debe marshalizar a este hilo antes de tocar
    /// Rows/RowsView: son un ObservableCollection ligado a un CollectionView con afinidad de hilo,
    /// y modificarlo desde otro hilo lanza una excepcion no controlada que tumba el proceso.
    /// </summary>
    private readonly System.Windows.Threading.Dispatcher _uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

    private readonly object _ctsGate = new();
    private CancellationTokenSource? _cts;
    private LiteEngine? _engine;
    private bool _isRunning;
    private bool _isPaused;
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Ejecución escaneada como previsualización (estado <see cref="LiteExecutionStatus.Scanned"/>)
    /// que aún no se ha procesado. Al pulsar Ejecutar, <see cref="StartAsync"/> la consume sin
    /// volver a escanear. Un nuevo drop la descarta antes de crear la siguiente.
    /// </summary>
    private string? _scannedExecutionId;

    public IReadOnlyList<string> SelectedPaths { get; set; } = Array.Empty<string>();

    public bool IncludeSubfolders { get; set; } = true;

    public bool IsRunning { get => _isRunning; private set { _isRunning = value; OnPropertyChanged(); } }

    public bool IsPaused { get => _isPaused; private set { _isPaused = value; OnPropertyChanged(); } }

    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }

    /// <summary>Inyectable en tests; en produccion crea el adaptador sobre el entorno seleccionado.</summary>
    public Func<IIngestBackend>? BackendFactory { get; set; }

    public LiteExecution? GetPendingRecovery() => _repository.GetIncompleteExecution();

    /// <summary>
    /// Marshaliza ReloadRows() y RefreshCounters() juntos en una unica operacion del
    /// despachador (en vez de dos BeginInvoke separados). Las propiedades que toca
    /// RefreshCounters (TotalFound, PendingCount, etc.) estan ligadas a TextBlock de la
    /// ventana igual que Rows/RowsView: son objetos de interfaz con afinidad de hilo, y
    /// notificarlas desde un hilo distinto del que las creo lanza una excepcion no
    /// controlada fuera de cualquier try/catch de la aplicacion.
    /// </summary>
    private void SafeRefreshRowsAndCounters() => MarshalToUiThread(() =>
    {
        ReloadRows();
        RefreshCounters();
    });

    private void MarshalToUiThread(Action action)
    {
        if (_uiDispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            _uiDispatcher.BeginInvoke(action);
        }
        catch
        {
            // El dispatcher pudo haberse cerrado (app cerrando) o no tener bucle de mensajes
            // activo (tests): no debe tumbar el hilo que dispara el refresco.
        }
    }

    public void DiscardExecution(LiteExecution execution)
    {
        _repository.UpdateExecutionStatus(execution.ExecutionId, LiteExecutionStatus.Aborted, setCompletedAt: true);
        StatusMessage = "Ejecucion anterior descartada.";
    }

    /// <summary>
    /// Escanea las rutas seleccionadas y puebla el grid con las filas Pending como
    /// previsualización, sin arrancar el motor de proceso. Se dispara al arrastrar o
    /// seleccionar ficheros/carpetas para dar feedback inmediato de qué se va a procesar.
    /// La ejecución se crea en estado <see cref="LiteExecutionStatus.Scanned"/> y
    /// <see cref="StartAsync"/> la consume después sin re-escanear.
    /// </summary>
    public async Task PreviewAsync(
        IReadOnlyList<string> paths,
        bool includeSubfolders,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning || paths is null || paths.Count == 0)
        {
            return;
        }

        // Un nuevo drop reemplaza el preview anterior: descartarlo para no dejar
        // ejecuciones Scanned colgadas en la base de datos.
        DiscardPendingPreview();

        SelectedPaths = paths;
        IncludeSubfolders = includeSubfolders;

        var configSnapshot = LiteConfigService.SerializeRedacted(Config);
        var execution = _repository.CreateExecution(
            paths[0], includeSubfolders, configSnapshot, LiteExecutionStatus.Scanned);
        CurrentExecutionId = execution.ExecutionId;
        _scannedExecutionId = execution.ExecutionId;

        StatusMessage = "Escaneando documentos...";
        var scanner = new FolderScanner(_repository);
        var config = Config;
        var scan = await Task.Run(() => scanner.Scan(
            execution.ExecutionId, paths, includeSubfolders,
            config.SkipAlreadyProcessed, config.ForceReprocess, config.InternalBatchSize, cancellationToken),
            cancellationToken);

        SafeRefreshRowsAndCounters();
        StatusMessage = scan.TotalFound == 0
            ? "No se encontraron documentos PDF."
            : $"{scan.TotalFound} documentos encontrados. Pulsa Ejecutar para procesar.";
    }

    private void DiscardPendingPreview()
    {
        if (_scannedExecutionId is null)
        {
            return;
        }

        _repository.UpdateExecutionStatus(_scannedExecutionId, LiteExecutionStatus.Aborted, setCompletedAt: true);
        _scannedExecutionId = null;
    }

    public async Task StartAsync(CancellationToken externalToken = default)
    {
        if (IsRunning || SelectedPaths.Count == 0)
        {
            return;
        }

        // Si existe un preview ya escaneado para la selección actual, procesar sobre
        // esas filas sin volver a escanear: solo se cambia el estado a Running y se
        // arranca el motor.
        if (_scannedExecutionId is not null
            && string.Equals(_scannedExecutionId, CurrentExecutionId, StringComparison.Ordinal))
        {
            var preview = _repository.GetExecution(_scannedExecutionId);
            _scannedExecutionId = null;

            if (preview is not null)
            {
                _repository.UpdateExecutionStatus(preview.ExecutionId, LiteExecutionStatus.Running);
                await RunExecutionAsync(preview, externalToken, async (engine, ct) =>
                {
                    StatusMessage = "Procesando...";
                    await engine.RunAsync(preview.ExecutionId, ct);
                });
                return;
            }
        }

        var configSnapshot = LiteConfigService.SerializeRedacted(Config);
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

            SafeRefreshRowsAndCounters();

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
        CancellationTokenSource cts;
        lock (_ctsGate)
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _cts = cts;
        }

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
            _ => SafeRefreshRowsAndCounters(),
            null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        var preserveStatusMessage = false;

        try
        {
            await body(_engine, cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Ejecucion cancelada.";
            preserveStatusMessage = true;
        }
        catch (Exception ex)
        {
            // LiteEngine.RunAsync ya marco la ejecucion como Aborted y relanza el fallo original:
            // se captura aqui para no tumbar la UI, reflejando el error en el mensaje de estado.
            StatusMessage = "Ejecucion fallida: " + ex.Message;
            preserveStatusMessage = true;
        }
        finally
        {
            SleepBlocker.AllowSleep();
            IsRunning = false;
            IsPaused = false;
            SafeRefreshRowsAndCounters();
            if (!preserveStatusMessage)
            {
                StatusMessage = "Ejecucion finalizada.";
            }

            lock (_ctsGate)
            {
                _cts?.Dispose();
                _cts = null;
            }

            _engine = null;
        }
    }

    public void PauseExecution()
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(CurrentExecutionId))
        {
            return;
        }

        _engine?.Pause();
        IsPaused = true;
        _repository.UpdateExecutionStatus(CurrentExecutionId!, LiteExecutionStatus.Paused);
        StatusMessage = "Ejecucion pausada.";
    }

    public void ResumeExecution()
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(CurrentExecutionId))
        {
            return;
        }

        _engine?.Resume();
        IsPaused = false;
        _repository.UpdateExecutionStatus(CurrentExecutionId!, LiteExecutionStatus.Running);
        StatusMessage = "Ejecucion reanudada.";
    }

    public void CancelExecution()
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(CurrentExecutionId))
        {
            return;
        }

        _engine?.Resume();

        CancellationTokenSource? cts;
        lock (_ctsGate)
        {
            cts = _cts;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

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
