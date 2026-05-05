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

    public async Task<IReadOnlyList<TipologiaPublicaDto>> GetTipologiasAsync(string backendUrl, CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(backendUrl, "/api/tipologias");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var tipologias = await JsonSerializer.DeserializeAsync<List<TipologiaPublicaDto>>(responseStream, JsonOptions, cancellationToken)
            ?? new List<TipologiaPublicaDto>();

        // Deduplica por código (parte antes del @) preservando nombre y identificador completo
        return tipologias
            .Where(x => !string.IsNullOrWhiteSpace(x.Identificador))
            .GroupBy(x => NormalizeTipologiaCode(x.Identificador), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    public async Task<BackendHealthInfo> GetHealthAsync(string backendUrl, string functionKey, CancellationToken cancellationToken)
    {
        var normalizedFunctionKey = NormalizeFunctionKey(functionKey);
        var endpoint = BuildEndpoint(backendUrl, "/api/healthcheck", normalizedFunctionKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

        if (!string.IsNullOrWhiteSpace(normalizedFunctionKey))
        {
            request.Headers.TryAddWithoutValidation("x-functions-key", normalizedFunctionKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "Error 401: La Function Key no es válida o ha expirado. " +
                "Actualiza la clave en la sección Herramientas.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Error consultando healthcheck: {(int)response.StatusCode} {response.ReasonPhrase}. {payload}");
        }

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        var components = root.TryGetProperty("components", out var componentsElement) && componentsElement.ValueKind == JsonValueKind.Object
            ? componentsElement
            : default;

        var aggregate = TryReadStatus(root, "status", "overall", "aggregate") ?? "unknown";

        return new BackendHealthInfo
        {
            AggregateStatus = aggregate,
            FunctionsStatus = ReadComponentStatus(components, "functions"),
            AssetResolverStatus = ReadComponentStatus(components, "assetResolver"),
            GdcStatus = ReadComponentStatus(components, "gdc"),
            ModelProvidersStatus = ReadComponentStatus(components, "modelProviders"),
            RawPayload = payload
        };
    }

    public async Task<IngestResponse> IngestAsync(
        string backendUrl,
        string functionKey,
        IngestRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedFunctionKey = NormalizeFunctionKey(functionKey);
        var body = JsonSerializer.Serialize(request, JsonOptions);
        var endpoints = new[]
        {
            BuildEndpoint(backendUrl, "/api/ingest", normalizedFunctionKey),
            BuildEndpoint(backendUrl, "/api/IngestDocument", normalizedFunctionKey)
        };

        HttpStatusCode lastStatusCode = HttpStatusCode.NotFound;

        foreach (var endpoint in endpoints)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(normalizedFunctionKey))
            {
                httpRequest.Headers.TryAddWithoutValidation("x-functions-key", normalizedFunctionKey);
            }

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            lastStatusCode = response.StatusCode;

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException(
                    "Error 401: La Function Key no es válida o ha expirado. " +
                    "Actualiza la clave en 'Herramientas > Function Key' y guarda la configuración.");
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

    public async Task<DurableStatusResponse> GetDurableStatusAsync(string statusQueryUri, string functionKey, CancellationToken cancellationToken)
    {
        var normalizedFunctionKey = NormalizeFunctionKey(functionKey);
        var endpointCandidates = new[]
        {
            statusQueryUri,
            AppendCodeQuery(statusQueryUri, normalizedFunctionKey)
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        HttpStatusCode? lastStatusCode = null;
        string? lastPayload = null;
        string? lastReason = null;

        foreach (var endpoint in endpointCandidates)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(normalizedFunctionKey))
            {
                request.Headers.TryAddWithoutValidation("x-functions-key", normalizedFunctionKey);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<DurableStatusResponse>(payload, JsonOptions)
                    ?? throw new InvalidOperationException("Respuesta de estado durable inválida.");
            }

            lastStatusCode = response.StatusCode;
            lastPayload = payload;
            lastReason = response.ReasonPhrase;

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                break;
            }
        }

        throw new InvalidOperationException(
            $"Error consultando estado durable: {(int)(lastStatusCode ?? HttpStatusCode.BadRequest)} {lastReason}. {lastPayload}");
    }

    private static string BuildEndpoint(string backendUrl, string path, string? functionKey = null)
    {
        var normalizedBase = backendUrl.Trim().TrimEnd('/');
        var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
        var endpoint = $"{normalizedBase}{normalizedPath}";

        if (string.IsNullOrWhiteSpace(functionKey))
        {
            return endpoint;
        }

        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{endpoint}{separator}code={Uri.EscapeDataString(functionKey)}";
    }

    private static string AppendCodeQuery(string endpoint, string functionKey)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(functionKey))
        {
            return endpoint;
        }

        if (endpoint.Contains("code=", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{endpoint}{separator}code={Uri.EscapeDataString(functionKey)}";
    }

    private static string NormalizeFunctionKey(string functionKey)
    {
        if (string.IsNullOrWhiteSpace(functionKey))
        {
            return string.Empty;
        }

        var trimmed = functionKey.Trim();

        if ((trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            || (trimmed.StartsWith('\'') && trimmed.EndsWith('\'')))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
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

    private static string ReadComponentStatus(JsonElement components, string componentName)
    {
        if (components.ValueKind != JsonValueKind.Object || !components.TryGetProperty(componentName, out var component))
        {
            return "unknown";
        }

        var status = TryReadStatus(component, "status", "state", "health");
        return string.IsNullOrWhiteSpace(status) ? "unknown" : status;
    }

    private static string? TryReadStatus(JsonElement source, params string[] names)
    {
        foreach (var name in names)
        {
            if (source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }
}

public class BackendHealthInfo
{
    public string AggregateStatus { get; set; } = "unknown";
    public string FunctionsStatus { get; set; } = "unknown";
    public string AssetResolverStatus { get; set; } = "unknown";
    public string GdcStatus { get; set; } = "unknown";
    public string ModelProvidersStatus { get; set; } = "unknown";
    public string RawPayload { get; set; } = string.Empty;
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

    [JsonPropertyName("assetResolver")]
    public IngestAssetResolverSettings? AssetResolver { get; set; }
}

public class IngestAssetResolverSettings
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("camposSolicitados")]
    public List<string>? CamposSolicitados { get; set; }
}

public class IngestIaConfig
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "auto";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "auto";

    [JsonPropertyName("umbral")]
    public double? Umbral { get; set; }

    [JsonPropertyName("umbralCompletitud")]
    public double? UmbralCompletitud { get; set; }

    [JsonPropertyName("umbralConfianza")]
    public double? UmbralConfianza { get; set; }
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