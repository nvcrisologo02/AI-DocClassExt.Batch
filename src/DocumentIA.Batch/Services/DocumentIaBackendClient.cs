using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocumentIA.Batch.Services;

public class DocumentIaBackendClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public DocumentIaBackendClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public async Task<IReadOnlyList<string>> GetTipologiasAsync(string backendUrl, CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(backendUrl, "/api/tipologias");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var tipologias = await JsonSerializer.DeserializeAsync<List<TipologiaPublicaDto>>(responseStream, JsonOptions, cancellationToken)
            ?? new List<TipologiaPublicaDto>();

        return tipologias
            .Select(x => NormalizeTipologiaCode(x.Identificador))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IngestResponse> IngestAsync(
        string backendUrl,
        string functionKey,
        IngestRequest request,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(request, JsonOptions);
        var endpoints = new[]
        {
            BuildEndpoint(backendUrl, "/api/ingest"),
            BuildEndpoint(backendUrl, "/api/IngestDocument")
        };

        HttpStatusCode lastStatusCode = HttpStatusCode.NotFound;

        foreach (var endpoint in endpoints)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(functionKey))
            {
                httpRequest.Headers.TryAddWithoutValidation("x-functions-key", functionKey.Trim());
            }

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            lastStatusCode = response.StatusCode;

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Error al invocar ingest: {(int)response.StatusCode} {response.ReasonPhrase}. {payload}");
            }

            var ingestResponse = JsonSerializer.Deserialize<IngestResponse>(payload, JsonOptions)
                ?? throw new InvalidOperationException("Respuesta de ingest vacía o inválida.");

            if (string.IsNullOrWhiteSpace(ingestResponse.StatusQueryUri))
            {
                throw new InvalidOperationException("La respuesta de ingest no contiene statusQueryUri.");
            }

            ingestResponse.StatusQueryUri = NormalizeStatusQueryUri(backendUrl, ingestResponse.StatusQueryUri);

            return ingestResponse;
        }

        throw new InvalidOperationException($"No se encontró endpoint de ingest. Último estado HTTP: {(int)lastStatusCode}.");
    }

    public async Task<DurableStatusResponse> GetDurableStatusAsync(string statusQueryUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, statusQueryUri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Error consultando estado durable: {(int)response.StatusCode} {response.ReasonPhrase}. {payload}");
        }

        return JsonSerializer.Deserialize<DurableStatusResponse>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Respuesta de estado durable inválida.");
    }

    private static string BuildEndpoint(string backendUrl, string path)
    {
        var normalizedBase = backendUrl.Trim().TrimEnd('/');
        var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
        return $"{normalizedBase}{normalizedPath}";
    }

    private static string NormalizeTipologiaCode(string identificador)
    {
        if (string.IsNullOrWhiteSpace(identificador))
        {
            return string.Empty;
        }

        var parts = identificador.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0].Trim() : identificador.Trim();
    }

    private static string NormalizeStatusQueryUri(string backendUrl, string statusQueryUri)
    {
        if (!Uri.TryCreate(statusQueryUri, UriKind.Absolute, out var statusUri)
            || !Uri.TryCreate(backendUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var backendUri))
        {
            return statusQueryUri;
        }

        if (!IsLocalHost(statusUri.Host) || !IsLocalHost(backendUri.Host) || backendUri.IsDefaultPort)
        {
            return statusQueryUri;
        }

        var builder = new UriBuilder(statusUri)
        {
            Scheme = backendUri.Scheme,
            Host = backendUri.Host,
            Port = backendUri.Port
        };

        return builder.Uri.ToString();
    }

    private static bool IsLocalHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
    }
}

public class IngestRequest
{
    [JsonPropertyName("instrucciones")]
    public IngestInstrucciones Instrucciones { get; set; } = new();

    [JsonPropertyName("documento")]
    public IngestDocumento Documento { get; set; } = new();

    [JsonPropertyName("trazabilidad")]
    public IngestTrazabilidad Trazabilidad { get; set; } = new();
}

public class IngestInstrucciones
{
    [JsonPropertyName("expectedType")]
    public string ExpectedType { get; set; } = string.Empty;

    [JsonPropertyName("skipDuplicateCheck")]
    public bool SkipDuplicateCheck { get; set; }

    [JsonPropertyName("forceReprocess")]
    public bool ForceReprocess { get; set; }

    [JsonPropertyName("skipGDCUpload")]
    public bool? SkipGdcUpload { get; set; }

    [JsonPropertyName("classification")]
    public IngestIaConfig Classification { get; set; } = new();

    [JsonPropertyName("extraction")]
    public IngestIaConfig Extraction { get; set; } = new();

    [JsonPropertyName("prompt")]
    public IngestPromptConfig? Prompt { get; set; }
}

public class IngestIaConfig
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "auto";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "auto";

    [JsonPropertyName("umbral")]
    public double? Umbral { get; set; }
}

public class IngestPromptConfig
{
    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; set; }

    [JsonPropertyName("userPromptTemplate")]
    public string? UserPromptTemplate { get; set; }
}

public class IngestDocumento
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public IngestDocumentoContent Content { get; set; } = new();
}

public class IngestDocumentoContent
{
    [JsonPropertyName("base64")]
    public string Base64 { get; set; } = string.Empty;
}

public class IngestTrazabilidad
{
    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("submittedBy")]
    public string SubmittedBy { get; set; } = "DocumentIA.Batch";
}

public class IngestResponse
{
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; set; } = string.Empty;

    [JsonPropertyName("statusQueryUri")]
    public string StatusQueryUri { get; set; } = string.Empty;
}

public class DurableStatusResponse
{
    [JsonPropertyName("runtimeStatus")]
    public string RuntimeStatus { get; set; } = string.Empty;

    [JsonPropertyName("customStatus")]
    public JsonElement? CustomStatus { get; set; }

    [JsonPropertyName("output")]
    public JsonElement? Output { get; set; }
}

public class TipologiaPublicaDto
{
    [JsonPropertyName("identificador")]
    public string Identificador { get; set; } = string.Empty;

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;
}