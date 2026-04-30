using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DocumentIA.Batch.Services;

public static class DurableCustomStatusReader
{
    public static BatchActivityProgress Read(JsonElement? customStatus)
    {
        if (!customStatus.HasValue || customStatus.Value.ValueKind != JsonValueKind.Object)
        {
            return BatchActivityProgress.Empty;
        }

        var root = customStatus.Value;
        var activities = ReadActivities(root).ToList();
        var currentActivity = CanonicalizeActivityName(GetString(root, "actividadActual", "ActividadActual"));
        var total = GetInt(root, "actividadesTotales", "ActividadesTotales") ?? activities.Count;
        var completed = GetCompletedCount(root, activities);
        var currentEntry = activities.FirstOrDefault(activity =>
            string.Equals(activity.Name, currentActivity, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(currentActivity))
        {
            currentActivity = activities.LastOrDefault(activity => activity.State == "running")?.Name
                ?? activities.LastOrDefault(activity => activity.State == "completed")?.Name
                ?? string.Empty;
        }

        var message = currentEntry?.Message ?? GetString(root, "mensaje", "Mensaje");
        var durationMs = currentEntry?.DurationMs;
        var state = currentEntry?.State ?? NormalizeState(GetString(root, "estado", "Estado"));

        return new BatchActivityProgress(
            currentActivity,
            state,
            total,
            completed,
            FormatProgress(completed, total),
            BuildDetail(message, durationMs));
    }

    private static IEnumerable<ActivityEntry> ReadActivities(JsonElement root)
    {
        if (!TryGetProperty(root, "actividades", out var activities)
            && !TryGetProperty(root, "Actividades", out activities))
        {
            yield break;
        }

        if (activities.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var activity in activities.EnumerateArray())
        {
            if (activity.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = CanonicalizeActivityName(GetString(activity, "nombre", "Nombre"));
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            yield return new ActivityEntry(
                name,
                NormalizeState(GetString(activity, "estado", "Estado")),
                GetLong(activity, "duracionMs", "DuracionMs"),
                GetString(activity, "mensaje", "Mensaje"));
        }
    }

    private static int GetCompletedCount(JsonElement root, IReadOnlyList<ActivityEntry> activities)
    {
        if ((TryGetProperty(root, "actividadesCompletadas", out var completed)
                || TryGetProperty(root, "ActividadesCompletadas", out completed))
            && completed.ValueKind == JsonValueKind.Array)
        {
            return completed.GetArrayLength();
        }

        return activities.Count(activity => activity.State == "completed" || activity.State == "skipped");
    }

    private static string FormatProgress(int completed, int total)
    {
        return total > 0 ? $"{Math.Min(completed, total)}/{total}" : string.Empty;
    }

    private static string BuildDetail(string message, long? durationMs)
    {
        var detail = message ?? string.Empty;
        if (durationMs.HasValue && durationMs.Value > 0)
        {
            var duration = TimeSpan.FromMilliseconds(durationMs.Value);
            var durationText = duration.TotalMinutes >= 1
                ? duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture)
                : $"{duration.TotalSeconds:0.#}s";

            detail = string.IsNullOrWhiteSpace(detail) ? durationText : $"{detail} ({durationText})";
        }

        return detail;
    }

    private static string NormalizeState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return string.Empty;
        }

        var normalized = RemoveDiacritics(state).Trim().ToLowerInvariant();
        return normalized switch
        {
            "completed" => "completed",
            "completado" => "completed",
            "failed" => "failed",
            "error" => "failed",
            "running" => "running",
            "inprogress" => "running",
            "enproceso" => "running",
            "skipped" => "skipped",
            "omitido" => "skipped",
            "omitted" => "skipped",
            _ => normalized
        };
    }

    private static string CanonicalizeActivityName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var normalized = RemoveDiacritics(name).Trim().ToLowerInvariant();
        normalized = normalized.Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);

        return normalized switch
        {
            "prompt" => "Prompt",
            "clasificar" => "Clasificar",
            "clasificacion" => "Clasificar",
            "extraer" => "Extraer",
            "extraccion" => "Extraer",
            "validar" => "Validar",
            "validacion" => "Validar",
            "assetresolver" => "AssetResolver",
            "obteneractivo" => "AssetResolver",
            "integrar" => "Integrar",
            "normalizacion" => "Integrar",
            "subirgdc" => "SubirGDC",
            "uploadgdc" => "SubirGDC",
            "persistir" => "Persistir",
            "persistencia" => "Persistir",
            "resultado" => "Resultado",
            _ => name.Trim()
        };
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                continue;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
        }

        return string.Empty;
    }

    private static int? GetInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static long? GetLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed record ActivityEntry(string Name, string State, long? DurationMs, string Message);
}

public sealed record BatchActivityProgress(
    string ActivityName,
    string State,
    int TotalActivities,
    int CompletedActivities,
    string ProgressText,
    string Detail)
{
    public static BatchActivityProgress Empty { get; } = new(string.Empty, string.Empty, 0, 0, string.Empty, string.Empty);
}
