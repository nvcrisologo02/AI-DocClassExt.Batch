using DocumentIA.Batch.Evaluation.Models;
using Xunit;

namespace DocumentIA.Batch.Evaluation.Tests;

public class DatasetSelectorTests
{
    private static ManifestRow Row(string fileName, bool validated = true, bool inCata100 = false)
        => new()
        {
            FileName = fileName,
            RelPath = "X/" + fileName,
            ExpectedTdn1 = "X",
            ExpectedTdn2 = "X-01",
            Validated = validated,
            InCata100 = inCata100,
            FolderIsTdn1 = true,
            LabelMatchesFolder = true
        };

    [Fact]
    public void FirstShaByte_ValoresConocidos()
    {
        // sha1("doc1.pdf")[0] = 0x0a (10, par); sha1("doc2.pdf")[0] = 0xd9 (217, impar).
        Assert.Equal(0x0a, DatasetSelector.FirstShaByte("doc1.pdf"));
        Assert.Equal(0xd9, DatasetSelector.FirstShaByte("doc2.pdf"));
    }

    [Fact]
    public void HalfA_ContieneSoloBytesPares()
    {
        var full = new List<ManifestRow> { Row("doc1.pdf"), Row("doc2.pdf"), Row("doc3.pdf"), Row("doc4.pdf") };

        var halfA = DatasetSelector.HalfA(full);

        Assert.All(halfA, r => Assert.Equal(0, DatasetSelector.FirstShaByte(r.FileName) % 2));
        Assert.Contains(halfA, r => r.FileName == "doc1.pdf");
        Assert.Contains(halfA, r => r.FileName == "doc4.pdf");
        Assert.DoesNotContain(halfA, r => r.FileName is "doc2.pdf" or "doc3.pdf");
    }

    [Fact]
    public void HalfB_ContieneSoloBytesImpares()
    {
        var full = new List<ManifestRow> { Row("doc1.pdf"), Row("doc2.pdf"), Row("doc3.pdf"), Row("doc4.pdf") };

        var halfB = DatasetSelector.HalfB(full);

        Assert.All(halfB, r => Assert.Equal(1, DatasetSelector.FirstShaByte(r.FileName) % 2));
        Assert.Contains(halfB, r => r.FileName == "doc2.pdf");
        Assert.Contains(halfB, r => r.FileName == "doc3.pdf");
    }

    [Fact]
    public void HalfAyHalfB_SonParticionCompletaYDisjuntaDeFull_YDeterministas()
    {
        var full = Enumerable.Range(1, 40).Select(i => Row($"doc{i}.pdf")).ToList();

        var halfA1 = DatasetSelector.HalfA(full);
        var halfB1 = DatasetSelector.HalfB(full);
        var halfA2 = DatasetSelector.HalfA(full);

        Assert.Equal(full.Count, halfA1.Count + halfB1.Count);
        Assert.Empty(halfA1.Select(r => r.FileName).Intersect(halfB1.Select(r => r.FileName)));
        Assert.Equal(halfA1.Select(r => r.FileName), halfA2.Select(r => r.FileName));
    }

    [Fact]
    public void Full_ExcluyeNoValidadosYCata100()
    {
        var rows = new List<ManifestRow>
        {
            Row("a.pdf", validated: true, inCata100: false),
            Row("b.pdf", validated: false, inCata100: false),
            Row("c.pdf", validated: true, inCata100: true),
        };

        var full = DatasetSelector.Full(rows);

        Assert.Single(full);
        Assert.Equal("a.pdf", full[0].FileName);
    }

    [Fact]
    public void Cata100_SoloValidadosDentroDeCata100()
    {
        var rows = new List<ManifestRow>
        {
            Row("a.pdf", validated: true, inCata100: true),
            Row("b.pdf", validated: false, inCata100: true),
            Row("c.pdf", validated: true, inCata100: false),
        };

        var cata100 = DatasetSelector.Cata100(rows);

        Assert.Single(cata100);
        Assert.Equal("a.pdf", cata100[0].FileName);
    }

    [Theory]
    [InlineData("golden", EvaluationSet.Golden)]
    [InlineData("full", EvaluationSet.Full)]
    [InlineData("half-a", EvaluationSet.HalfA)]
    [InlineData("half-b", EvaluationSet.HalfB)]
    [InlineData("cata100", EvaluationSet.Cata100)]
    [InlineData("GOLDEN", EvaluationSet.Golden)]
    public void ParseSet_ReconoceValoresValidos(string value, EvaluationSet expected)
    {
        Assert.Equal(expected, DatasetSelector.ParseSet(value));
    }

    [Fact]
    public void ParseSet_ValorInvalido_LanzaExcepcionDeUso()
    {
        Assert.Throws<EvaluationUsageException>(() => DatasetSelector.ParseSet("no-existe"));
    }
}
