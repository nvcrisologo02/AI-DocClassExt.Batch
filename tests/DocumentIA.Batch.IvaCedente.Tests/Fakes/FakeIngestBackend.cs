using System.Text.Json;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.IvaCedente.Tests.Fakes;

/// <summary>Doble de pruebas de IIngestBackend para la consola de IVA del cedente.</summary>
public class FakeIngestBackend : IIngestBackend
{
    public Func<IngestRequest, IngestResponse> OnIngest { get; set; } = request => new IngestResponse
    {
        InstanceId = "inst-" + request.Documento.ObjectIdGDC,
        StatusQueryUri = "https://backend/runtime/instances/" + request.Documento.ObjectIdGDC
    };

    public Func<string, DurableStatusResponse> OnStatus { get; set; } = _ => CompletedStatus("OK", "ESCR-10", null);

    public List<IngestRequest> Requests { get; } = new();

    public Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(OnIngest(request));
    }

    public Task<DurableStatusResponse> GetStatusAsync(string statusQueryUri, CancellationToken ct)
        => Task.FromResult(OnStatus(statusQueryUri));

    public static DurableStatusResponse CompletedStatus(string estado, string? tdn2, string? resultadoPrompt)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Identificacion"] = new Dictionary<string, object?> { ["Tdn2"] = tdn2 },
            ["Resultado"] = new Dictionary<string, object?> { ["Estado"] = estado },
            ["DatosExtraidos"] = new Dictionary<string, object?> { ["ResultadoPrompt"] = resultadoPrompt }
        });
        return new DurableStatusResponse
        {
            RuntimeStatus = "Completed",
            Output = JsonDocument.Parse(json).RootElement.Clone()
        };
    }

    public static DurableStatusResponse Runtime(string runtimeStatus) => new() { RuntimeStatus = runtimeStatus };
}
