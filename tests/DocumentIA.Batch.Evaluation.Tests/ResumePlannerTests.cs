using DocumentIA.Batch.Evaluation.Models;
using Xunit;

namespace DocumentIA.Batch.Evaluation.Tests;

public class ResumePlannerTests
{
    private static ManifestRow Doc(string relPath) => new()
    {
        RelPath = relPath,
        FileName = relPath.Split('/')[^1],
        ExpectedTdn1 = relPath.Split('/')[0],
        Validated = true
    };

    private static EvaluationResultRow Result(string relPath, string estado, string? tdn1 = null) => new()
    {
        RelPath = relPath,
        FileName = relPath.Split('/')[^1],
        Estado = estado,
        PredictedTdn1 = tdn1
    };

    [Fact]
    public void Pending_SaltaSoloLosOkDeLaPasadaAnterior()
    {
        var docs = new List<ManifestRow> { Doc("A/1.pdf"), Doc("A/2.pdf"), Doc("B/3.pdf"), Doc("B/4.pdf") };
        var previous = new List<EvaluationResultRow>
        {
            Result("A/1.pdf", EvaluationEstado.Ok, "A"),
            Result("A/2.pdf", EvaluationEstado.Error),
            Result("B/3.pdf", EvaluationEstado.Timeout)
        };

        var pending = ResumePlanner.Pending(docs, previous);

        Assert.Equal(new[] { "A/2.pdf", "B/3.pdf", "B/4.pdf" }, pending.Select(d => d.RelPath));
    }

    [Fact]
    public void Pending_ReintentaLosOkSinTipologiaPorRateLimit()
    {
        var docs = new List<ManifestRow> { Doc("A/1.pdf"), Doc("A/2.pdf") };
        var limited = Result("A/1.pdf", EvaluationEstado.Ok);
        limited.RateLimit = true;
        var previous = new List<EvaluationResultRow> { limited, Result("A/2.pdf", EvaluationEstado.Ok, "A") };

        var pending = ResumePlanner.Pending(docs, previous);

        Assert.Equal(new[] { "A/1.pdf" }, pending.Select(d => d.RelPath));
    }

    [Fact]
    public void Pending_ComparaRelPathSinDistinguirMayusculas()
    {
        var docs = new List<ManifestRow> { Doc("A/Doc.PDF") };
        var previous = new List<EvaluationResultRow> { Result("a/doc.pdf", EvaluationEstado.Ok, "A") };

        Assert.Empty(ResumePlanner.Pending(docs, previous));
    }

    [Fact]
    public void Merge_ConservaElOrdenDelSetYLaPasadaNuevaPisaLaAnterior()
    {
        var docs = new List<ManifestRow> { Doc("A/1.pdf"), Doc("A/2.pdf"), Doc("B/3.pdf") };
        var previous = new List<EvaluationResultRow>
        {
            Result("B/3.pdf", EvaluationEstado.Error),
            Result("A/1.pdf", EvaluationEstado.Ok, "A")
        };
        var fresh = new List<EvaluationResultRow>
        {
            Result("B/3.pdf", EvaluationEstado.Ok, "B"),
            Result("A/2.pdf", EvaluationEstado.Ok, "A")
        };

        var merged = ResumePlanner.Merge(docs, previous, fresh);

        Assert.Equal(new[] { "A/1.pdf", "A/2.pdf", "B/3.pdf" }, merged.Select(r => r.RelPath));
        Assert.Equal("B", merged[2].PredictedTdn1);
        Assert.Equal(EvaluationEstado.Ok, merged[2].Estado);
    }

    [Fact]
    public void Merge_DocumentoSinResultadoEnNingunaPasada_SeOmite()
    {
        var docs = new List<ManifestRow> { Doc("A/1.pdf"), Doc("A/2.pdf") };
        var previous = new List<EvaluationResultRow>();
        var fresh = new List<EvaluationResultRow> { Result("A/1.pdf", EvaluationEstado.Ok, "A") };

        var merged = ResumePlanner.Merge(docs, previous, fresh);

        Assert.Single(merged);
        Assert.Equal("A/1.pdf", merged[0].RelPath);
    }
}
