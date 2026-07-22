using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace DocumentIA.Batch.ClassificationLite.Engine;

public class LiteClassificationResult
{
    public string? Tdn1 { get; set; }
    public string? Tdn2 { get; set; }
    public double? Confidence { get; set; }
    public int? Pages { get; set; }
    public string? PagesIncluded { get; set; }
    public string? ProcessDate { get; set; }
    public long? DurationMs { get; set; }
    public string? Estado { get; set; }
    public string? Summary { get; set; }
}

public static class LiteResultParser
{
    public static LiteClassificationResult Parse(JsonElement output)
    {
        var identificacion = GetProperty(output, "Identificacion", "identificacion");
        var resultado = GetProperty(output, "Resultado", "resultado");
        var detalle = GetProperty(output, "DetalleEjecucion", "detalleEjecucion");
        var clasificacion = detalle.HasValue ? GetProperty(detalle.Value, "Clasificacion", "clasificacion") : null;
        var seguimiento = detalle.HasValue ? GetProperty(detalle.Value, "Seguimiento", "seguimiento") : null;
        var datosExtraidos = GetProperty(output, "DatosExtraidos", "datosExtraidos");

        var tdn2 = FirstNonEmpty(
            GetString(identificacion, "Tdn2", "tdn2"),
            GetString(clasificacion, "Tdn2Detectado", "tdn2Detectado"));

        return new LiteClassificationResult
        {
            // El backend solo emite Identificacion.Tdn1 en clasificaciones parciales o
            // en nivel TDN1; en el camino de éxito completo llega null. Como la familia
            // TDN1 es el prefijo del TDN2 (COMU-69 -> COMU), la derivamos como fallback.
            Tdn1 = FirstNonEmpty(
                GetString(identificacion, "Tdn1", "tdn1"),
                DeriveTdn1FromTdn2(tdn2)),
            Tdn2 = tdn2,
            Confidence = GetDouble(clasificacion, "Confianza", "confianza")
                ?? GetDouble(clasificacion, "ConfianzaGPT", "confianzaGPT")
                ?? GetDouble(resultado, "ConfianzaGlobal", "confianzaGlobal"),
            Pages = (int?)GetLong(identificacion, "Paginas", "paginas"),
            PagesIncluded = GetString(detalle, "PaginasIncluidas", "paginasIncluidas"),
            ProcessDate = GetString(identificacion, "FechaProceso", "fechaProceso"),
            DurationMs = GetLong(seguimiento, "DuracionTotalMs", "duracionTotalMs"),
            Estado = GetString(resultado, "Estado", "estado"),
            Summary = FirstNonEmpty(
                GetString(datosExtraidos, "Resumen", "resumen"),
                GetString(clasificacion, "ResumenCombinado", "resumenCombinado"))
        };
    }

    private static JsonElement? GetProperty(JsonElement source, params string[] names)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (source.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement? source, params string[] names)
    {
        if (!source.HasValue)
        {
            return null;
        }

        var property = GetProperty(source.Value, names);
        if (!property.HasValue)
        {
            return null;
        }

        return property.Value.ValueKind switch
        {
            JsonValueKind.String => property.Value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
            _ => null
        };
    }

    private static double? GetDouble(JsonElement? source, params string[] names)
    {
        if (!source.HasValue)
        {
            return null;
        }

        var property = GetProperty(source.Value, names);
        if (!property.HasValue)
        {
            return null;
        }

        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var number))
        {
            return number;
        }

        if (property.Value.ValueKind == JsonValueKind.String
            && double.TryParse(property.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static long? GetLong(JsonElement? source, params string[] names)
    {
        if (!source.HasValue)
        {
            return null;
        }

        var property = GetProperty(source.Value, names);
        if (!property.HasValue)
        {
            return null;
        }

        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var number))
        {
            return number;
        }

        if (property.Value.ValueKind == JsonValueKind.String
            && long.TryParse(property.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// Estados de resultado del backend que indican que la clasificación no se completó y
    /// debe reintentarse (a diferencia de OK o BAJA_CONFIANZA_CLASIFICACION, que son válidos).
    /// </summary>
    public static bool IsRetryableBackendFailure(string? estado)
        => !string.IsNullOrWhiteSpace(estado)
           && (estado.Equals("PENDIENTE_REINTENTO", StringComparison.OrdinalIgnoreCase)
               || estado.Equals("ERROR", StringComparison.OrdinalIgnoreCase));

    private static string? DeriveTdn1FromTdn2(string? tdn2)
    {
        if (string.IsNullOrWhiteSpace(tdn2))
        {
            return null;
        }

        var separatorIndex = tdn2.IndexOfAny(new[] { '-', '.' });
        return separatorIndex > 0 ? tdn2[..separatorIndex] : tdn2;
    }
}
