using System.Text.Json;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.ClassificationLite.Tests.Fakes;

public class FakeIngestBackend : IIngestBackend
{
    private int _ingestCalls;
    private int _statusCalls;

    public int IngestCalls => _ingestCalls;
    public int StatusCalls => _statusCalls;
    public List<IngestRequest> Requests { get; } = new();

    /// <summary>Por nombre de documento: comportamiento del ingest. Default: OK con instanceId=nombre.</summary>
    public Func<IngestRequest, IngestResponse> OnIngest { get; set; } = request => new IngestResponse
    {
        InstanceId = "inst-" + request.Documento.Name,
        StatusQueryUri = "https://backend/runtime/instances/" + request.Documento.Name
    };

    /// <summary>Por statusQueryUri: respuesta de estado. Default: Completed con output basico.</summary>
    public Func<string, DurableStatusResponse> OnStatus { get; set; } = uri => CompletedStatus("T01", "T01.02", 0.9);

    public Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _ingestCalls);
        lock (Requests) { Requests.Add(request); }
        return Task.FromResult(OnIngest(request));
    }

    public Task<DurableStatusResponse> GetStatusAsync(string statusQueryUri, CancellationToken ct)
    {
        Interlocked.Increment(ref _statusCalls);
        return Task.FromResult(OnStatus(statusQueryUri));
    }

    public static DurableStatusResponse CompletedStatus(string tdn1, string tdn2, double confidence)
    {
        var json = $$"""
            {
              "Identificacion": { "Tdn1": "{{tdn1}}", "Tdn2": "{{tdn2}}", "Paginas": 5, "FechaProceso": "2026-07-20T12:00:00Z" },
              "Resultado": { "Estado": "OK", "ConfianzaGlobal": {{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}} },
              "DetalleEjecucion": { "PaginasIncluidas": "1-5", "Seguimiento": { "DuracionTotalMs": 3000 } }
            }
            """;
        return new DurableStatusResponse
        {
            RuntimeStatus = "Completed",
            Output = JsonDocument.Parse(json).RootElement.Clone()
        };
    }

    public static DurableStatusResponse FailedStatus() => new() { RuntimeStatus = "Failed" };

    public static DurableStatusResponse RunningStatus() => new() { RuntimeStatus = "Running" };
}
