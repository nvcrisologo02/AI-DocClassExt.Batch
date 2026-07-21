using DocumentIA.Batch.Evaluation.Metrics;
using DocumentIA.Batch.Evaluation.Models;
using Xunit;

namespace DocumentIA.Batch.Evaluation.Tests;

public class AccuracyCalculatorTests
{
    private static EvaluationResultRow Ok(string relPath, string expectedTdn1, string expectedTdn2, string predictedTdn1, string predictedTdn2)
        => new()
        {
            RelPath = relPath,
            ExpectedTdn1 = expectedTdn1,
            ExpectedTdn2 = expectedTdn2,
            PredictedTdn1 = predictedTdn1,
            PredictedTdn2 = predictedTdn2,
            Estado = EvaluationEstado.Ok
        };

    private static EvaluationResultRow NotEvaluated(string relPath, string estado)
        => new() { RelPath = relPath, ExpectedTdn1 = "ACTE", ExpectedTdn2 = "ACTE-01", Estado = estado, Error = "boom" };

    private static List<EvaluationResultRow> SampleRows() => new()
    {
        Ok("a1.pdf", "ACTE", "ACTE-01", "ACTE", "ACTE-01"), // tdn1 ok, tdn2 ok
        Ok("a2.pdf", "ACTE", "ACTE-02", "ACTE", "ACTE-01"), // tdn1 ok, tdn2 mal (ACTE-02 -> ACTE-01)
        Ok("a3.pdf", "ACTE", "ACTE-02", "ACTE", "ACTE-01"), // tdn1 ok, tdn2 mal (misma confusion repetida)
        Ok("c1.pdf", "COMU", "COMU-01", "ACTE", "ACTE-01"), // tdn1 mal, tdn2 mal
        NotEvaluated("e1.pdf", EvaluationEstado.Error),
        NotEvaluated("t1.pdf", EvaluationEstado.Timeout),
    };

    [Fact]
    public void Evaluated_ExcluyeErrorYTimeout()
    {
        var evaluated = AccuracyCalculator.Evaluated(SampleRows());

        Assert.Equal(4, evaluated.Count);
    }

    [Fact]
    public void NotEvaluated_SoloErrorYTimeout()
    {
        var notEvaluated = AccuracyCalculator.NotEvaluated(SampleRows());

        Assert.Equal(2, notEvaluated.Count);
        Assert.Contains(notEvaluated, r => r.RelPath == "e1.pdf" && r.Estado == EvaluationEstado.Error);
        Assert.Contains(notEvaluated, r => r.RelPath == "t1.pdf" && r.Estado == EvaluationEstado.Timeout);
    }

    [Fact]
    public void Tdn1_CuentaSoloSobreEvaluados()
    {
        var stat = AccuracyCalculator.Tdn1(SampleRows());

        Assert.Equal(4, stat.Total);
        Assert.Equal(3, stat.Correct);
        Assert.Equal(0.75, stat.Accuracy, precision: 10);
    }

    [Fact]
    public void Tdn2_CuentaSoloSobreEvaluados()
    {
        var stat = AccuracyCalculator.Tdn2(SampleRows());

        Assert.Equal(4, stat.Total);
        Assert.Equal(1, stat.Correct);
        Assert.Equal(0.25, stat.Accuracy, precision: 10);
    }

    [Fact]
    public void CiMargin_SigueLaFormulaWald()
    {
        var stat = new AccuracyStat(3, 4);

        var expectedMargin = 1.96 * Math.Sqrt(0.75 * 0.25 / 4);

        Assert.Equal(expectedMargin, stat.CiMargin, precision: 10);
    }

    [Fact]
    public void ByTdn1_AgrupaPorTdn1Esperado()
    {
        var byTdn1 = AccuracyCalculator.ByTdn1(SampleRows());

        var acte = byTdn1.Single(g => g.ExpectedTdn1 == "ACTE");
        Assert.Equal(3, acte.Tdn1.Total);
        Assert.Equal(3, acte.Tdn1.Correct);
        Assert.Equal(1, acte.Tdn2.Correct);

        var comu = byTdn1.Single(g => g.ExpectedTdn1 == "COMU");
        Assert.Equal(1, comu.Tdn1.Total);
        Assert.Equal(0, comu.Tdn1.Correct);
    }

    [Fact]
    public void Tdn1ConfusionMatrix_CuentaCeldasEsperadoXPredicho()
    {
        var matrix = AccuracyCalculator.Tdn1ConfusionMatrix(SampleRows());

        var acteIndex = matrix.Labels.ToList().IndexOf("ACTE");
        var comuIndex = matrix.Labels.ToList().IndexOf("COMU");

        Assert.Equal(3, matrix[acteIndex, acteIndex]); // ACTE->ACTE x3
        Assert.Equal(1, matrix[comuIndex, acteIndex]); // COMU esperado, ACTE predicho x1
        Assert.Equal(0, matrix[comuIndex, comuIndex]);
    }

    [Fact]
    public void TopConfusedTdn2_OrdenaPorFrecuenciaDescendente()
    {
        var top = AccuracyCalculator.TopConfusedTdn2(SampleRows(), top: 15);

        Assert.Equal(2, top.Count); // ACTE-02->ACTE-01 (x2) y COMU-01->ACTE-01 (x1)
        Assert.Equal(("ACTE-02", "ACTE-01", 2), top[0]);
        Assert.Equal(("COMU-01", "ACTE-01", 1), top[1]);
    }

    [Fact]
    public void TopConfusedTdn2_RespetaElLimiteTop()
    {
        var rows = Enumerable.Range(1, 20)
            .Select(i => Ok($"d{i}.pdf", "ACTE", $"ACTE-{i:00}", "ACTE", $"OTRO-{i:00}"))
            .ToList();

        var top = AccuracyCalculator.TopConfusedTdn2(rows, top: 15);

        Assert.Equal(15, top.Count);
    }
}
