using DocumentIA.Batch.Evaluation.Models;

namespace DocumentIA.Batch.Evaluation.Io;

/// <summary>
/// Lee una lista de documentos fuera del manifest (--set list --list &lt;csv&gt;): columnas
/// rel_path;expected_tdn1;expected_tdn2 mapeadas por nombre, ';' y UTF-8 con BOM como el resto
/// de eval/. El nombre de fichero y la carpeta TDN1 se derivan del rel_path; las filas se
/// consideran validadas porque la lista ya viene filtrada por quien la genera.
/// </summary>
public static class ListCsvReader
{
    private const string ColRelPath = "rel_path";
    private const string ColExpectedTdn1 = "expected_tdn1";
    private const string ColExpectedTdn2 = "expected_tdn2";

    public static List<ManifestRow> Read(string path)
    {
        var rows = CsvUtil.ReadRows(path);
        if (rows.Count == 0)
        {
            return new List<ManifestRow>();
        }

        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows[0].Length; i++)
        {
            index[rows[0][i].TrimStart('﻿').Trim()] = i;
        }

        if (!index.ContainsKey(ColRelPath))
        {
            throw new EvaluationUsageException($"La lista '{path}' no tiene la columna '{ColRelPath}'.");
        }

        var result = new List<ManifestRow>(rows.Count - 1);
        for (var i = 1; i < rows.Count; i++)
        {
            var fields = rows[i];
            var relPath = Get(fields, index, ColRelPath).Replace('\\', '/');
            if (relPath.Length == 0)
            {
                continue;
            }

            var segments = relPath.Split('/');
            result.Add(new ManifestRow
            {
                Tdn1Folder = segments.Length > 1 ? segments[0] : string.Empty,
                FileName = segments[^1],
                RelPath = relPath,
                ExpectedTdn1 = Get(fields, index, ColExpectedTdn1),
                ExpectedTdn2 = Get(fields, index, ColExpectedTdn2),
                Validated = true,
                FolderIsTdn1 = true,
                LabelMatchesFolder = true
            });
        }

        return result;
    }

    private static string Get(string[] fields, Dictionary<string, int> index, string column)
        => index.TryGetValue(column, out var i) && i < fields.Length ? fields[i] : string.Empty;
}
