using System.Globalization;
using System.Text;
using DocumentIA.Batch.Evaluation.Io;
using DocumentIA.Batch.Evaluation.Metrics;
using DocumentIA.Batch.Evaluation.Models;

namespace DocumentIA.Batch.Evaluation.Commands;

public static class ReportCommand
{
    public static int Execute(string[] args)
    {
        var parsed = CliArgs.Parse(args);
        var runDir = Path.GetFullPath(parsed.Require("run"));
        var resultsPath = Path.Combine(runDir, "results.csv");

        if (!File.Exists(resultsPath))
        {
            throw new EvaluationUsageException($"No se encontro results.csv en '{runDir}'.");
        }

        var results = ResultsCsv.Read(resultsPath);
        var evaluated = AccuracyCalculator.Evaluated(results);
        var notEvaluated = AccuracyCalculator.NotEvaluated(results);

        var tdn1 = AccuracyCalculator.Tdn1(results);
        var tdn2 = AccuracyCalculator.Tdn2(results);
        var byTdn1 = AccuracyCalculator.ByTdn1(results);
        var confusion = AccuracyCalculator.Tdn1ConfusionMatrix(results);
        var topConfused = AccuracyCalculator.TopConfusedTdn2(results, 15);

        WriteConfusionCsv(Path.Combine(runDir, "confusion_tdn1.csv"), confusion);
        var reportMd = BuildReportMarkdown(runDir, results.Count, evaluated.Count, notEvaluated.Count, tdn1, tdn2, byTdn1, topConfused, notEvaluated);
        File.WriteAllText(Path.Combine(runDir, "report.md"), reportMd, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine(reportMd);
        Console.WriteLine($"Informe escrito en: {Path.Combine(runDir, "report.md")}");
        Console.WriteLine($"Matriz de confusion TDN1 en: {Path.Combine(runDir, "confusion_tdn1.csv")}");

        return 0;
    }

    private static void WriteConfusionCsv(string path, ConfusionMatrix matrix)
    {
        var header = new[] { "expected_tdn1" }.Concat(matrix.Labels);
        var rows = new List<IEnumerable<string?>>();
        for (var i = 0; i < matrix.Labels.Count; i++)
        {
            var row = new List<string?> { matrix.Labels[i] };
            for (var j = 0; j < matrix.Labels.Count; j++)
            {
                row.Add(matrix[i, j].ToString(CultureInfo.InvariantCulture));
            }

            rows.Add(row);
        }

        CsvUtil.WriteRows(path, header, rows);
    }

    private static string BuildReportMarkdown(
        string runDir,
        int total,
        int evaluatedCount,
        int notEvaluatedCount,
        AccuracyStat tdn1,
        AccuracyStat tdn2,
        IReadOnlyList<(string ExpectedTdn1, AccuracyStat Tdn1, AccuracyStat Tdn2)> byTdn1,
        IReadOnlyList<(string Expected, string Predicted, int Count)> topConfused,
        IReadOnlyList<EvaluationResultRow> notEvaluated)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Informe de evaluacion — {Path.GetFileName(runDir)}");
        sb.AppendLine();
        sb.AppendLine("## Resumen");
        sb.AppendLine();
        sb.AppendLine($"- Total documentos: {total}");
        sb.AppendLine($"- Evaluados (estado OK): {evaluatedCount}");
        sb.AppendLine($"- No evaluados (error/timeout, no cuentan como fallo): {notEvaluatedCount}");
        sb.AppendLine();
        sb.AppendLine("## Accuracy global");
        sb.AppendLine();
        sb.AppendLine("| Nivel | Aciertos | N | Accuracy | IC 95% (±1.96·sqrt(p(1-p)/n)) |");
        sb.AppendLine("|---|---:|---:|---:|---|");
        sb.AppendLine(FormatAccuracyRow("TDN1", tdn1));
        sb.AppendLine(FormatAccuracyRow("TDN2", tdn2));
        sb.AppendLine();
        sb.AppendLine("## Accuracy por TDN1");
        sb.AppendLine();
        sb.AppendLine("| TDN1 | N | Accuracy TDN1 | Accuracy TDN2 |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var row in byTdn1)
        {
            sb.AppendLine(
                $"| {row.ExpectedTdn1} | {row.Tdn1.Total} | "
                + $"{FormatPct(row.Tdn1.Accuracy)} (±{FormatPct(row.Tdn1.CiMargin)}) | "
                + $"{FormatPct(row.Tdn2.Accuracy)} (±{FormatPct(row.Tdn2.CiMargin)}) |");
        }

        sb.AppendLine();
        sb.AppendLine("## Top 15 confusiones TDN2");
        sb.AppendLine();
        if (topConfused.Count == 0)
        {
            sb.AppendLine("Sin confusiones (accuracy TDN2 = 100% sobre los documentos evaluados).");
        }
        else
        {
            sb.AppendLine("| Esperado | Predicho | Casos |");
            sb.AppendLine("|---|---|---:|");
            foreach (var (expected, predicted, count) in topConfused)
            {
                sb.AppendLine($"| {expected} | {predicted} | {count} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Documentos no evaluados (error/timeout)");
        sb.AppendLine();
        if (notEvaluated.Count == 0)
        {
            sb.AppendLine("Ninguno.");
        }
        else
        {
            sb.AppendLine("| rel_path | estado | error |");
            sb.AppendLine("|---|---|---|");
            foreach (var row in notEvaluated)
            {
                sb.AppendLine($"| {row.RelPath} | {row.Estado} | {(row.Error ?? string.Empty).Replace("|", "\\|")} |");
            }
        }

        return sb.ToString();
    }

    private static string FormatAccuracyRow(string label, AccuracyStat stat)
        => $"| {label} | {stat.Correct} | {stat.Total} | {FormatPct(stat.Accuracy)} | ±{FormatPct(stat.CiMargin)} |";

    private static string FormatPct(double value) => value.ToString("P2", CultureInfo.InvariantCulture);
}
