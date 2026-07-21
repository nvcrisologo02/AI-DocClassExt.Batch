using System.Globalization;
using DocumentIA.Batch.Evaluation.Models;

namespace DocumentIA.Batch.Evaluation.Io;

/// <summary>Lectura/escritura de results.csv (';' UTF-8 BOM), contrato fijo de columnas.</summary>
public static class ResultsCsv
{
    private static readonly string[] Header =
    {
        "rel_path", "file_name", "expected_tdn1", "expected_tdn2",
        "predicted_tdn1", "predicted_tdn2", "confianza", "proveedor",
        "fallback", "duracion_ms", "estado", "error"
    };

    public static void Write(string path, IEnumerable<EvaluationResultRow> rows)
    {
        var lines = rows.Select(r => (IEnumerable<string?>)new[]
        {
            r.RelPath,
            r.FileName,
            r.ExpectedTdn1,
            r.ExpectedTdn2,
            r.PredictedTdn1,
            r.PredictedTdn2,
            r.Confianza?.ToString("F4", CultureInfo.InvariantCulture),
            r.Proveedor,
            r.Fallback ? "True" : "False",
            r.DuracionMs.ToString(CultureInfo.InvariantCulture),
            r.Estado,
            r.Error
        });

        CsvUtil.WriteRows(path, Header, lines);
    }

    public static List<EvaluationResultRow> Read(string path)
    {
        var rows = CsvUtil.ReadRows(path);
        if (rows.Count == 0)
        {
            return new List<EvaluationResultRow>();
        }

        var header = rows[0];
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            index[header[i].TrimStart('﻿').Trim()] = i;
        }

        var result = new List<EvaluationResultRow>(rows.Count - 1);
        for (var i = 1; i < rows.Count; i++)
        {
            var f = rows[i];
            result.Add(new EvaluationResultRow
            {
                RelPath = Get(f, index, "rel_path"),
                FileName = Get(f, index, "file_name"),
                ExpectedTdn1 = Get(f, index, "expected_tdn1"),
                ExpectedTdn2 = Get(f, index, "expected_tdn2"),
                PredictedTdn1 = NullIfEmpty(Get(f, index, "predicted_tdn1")),
                PredictedTdn2 = NullIfEmpty(Get(f, index, "predicted_tdn2")),
                Confianza = ParseNullableDouble(Get(f, index, "confianza")),
                Proveedor = NullIfEmpty(Get(f, index, "proveedor")),
                Fallback = string.Equals(Get(f, index, "fallback"), "True", StringComparison.OrdinalIgnoreCase),
                DuracionMs = long.TryParse(Get(f, index, "duracion_ms"), out var ms) ? ms : 0,
                Estado = Get(f, index, "estado"),
                Error = NullIfEmpty(Get(f, index, "error"))
            });
        }

        return result;
    }

    private static string Get(string[] fields, Dictionary<string, int> index, string column)
        => index.TryGetValue(column, out var i) && i < fields.Length ? fields[i] : string.Empty;

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static double? ParseNullableDouble(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
