using DocumentIA.Batch.Models;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.ClassificationLite.Engine;

public interface IIngestBackend
{
    Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct);
    Task<DurableStatusResponse> GetStatusAsync(string statusQueryUri, CancellationToken ct);
}

public class IngestBackendAdapter : IIngestBackend
{
    private readonly DocumentIaBackendClient _client;
    private readonly EnvironmentConfig _environment;

    public IngestBackendAdapter(DocumentIaBackendClient client, EnvironmentConfig environment)
    {
        _client = client;
        _environment = environment;
    }

    public Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct)
        => _client.IngestAsync(_environment.BackendUrl, _environment.FunctionKey, request, ct);

    public Task<DurableStatusResponse> GetStatusAsync(string statusQueryUri, CancellationToken ct)
        => _client.GetDurableStatusAsync(statusQueryUri, _environment.FunctionKey, ct);
}
