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

    private const int ConsecutiveUnauthorizedLimit = 5;

    private int _consecutiveUnauthorized;

    public event Action<string>? AutoPaused;

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
            TryPersist(document);
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
                Interlocked.Exchange(ref _consecutiveUnauthorized, 0);
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
        TryPersist(document);

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

    /// <summary>
    /// Persiste el estado terminal de un documento sin dejar escapar fallos de la BD:
    /// un error al escribir no debe abortar el resto del lote.
    /// </summary>
    private void TryPersist(LiteDocument document)
    {
        try
        {
            _repository.UpdateDocument(document);
        }
        catch (Exception)
        {
            // El estado en memoria ya refleja el resultado; la recuperacion posterior
            // relee desde SQLite y reintentara este documento.
        }
    }

    private async Task WaitWhilePausedAsync(CancellationToken ct)
    {
        while (_paused)
        {
            await _delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }
}
