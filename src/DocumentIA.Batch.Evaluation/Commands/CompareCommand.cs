using System.Globalization;
using System.Text;
using DocumentIA.Batch.Evaluation.Io;
using DocumentIA.Batch.Evaluation.Metrics;
using DocumentIA.Batch.Evaluation.Models;

namespace DocumentIA.Batch.Evaluation.Commands;

public static class CompareCommand
{
    private const double SignificanceThreshold = 0.05;

    public static int Execute(string[] args)
    {
        var parsed = CliArgs.Parse(args);
        var dirA = Path.GetFullPath(parsed.Require("a"));
        var dirB = Path.GetFullPath(parsed.Require("b"));

        var resultsA = ReadResults(dirA);
        var resultsB = ReadResults(dirB);

        var byPathA = resultsA.Where(r => r.IsEvaluated).ToDictionary(r => r.RelPath, StringComparer.OrdinalIgnoreCase);
        var byPathB = resultsB.Where(r => r.IsEvaluated).ToDictionary(r => r.RelPath, StringComparer.OrdinalIgnoreCase);

        var common = byPathA.Keys.Intersect(byPathB.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        if (common.Count == 0)
        {
            throw new EvaluationUsageException(
                $"No hay documentos evaluados (estado OK) en comun por rel_path entre '{dirA}' y '{dirB}'.");
        }

        var tdn1 = ComputeComparison(common, byPathA, byPathB, r => r.Tdn1Correct);
        var tdn2 = ComputeComparison(common, byPathA, byPathB, r => r.Tdn2Correct);

        var markdown = BuildMarkdown(dirA, dirB, common.Count, tdn1, tdn2);

        // Se archiva junto al resto de runs (eval/runs/compare-<A>-vs-<B>/) para no dispersar
        // salidas por el directorio desde el que se invoque la consola.
        var evalDir = EvalPaths.FindEvalDirectory();
        var compareDir = Path.Combine(evalDir, "runs", $"compare-{Path.GetFileName(dirA)}-vs-{Path.GetFileName(dirB)}");
        Directory.CreateDirectory(compareDir);
        var comparePath = Path.Combine(compareDir, "compare.md");
        File.WriteAllText(comparePath, markdown, new UTF8Encoding(false));

        Console.WriteLine(markdown);
        Console.WriteLine($"compare.md escrito en: {comparePath}");

        return 0;
    }

    private static List<EvaluationResultRow> ReadResults(string runDir)
    {
        var path = Path.Combine(runDir, "results.csv");
        if (!File.Exists(path))
        {
            throw new EvaluationUsageException($"No se encontro results.csv en '{runDir}'.");
        }

        return ResultsCsv.Read(path);
    }

    private static ComparisonResult ComputeComparison(
        IReadOnlyList<string> common,
        IReadOnlyDictionary<string, EvaluationResultRow> byPathA,
        IReadOnlyDictionary<string, EvaluationResultRow> byPathB,
        Func<EvaluationResultRow, bool> isCorrect)
    {
        var b01 = 0; // A acierta, B falla
        var b10 = 0; // A falla, B acierta
        var correctA = 0;
        var correctB = 0;

        foreach (var relPath in common)
        {
            var a = isCorrect(byPathA[relPath]);
            var b = isCorrect(byPathB[relPath]);
            if (a)
            {
                correctA++;
            }

            if (b)
            {
                correctB++;
            }

            if (a && !b)
            {
                b01++;
            }
            else if (!a && b)
            {
                b10++;
            }
        }

        var n = common.Count;
        var pValue = McNemarTest.ExactTwoSidedPValue(b01, b10);
        return new ComparisonResult(
            new AccuracyStat(correctA, n),
            new AccuracyStat(correctB, n),
            b01,
            b10,
            pValue,
            Verdict(b01, b10, pValue));
    }

    private static string Verdict(int b01, int b10, double pValue)
    {
        if (pValue >= SignificanceThreshold)
        {
            return "diferencia dentro del ruido";
        }

        return b10 > b01 ? "mejora significativa" : "empeora significativa";
    }

    private static string BuildMarkdown(string dirA, string dirB, int commonCount, ComparisonResult tdn1, ComparisonResult tdn2)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Comparativa de ejecuciones");
        sb.AppendLine();
        sb.AppendLine($"- A: `{dirA}`");
        sb.AppendLine($"- B: `{dirB}`");
        sb.AppendLine($"- Documentos evaluados en comun (interseccion por rel_path): {commonCount}");
        sb.AppendLine();
        sb.AppendLine(BuildLevelSection("TDN1", tdn1));
        sb.AppendLine(BuildLevelSection("TDN2", tdn2));
        sb.AppendLine("## Veredicto global");
        sb.AppendLine();
        sb.AppendLine(
            $"Basado en TDN2 (nivel mas fino): **{tdn2.Verdict}** (p={tdn2.PValue.ToString("F4", CultureInfo.InvariantCulture)}, "
            + $"umbral p<0.05). TDN1: **{tdn1.Verdict}** (p={tdn1.PValue.ToString("F4", CultureInfo.InvariantCulture)}).");

        return sb.ToString();
    }

    private static string BuildLevelSection(string label, ComparisonResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {label}");
        sb.AppendLine();
        sb.AppendLine($"- Accuracy A: {result.AccuracyA.Accuracy.ToString("P2", CultureInfo.InvariantCulture)} ({result.AccuracyA.Correct}/{result.AccuracyA.Total})");
        sb.AppendLine($"- Accuracy B: {result.AccuracyB.Accuracy.ToString("P2", CultureInfo.InvariantCulture)} ({result.AccuracyB.Correct}/{result.AccuracyB.Total})");
        sb.AppendLine($"- b01 (A acierta, B falla): {result.B01}");
        sb.AppendLine($"- b10 (A falla, B acierta): {result.B10}");
        sb.AppendLine($"- McNemar exacto, p-valor de dos colas: {result.PValue.ToString("F4", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"- Veredicto: **{result.Verdict}**");
        sb.AppendLine();
        return sb.ToString();
    }

    private sealed record ComparisonResult(
        AccuracyStat AccuracyA,
        AccuracyStat AccuracyB,
        int B01,
        int B10,
        double PValue,
        string Verdict);
}
