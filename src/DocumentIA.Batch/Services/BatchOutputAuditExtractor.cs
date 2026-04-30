using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DocumentIA.Batch.Services;

public class BatchOutputAuditExtractor
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false
    };

    public static readonly IReadOnlyList<string> Headers = new[]
    {
        "Identificacion_TipologiaDetectada",
        "Identificacion_TipoDocumento",
        "Identificacion_NumeroFinca",
        "Identificacion_Registro",
        "Identificacion_Paginas",
        "Integridad_Estado",
        "Integridad_Completa",
        "Integridad_Observaciones",
        "Resultado_EstadoCalidad",
        "Resultado_ConfianzaGlobal",
        "Resultado_MotivoRevision",
        "CamposExtraidos_Resumen",
        "CamposExtraidos_Json",
        "ConfianzaPorCampo_Json",
        "Validaciones_Total",
        "Validaciones_Fallidas",
        "Validaciones_FallidasDetalle",
        "Revision_Requerida",
        "Revision_Campos",
        "Revision_Motivos",
        "AAII_Id",
        "AACC_Id",
        "OutputJson_ParseStatus",
        "OutputJson_ParseError"
    };

    public BatchOutputAuditColumns Extract(string outputJsonPath)
    {
        if (string.IsNullOrWhiteSpace(outputJsonPath) || !File.Exists(outputJsonPath))
        {
            return BatchOutputAuditColumns.Empty("SinOutput", string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(outputJsonPath));
            var root = document.RootElement;
            var extractedFields = FindFirstContainer(root, "campos", "fields", "datosextraidos", "extractedfields", "extraccion", "extraction");
            var validations = FindFirstContainer(root, "validaciones", "validations", "checks", "errores", "errors", "warnings");
            var review = FindFirstContainer(root, "revision", "review", "camposrevision", "reviewfields");

            var audit = new BatchOutputAuditColumns
            {
                IdentificacionTipologiaDetectada = FindFirstScalar(root, "tipologiadetectada", "tipologia", "documenttype", "classification"),
                IdentificacionTipoDocumento = FindFirstScalar(root, "tipodocumento", "tipo", "doctype"),
                IdentificacionNumeroFinca = FindFirstScalar(root, "numerofinca", "finca", "numfinca"),
                IdentificacionRegistro = FindFirstScalar(root, "registro", "registropropiedad"),
                IdentificacionPaginas = FindFirstScalar(root, "paginas", "pages", "pagecount", "numeropaginas"),
                IntegridadEstado = FindFirstScalar(root, "estadointegridad", "integridad", "integrity", "completitud"),
                IntegridadCompleta = FindFirstScalar(root, "documentocompleto", "completo", "complete", "iscomplete"),
                IntegridadObservaciones = FindFirstScalar(root, "observacionesintegridad", "integrityobservations", "observaciones"),
                ResultadoEstadoCalidad = FindFirstScalar(root, "estadocalidad", "qualitystatus"),
                ResultadoConfianzaGlobal = FindFirstScalar(root, "confianzaglobal", "confidenceglobal", "globalconfidence", "confidence"),
                ResultadoMotivoRevision = FindFirstScalar(root, "motivorevision", "motivosrevision", "reviewreason", "reviewreasons"),
                CamposExtraidosResumen = BuildFieldsSummary(extractedFields),
                CamposExtraidosJson = Compact(extractedFields),
                ConfianzaPorCampoJson = BuildFieldConfidenceJson(extractedFields ?? root),
                RevisionRequerida = FindFirstScalar(root, "requiererevision", "revisionrequerida", "reviewrequired", "requiresreview"),
                RevisionCampos = BuildFieldsSummary(review),
                RevisionMotivos = FindFirstScalar(root, "motivosrevision", "reviewreasons", "motivorevision"),
                AaiiId = FindFirstScalar(root, "idaaii", "aaiiid", "codigoaaii", "aaii"),
                AaccId = FindFirstScalar(root, "idaacc", "aaccid", "codigoaacc", "aacc"),
                ParseStatus = "OK"
            };

            var validationSummary = BuildValidationSummary(validations);
            audit.ValidacionesTotal = validationSummary.Total;
            audit.ValidacionesFallidas = validationSummary.Failed;
            audit.ValidacionesFallidasDetalle = validationSummary.FailedDetail;

            return audit;
        }
        catch (Exception ex)
        {
            return BatchOutputAuditColumns.Empty("Error", ex.Message);
        }
    }

    private static JsonElement? FindFirstContainer(JsonElement element, params string[] names)
    {
        var normalizedNames = names.Select(NormalizeName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return FindFirstElement(element, normalizedNames, requireContainer: true);
    }

    private static string FindFirstScalar(JsonElement element, params string[] names)
    {
        var normalizedNames = names.Select(NormalizeName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = FindFirstElement(element, normalizedNames, requireContainer: false);
        return found.HasValue ? ToDisplayValue(found.Value) : string.Empty;
    }

    private static JsonElement? FindFirstElement(JsonElement element, HashSet<string> names, bool requireContainer)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var name = NormalizeName(property.Name);
                if (names.Contains(name) && (!requireContainer || IsContainer(property.Value)))
                {
                    return property.Value;
                }

                var nested = FindFirstElement(property.Value, names, requireContainer);
                if (nested.HasValue)
                {
                    return nested.Value;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindFirstElement(item, names, requireContainer);
                if (nested.HasValue)
                {
                    return nested.Value;
                }
            }
        }

        return null;
    }

    private static string BuildFieldsSummary(JsonElement? element)
    {
        if (!element.HasValue)
        {
            return string.Empty;
        }

        var values = new List<string>();
        CollectLeafValues(element.Value, string.Empty, values, maxItems: 20);
        return string.Join(" | ", values);
    }

    private static string BuildFieldConfidenceJson(JsonElement element)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CollectFieldConfidences(element, string.Empty, values);
        return values.Count == 0 ? string.Empty : JsonSerializer.Serialize(values, CompactJsonOptions);
    }

    private static ValidationSummary BuildValidationSummary(JsonElement? element)
    {
        if (!element.HasValue)
        {
            return new ValidationSummary(string.Empty, string.Empty, string.Empty);
        }

        var total = 0;
        var failed = 0;
        var failedDetails = new List<string>();
        CollectValidations(element.Value, string.Empty, ref total, ref failed, failedDetails);
        return new ValidationSummary(
            total.ToString(CultureInfo.InvariantCulture),
            failed.ToString(CultureInfo.InvariantCulture),
            string.Join(" | ", failedDetails.Take(10)));
    }

    private static void CollectLeafValues(JsonElement element, string path, List<string> values, int maxItems)
    {
        if (values.Count >= maxItems)
        {
            return;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var childPath = string.IsNullOrWhiteSpace(path) ? property.Name : $"{path}.{property.Name}";
                CollectLeafValues(property.Value, childPath, values, maxItems);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                CollectLeafValues(item, $"{path}[{index++}]", values, maxItems);
            }
        }
        else if (!string.IsNullOrWhiteSpace(path))
        {
            var value = ToDisplayValue(element);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add($"{path}={value}");
            }
        }
    }

    private static void CollectFieldConfidences(JsonElement element, string path, Dictionary<string, string> values)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            string? confidence = null;
            foreach (var property in element.EnumerateObject())
            {
                var normalized = NormalizeName(property.Name);
                if (normalized is "confianza" or "confidence" or "score" or "confidenceglobal" or "confianzaglobal")
                {
                    confidence = ToDisplayValue(property.Value);
                }
            }

            if (!string.IsNullOrWhiteSpace(confidence) && !string.IsNullOrWhiteSpace(path))
            {
                values[path] = confidence;
            }

            foreach (var property in element.EnumerateObject())
            {
                var normalized = NormalizeName(property.Name);
                if (normalized is "confianza" or "confidence" or "score" or "valor" or "value")
                {
                    continue;
                }

                var childPath = string.IsNullOrWhiteSpace(path) ? property.Name : $"{path}.{property.Name}";
                CollectFieldConfidences(property.Value, childPath, values);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                CollectFieldConfidences(item, $"{path}[{index++}]", values);
            }
        }
    }

    private static void CollectValidations(JsonElement element, string path, ref int total, ref int failed, List<string> failedDetails)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                CollectValidations(item, $"{path}[{index++}]", ref total, ref failed, failedDetails);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (LooksLikeValidation(element))
        {
            total++;
            if (IsFailedValidation(element))
            {
                failed++;
                failedDetails.Add(BuildValidationDetail(element, path));
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            var childPath = string.IsNullOrWhiteSpace(path) ? property.Name : $"{path}.{property.Name}";
            CollectValidations(property.Value, childPath, ref total, ref failed, failedDetails);
        }
    }

    private static bool LooksLikeValidation(JsonElement element)
    {
        return element.EnumerateObject().Any(property =>
        {
            var name = NormalizeName(property.Name);
            return name is "ok" or "passed" or "success" or "estado" or "status" or "severity" or "severidad";
        });
    }

    private static bool IsFailedValidation(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            var name = NormalizeName(property.Name);
            var value = ToDisplayValue(property.Value);
            var normalizedValue = NormalizeName(value);

            if ((name is "ok" or "passed" or "success") && property.Value.ValueKind == JsonValueKind.False)
            {
                return true;
            }

            if ((name is "estado" or "status") && normalizedValue is "ko" or "error" or "failed" or "fail" or "invalid")
            {
                return true;
            }

            if ((name is "severity" or "severidad") && normalizedValue is "error" or "critical" or "critico")
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildValidationDetail(JsonElement element, string path)
    {
        var message = FindFirstScalar(element, "mensaje", "message", "descripcion", "description", "campo", "field", "name");
        if (!string.IsNullOrWhiteSpace(message))
        {
            return string.IsNullOrWhiteSpace(path) ? message : $"{path}: {message}";
        }

        var compact = Compact(element);
        return string.IsNullOrWhiteSpace(path) ? compact : $"{path}: {compact}";
    }

    private static string Compact(JsonElement? element)
    {
        return element.HasValue ? JsonSerializer.Serialize(element.Value, CompactJsonOptions) : string.Empty;
    }

    private static string ToDisplayValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            _ => JsonSerializer.Serialize(element, CompactJsonOptions)
        };
    }

    private static bool IsContainer(JsonElement element)
    {
        return element.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
    }

    private static string NormalizeName(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private sealed record ValidationSummary(string Total, string Failed, string FailedDetail);
}

