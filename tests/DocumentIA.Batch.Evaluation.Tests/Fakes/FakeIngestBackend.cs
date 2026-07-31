using System.Text.Json;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.Evaluation.Tests.Fakes;

/// <summary>Doble de pruebas de IIngestBackend, misma forma que el de ClassificationLite.Tests
/// (no accesible desde este proyecto por ser internal a aquel ensamblado).</summary>
public class FakeIngestBackend : IIngestBackend
{
    public Func<IngestRequest, IngestResponse> OnIngest { get; set; } = request => new IngestResponse
    {
        InstanceId = "inst-" + request.Documento.Name,
        StatusQueryUri = "https://backend/runtime/instances/" + request.Documento.Name
    };

    public Func<string, DurableStatusResponse> OnStatus { get; set; } = _ => CompletedStatus("T01", "T01.02", 0.9);

    /// <summary>Ultima request recibida por IngestAsync, para inspeccionar en los tests (p.ej. el recorte de paginas).</summary>
    public IngestRequest? LastRequest { get; private set; }

    public Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(OnIngest(request));
    }

    public Task<DurableStatusResponse> GetStatusAsync(string statusQueryUri, CancellationToken ct) => Task.FromResult(OnStatus(statusQueryUri));

    public static DurableStatusResponse CompletedStatus(
        string tdn1,
        string tdn2,
        double confidence,
        string? provider = null,
        bool fallback = false,
        string estadoContrato = "OK",
        bool rateLimitExcedido = false,
        string origenMarkdown = "")
    {
        var json = $$"""
            {
              "Identificacion": { "Tdn1": "{{tdn1}}", "Tdn2": "{{tdn2}}", "Paginas": 5, "FechaProceso": "2026-07-20T12:00:00Z" },
              "Resultado": { "Estado": "{{estadoContrato}}", "ConfianzaGlobal": {{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}} },
              "DetalleEjecucion": {
                "PaginasIncluidas": "1-5",
                "OrigenMarkdown": "{{origenMarkdown}}",
                "Seguimiento": { "DuracionTotalMs": 3000 },
                "Clasificacion": { "Clasificador": {{(provider is null ? "null" : $"\"{provider}\"")}}, "FallbackLLM": "{{fallback.ToString().ToLowerInvariant()}}", "RateLimitExcedido": {{rateLimitExcedido.ToString().ToLowerInvariant()}} }
              }
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
