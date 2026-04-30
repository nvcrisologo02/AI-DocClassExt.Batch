using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DocumentIA.Batch.Services;

public static class BatchOutputJsonReader
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false
    };

    public static JsonDocument? LoadDocument(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static string GetPathValue(JsonElement root, string path)
    {
        return TryGetPath(root, path, out var value) ? FormatCellValue(value) : string.Empty;
    }

    public static double? GetDoublePathValue(JsonElement root, string path)
    {
        if (!TryGetPath(root, path, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        var text = FormatCellValue(value);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static IReadOnlyList<string> GetObjectPropertyNames(JsonElement root, string path)
    {
        if (!TryGetPath(root, path, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
    }

    public static bool TryGetPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !TryGetProperty(value, segment, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    public static string FormatCellValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            JsonValueKind.Object or JsonValueKind.Array => JsonSerializer.Serialize(value, CompactJsonOptions),
            _ => value.GetRawText()
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

        return false;
    }
}
