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

    /// <summary>
    /// Envoltorio de ReloadRows() seguro para cualquier hilo. RefreshCounters() no necesita esto:
    /// solo actualiza propiedades simples, sin tocar el ObservableCollection ligado a RowsView.
    /// </summary>
    private void SafeReloadRows()
    {
        if (_uiDispatcher.CheckAccess())
        {
            ReloadRows();
            return;
        }

        try
        {
            _uiDispatcher.BeginInvoke(new Action(ReloadRows));
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

            SafeReloadRows();
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
            _ => { SafeReloadRows(); RefreshCounters(); },
            null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        try
        {
            await body(_engine, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Ejecucion cancelada.";
        }
        catch (Exception ex)
        {
            // LiteEngine.RunAsync ya marco la ejecucion como Aborted y relanza el fallo original:
            // se captura aqui para no tumbar la UI, reflejando el error en el mensaje de estado.
            StatusMessage = "Ejecucion fallida: " + ex.Message;
        }
        finally
        {
            SleepBlocker.AllowSleep();
            IsRunning = false;
            IsPaused = false;
            SafeReloadRows();
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
