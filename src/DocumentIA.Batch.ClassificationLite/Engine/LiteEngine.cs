using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.Engine;

public class LiteEngine
{
    // El servidor devuelve el JSON compacto (una sola línea); lo reindentamos para
    // que el diálogo de detalle lo muestre legible, igual que el request almacenado.
    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { WriteIndented = true };

    private readonly LiteRepository _repository;
    private readonly IIngestBackend _backend;
    private readonly LiteConfig _config;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly AdaptivePollingStrategy _polling;

    private volatile bool _paused;

    private const int ConsecutiveUnauthorizedLimit = 5;

    private int _consecutiveUnauthorized;
    private int _autoPauseSignaled;

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

                // Cota de vueltas defensiva: en el camino normal el bucle termina antes (cuando
                // ya no quedan documentos reintentables), pero esto evita un bucle indefinido si
                // algo deja el estado inconsistente (p. ej. RetryCount manipulado externamente).
                var maxPasses = _config.MaxRetries + 1;
                for (var pass = 0; pass < maxPasses; pass++)
                {
                    ct.ThrowIfCancellationRequested();

                    var errors = _repository.GetErrorsInBatch(executionId, batchNumber);

                    // Los que ya agotaron su presupuesto de reintentos (persistido en BD, no el
                    // numero de vuelta de este bucle) no son reintentables: si la app cayo a
                    // mitad de los reintentos y se reanuda la ejecucion, no deben recibir
                    // reintentos extra. Se dejan en Error y el barrido final de mas abajo los
                    // pasa a DefinitiveError sin volver a llamar al backend.
                    var retryable = errors.Where(e => e.RetryCount < _config.MaxRetries).ToList();
                    if (retryable.Count == 0)
                    {
                        break;
                    }

                    foreach (var error in retryable)
                    {
                        error.RetryCount += 1;
                        error.Status = LiteDocumentStatus.Pending;
                        TryPersist(error);
                    }

                    await ProcessDocumentsAsync(retryable, ct);
                }

                foreach (var definitive in _repository.GetErrorsInBatch(executionId, batchNumber))
                {
                    definitive.Status = LiteDocumentStatus.DefinitiveError;
                    TryPersist(definitive);
                }

                ProgressChanged?.Invoke();
            }

            _repository.UpdateExecutionStatus(executionId, LiteExecutionStatus.Completed, setCompletedAt: true);
        }
        catch (OperationCanceledException)
        {
            _repository.UpdateExecutionStatus(executionId, LiteExecutionStatus.Cancelled, setCompletedAt: true);
        }
        catch (Exception)
        {
            try
            {
                _repository.UpdateExecutionStatus(executionId, LiteExecutionStatus.Aborted, setCompletedAt: true);
            }
            catch (Exception)
            {
                // No enmascarar el fallo original si tambien falla la actualizacion de estado.
            }

            throw;
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
                document.Summary = result.Summary;
                document.Estado = result.Estado;
                document.ResponseJson = JsonSerializer.Serialize(status.Output.Value, ResponseJsonOptions);
                document.ErrorMessage = null;

                if (LiteResultParser.IsRetryableBackendFailure(result.Estado))
                {
                    document.Status = LiteDocumentStatus.Error;
                    document.ErrorMessage = $"El backend no completó la clasificación (Estado={result.Estado}).";
                    _repository.UpdateDocument(document);
                    ProgressChanged?.Invoke();
                    return;
                }

                document.Status = LiteDocumentStatus.Succeeded;
                Interlocked.Exchange(ref _consecutiveUnauthorized, 0);
                Interlocked.Exchange(ref _autoPauseSignaled, 0);
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

        if (IsUnauthorizedMessage(message))
        {
            var consecutive = Interlocked.Increment(ref _consecutiveUnauthorized);
            if (consecutive >= ConsecutiveUnauthorizedLimit
                && Interlocked.CompareExchange(ref _autoPauseSignaled, 1, 0) == 0)
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
    /// Reconoce solo los mensajes que indican un 401 real del backend. Buscar "401" suelto
    /// daria falsos positivos con rutas y nombres de fichero (los documentos se nombran por expediente).
    /// </summary>
    private static bool IsUnauthorizedMessage(string message)
    {
        return message.Contains("Error 401", StringComparison.OrdinalIgnoreCase)
            || message.Contains(": 401 ", StringComparison.Ordinal);
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
