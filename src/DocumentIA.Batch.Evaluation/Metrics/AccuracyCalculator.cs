using DocumentIA.Batch.Evaluation.Models;

namespace DocumentIA.Batch.Evaluation.Metrics;

/// <summary>
/// Calculo de accuracy/matriz de confusion sobre resultados de evaluacion. Solo las filas
/// evaluadas (estado OK) entran en el denominador: error/timeout no cuentan como fallo de
/// clasificacion, son "no evaluados" y se reportan aparte.
/// </summary>
public static class AccuracyCalculator
{
    public static IReadOnlyList<EvaluationResultRow> Evaluated(IReadOnlyList<EvaluationResultRow> rows)
        => rows.Where(r => r.IsEvaluated).ToList();

    public static IReadOnlyList<EvaluationResultRow> NotEvaluated(IReadOnlyList<EvaluationResultRow> rows)
        => rows.Where(r => !r.IsEvaluated).ToList();

    public static AccuracyStat Tdn1(IReadOnlyList<EvaluationResultRow> rows)
    {
        var evaluated = Evaluated(rows);
        return new AccuracyStat(evaluated.Count(r => r.Tdn1Correct), evaluated.Count);
    }

    public static AccuracyStat Tdn2(IReadOnlyList<EvaluationResultRow> rows)
    {
        var evaluated = Evaluated(rows);
        return new AccuracyStat(evaluated.Count(r => r.Tdn2Correct), evaluated.Count);
    }

    public static IReadOnlyList<(string ExpectedTdn1, AccuracyStat Tdn1, AccuracyStat Tdn2)> ByTdn1(IReadOnlyList<EvaluationResultRow> rows)
    {
        return Evaluated(rows)
            .GroupBy(r => r.ExpectedTdn1, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                ExpectedTdn1: g.Key,
                Tdn1: new AccuracyStat(g.Count(r => r.Tdn1Correct), g.Count()),
                Tdn2: new AccuracyStat(g.Count(r => r.Tdn2Correct), g.Count())))
            .ToList();
    }

    public static ConfusionMatrix Tdn1ConfusionMatrix(IReadOnlyList<EvaluationResultRow> rows)
    {
        var evaluated = Evaluated(rows);
        var labels = evaluated
            .Select(r => r.ExpectedTdn1)
            .Concat(evaluated.Select(r => r.PredictedTdn1 ?? string.Empty))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < labels.Count; i++)
        {
            index[labels[i]] = i;
        }

        var counts = new int[labels.Count, labels.Count];
        foreach (var r in evaluated)
        {
            var i = index[r.ExpectedTdn1];
            var j = index[r.PredictedTdn1 ?? string.Empty];
            counts[i, j]++;
        }

        return new ConfusionMatrix(labels, counts);
    }

    public static IReadOnlyList<(string Expected, string Predicted, int Count)> TopConfusedTdn2(
        IReadOnlyList<EvaluationResultRow> rows, int top = 15)
    {
        return Evaluated(rows)
            .Where(r => !r.Tdn2Correct)
            .GroupBy(r => (Expected: r.ExpectedTdn2, Predicted: r.PredictedTdn2 ?? string.Empty))
            .Select(g => (g.Key.Expected, g.Key.Predicted, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Expected, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Predicted, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .ToList();
    }
}