public class BatchOutputAuditColumns
{
    public string IdentificacionTipologiaDetectada { get; set; } = string.Empty;
    public string IdentificacionTipoDocumento { get; set; } = string.Empty;
    public string IdentificacionNumeroFinca { get; set; } = string.Empty;
    public string IdentificacionRegistro { get; set; } = string.Empty;
    public string IdentificacionPaginas { get; set; } = string.Empty;
    public string IntegridadEstado { get; set; } = string.Empty;
    public string IntegridadCompleta { get; set; } = string.Empty;
    public string IntegridadObservaciones { get; set; } = string.Empty;
    public string ResultadoEstadoCalidad { get; set; } = string.Empty;
    public string ResultadoConfianzaGlobal { get; set; } = string.Empty;
    public string ResultadoMotivoRevision { get; set; } = string.Empty;
    public string CamposExtraidosResumen { get; set; } = string.Empty;
    public string CamposExtraidosJson { get; set; } = string.Empty;
    public string ConfianzaPorCampoJson { get; set; } = string.Empty;
    public string ValidacionesTotal { get; set; } = string.Empty;
    public string ValidacionesFallidas { get; set; } = string.Empty;
    public string ValidacionesFallidasDetalle { get; set; } = string.Empty;
    public string RevisionRequerida { get; set; } = string.Empty;
    public string RevisionCampos { get; set; } = string.Empty;
    public string RevisionMotivos { get; set; } = string.Empty;
    public string AaiiId { get; set; } = string.Empty;
    public string AaccId { get; set; } = string.Empty;
    public string ParseStatus { get; set; } = string.Empty;
    public string ParseError { get; set; } = string.Empty;

    public IReadOnlyList<string> Values => new[]
    {
        IdentificacionTipologiaDetectada,
        IdentificacionTipoDocumento,
        IdentificacionNumeroFinca,
        IdentificacionRegistro,
        IdentificacionPaginas,
        IntegridadEstado,
        IntegridadCompleta,
        IntegridadObservaciones,
        ResultadoEstadoCalidad,
        ResultadoConfianzaGlobal,
        ResultadoMotivoRevision,
        CamposExtraidosResumen,
        CamposExtraidosJson,
        ConfianzaPorCampoJson,
        ValidacionesTotal,
        ValidacionesFallidas,
        ValidacionesFallidasDetalle,
        RevisionRequerida,
        RevisionCampos,
        RevisionMotivos,
        AaiiId,
        AaccId,
        ParseStatus,
        ParseError
    };

    public static BatchOutputAuditColumns Empty(string status, string error)
    {
        return new BatchOutputAuditColumns
        {
            ParseStatus = status,
            ParseError = error
        };
    }
}
