using System.IO;
using System.Text;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.Services;

public class BatchCsvExportService
{
    public void Export(
        string filePath,
        IEnumerable<BatchFileItem> files,
        string tipologia,
        int numeroColas,
        int umbralConfianza,
        bool subirAGdc,
        bool ejecutarConAssetResolver)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(';', BatchExportRows.Headers.Select(Escape)));

        foreach (var row in BatchExportRows.BuildRows(
            files,
            tipologia,
            numeroColas,
            umbralConfianza,
            subirAGdc,
            ejecutarConAssetResolver))
        {
            builder.AppendLine(string.Join(';', row.Select(Escape)));
        }

        File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Escape(string value)
    {
        var normalized = value ?? string.Empty;
        var mustQuote = normalized.Contains(';')
            || normalized.Contains('"')
            || normalized.Contains('\r')
            || normalized.Contains('\n');

        if (!mustQuote)
        {
            return normalized;
        }

        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }
}
