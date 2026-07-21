using DocumentIA.Batch.Evaluation.Models;

namespace DocumentIA.Batch.Evaluation.Io;

/// <summary>
/// Lee eval/manifest.csv y eval/golden.csv (columnas identicas, generadas por
/// eval/build_manifest.py). El mapeo es por nombre de columna (no por posicion) para tolerar
/// reordenaciones futuras del script.
/// </summary>
public static class ManifestCsvReader
{
    private const string ColTdn1Folder = "tdn1_folder";
    private const string ColFileName = "file_name";
    private const string ColRelPath = "rel_path";
    private const string ColSizeBytes = "size_bytes";
    private const string ColMtimeUtc = "mtime_utc";
    private const string ColExpectedTdn1 = "expected_tdn1";
    private const string ColExpectedTdn2 = "expected_tdn2";
    private const string ColValidated = "validated";
    private const string ColFolderIsTdn1 = "folder_is_tdn1";
    private const string ColInCata100 = "in_cata100";
    private const string ColLabelMatchesFolder = "label_matches_folder";

    public static List<ManifestRow> Read(string path)
    {
        var rows = CsvUtil.ReadRows(path);
        if (rows.Count == 0)
        {
            return new List<ManifestRow>();
        }

        var header = rows[0];
        var index = BuildIndex(header);
        var result = new List<ManifestRow>(rows.Count - 1);

        for (var i = 1; i < rows.Count; i++)
        {
            var fields = rows[i];
            result.Add(new ManifestRow
            {
                Tdn1Folder = Get(fields, index, ColTdn1Folder),
                FileName = Get(fields, index, ColFileName),
                RelPath = Get(fields, index, ColRelPath),
                SizeBytes = ParseLong(Get(fields, index, ColSizeBytes)),
                MtimeUtc = Get(fields, index, ColMtimeUtc),
                ExpectedTdn1 = Get(fields, index, ColExpectedTdn1),
                ExpectedTdn2 = Get(fields, index, ColExpectedTdn2),
                Validated = ParseBool(Get(fields, index, ColValidated)),
                FolderIsTdn1 = ParseBool(Get(fields, index, ColFolderIsTdn1)),
                InCata100 = ParseBool(Get(fields, index, ColInCata100)),
                LabelMatchesFolder = ParseBool(Get(fields, index, ColLabelMatchesFolder))
            });
        }

        return result;
    }

    private static Dictionary<string, int> BuildIndex(string[] header)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            // El primer campo de la cabecera puede llevar el BOM UTF-8 si el StreamReader no lo
            // detecto como marca de encoding (defensivo: TrimStart del caracter BOM).
            var name = header[i].TrimStart('﻿').Trim();
            index[name] = i;
        }

        return index;
    }

    private static string Get(string[] fields, Dictionary<string, int> index, string column)
    {
        if (!index.TryGetValue(column, out var i) || i >= fields.Length)
        {
            return string.Empty;
        }

        return fields[i];
    }

    private static bool ParseBool(string value)
        => string.Equals(value, "True", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.Ordinal);

    private static long ParseLong(string value)
        => long.TryParse(value, out var parsed) ? parsed : 0;
}
