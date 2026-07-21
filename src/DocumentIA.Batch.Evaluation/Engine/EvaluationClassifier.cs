using System.Diagnostics;
using System.Text.Json;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.Evaluation.Models;

namespace DocumentIA.Batch.Evaluation.Engine;

/// <summary>
/// Orquesta la clasificacion-only de un documento reutilizando las piezas puras de
/// ClassificationLite (LiteRequestFactory, IIngestBackend, LiteResultParser,
/// AdaptivePollingStrategy). A diferencia de LiteEngine no hay persistencia en SQLite ni
/// reanudacion: el harness es de un solo paso (ingest -> poll -> parse) por documento, con
/// reintentos en memoria, adecuado para una ejecucion de evaluacion que no necesita resumirse.
/// </summary>
public class EvaluationClassifier
{
    private readonly IIngestBackend _backend;
    private readonly LiteConfig _config;
    private readonly AdaptivePollingStrategy _polling;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public EvaluationClassifier(IIngestBackend backend, LiteConfig config, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _backend = backend;
        _config = config;
        _polling = new AdaptivePollingStrategy(config.PollingIntervalSeconds);
        _delay = delay ?? Task.Delay;
    }

    public async Task<EvaluationResultRow> ClassifyAsync(string corpusRoot, ManifestRow document, CancellationToken ct)
    {
        var result = new EvaluationResultRow
        {
            RelPath = document.RelPath,
            FileName = document.FileName,
            ExpectedTdn1 = document.ExpectedTdn1,
            ExpectedTdn2 = document.ExpectedTdn2
        };

        var fullPath = Path.Combine(corpusRoot, document.RelPath.Replace('/', Path.DirectorySeparatorChar));
        var maxAttempts = _config.MaxRetries + 1;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var bytes = await File.ReadAllBytesAsync(fullPath, ct);
                var correlationId = Guid.NewGuid().ToString();
                var request = LiteRequestFactory.Build(_config, document.FileName, bytes, correlationId);

                var response = await _backend.IngestAsync(request, ct);
                var outcome = await PollUntilTerminalAsync(response.StatusQueryUri, stopwatch, ct);

                if (outcome.Success)
                {
                    ApplySuccess(result, outcome, stopwatch.ElapsedMilliseconds);
                    return result;
                }

                result.Estado = outcome.TimedOut ? EvaluationEstado.Timeout : EvaluationEstado.Error;
                result.Error = outcome.Message;
                result.DuracionMs = stopwatch.ElapsedMilliseconds;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Estado = EvaluationEstado.Error;
                result.Error = ex.Message;
                result.DuracionMs = stopwatch.ElapsedMilliseconds;
            }
        }

        return result;
    }

    private async Task<PollOutcome> PollUntilTerminalAsync(string statusQueryUri, Stopwatch stopwatch, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (_polling.IsTimedOut(stopwatch.Elapsed))
            {
                return PollOutcome.Timeout($"Timeout esperando el resultado ({stopwatch.Elapsed.TotalMinutes:F0} min).");
            }

            await _delay(_polling.GetDelay(attempt), ct);

            var status = await _backend.GetStatusAsync(statusQueryUri, ct);

            if (string.Equals(status.RuntimeStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                if (!status.Output.HasValue)
                {
                    return PollOutcome.Failure("Orquestacion completada sin output.");
                }

                return PollOutcome.Succeeded(status.Output.Value);
            }

            if (string.Equals(status.RuntimeStatus, "Failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status.RuntimeStatus, "Terminated", StringComparison.OrdinalIgnoreCase))
            {
                return PollOutcome.Failure($"Orquestacion terminada con estado {status.RuntimeStatus}.");
            }
        }
    }

    private static void ApplySuccess(EvaluationResultRow result, PollOutcome outcome, long elapsedMs)
    {
        var parsed = LiteResultParser.Parse(outcome.Output!.Value);
        result.PredictedTdn1 = parsed.Tdn1;
        result.PredictedTdn2 = parsed.Tdn2;
        result.Confianza = parsed.Confidence;
        result.DuracionMs = parsed.DurationMs ?? elapsedMs;
        result.Estado = EvaluationEstado.Ok;
        result.Error = null;

        var (provider, fallback) = ExtractProviderAndFallback(outcome.Output.Value);
        result.Proveedor = provider;
        result.Fallback = fallback;
    }

    /// <summary>
    /// Extrae proveedor/fallback de DetalleEjecucion.Clasificacion, replicando el mismo camino
    /// de campos que ClassificationExportService (Clasificador/clasificador y
    /// FallbackLLM/fallbackLLM) porque LiteResultParser no expone estos campos (fuera de alcance
    /// del motor Lite, que no los usa).
    /// </summary>
    private static (string? Provider, bool Fallback) ExtractProviderAndFallback(JsonElement output)
    {
        if (!TryGetProperty(output, out var detalle, "DetalleEjecucion", "detalleEjecucion")
            || !TryGetProperty(detalle, out var clasificacion, "Clasificacion", "clasificacion"))
        {
            return (null, false);
        }

        var provider = GetString(clasificacion, "Clasificador", "clasificador", "ProveedorClasif", "proveedorClasif");
        var fallbackText = GetString(clasificacion, "FallbackLLM", "fallbackLLM");
        var fallback = bool.TryParse(fallbackText, out var parsed) && parsed;

        return (provider, fallback);
    }

    private static bool TryGetProperty(JsonElement source, out JsonElement value, params string[] names)
    {
        if (source.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (source.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement source, params string[] names)
    {
        if (!TryGetProperty(source, out var value, names))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null
        };
    }

    private readonly struct PollOutcome
    {
        public bool Success { get; private init; }
        public bool TimedOut { get; private init; }
        public string? Message { get; private init; }
        public JsonElement? Output { get; private init; }

        public static PollOutcome Succeeded(JsonElement output) => new() { Success = true, Output = output };
        public static PollOutcome Failure(string message) => new() { Success = false, Message = message };
        public static PollOutcome Timeout(string message) => new() { Success = false, TimedOut = true, Message = message };
    }
}
