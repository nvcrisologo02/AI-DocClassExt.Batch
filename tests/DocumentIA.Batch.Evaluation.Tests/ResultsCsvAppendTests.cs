using System.IO;
using DocumentIA.Batch.Evaluation.Io;
using DocumentIA.Batch.Evaluation.Models;
using Xunit;

namespace DocumentIA.Batch.Evaluation.Tests;

public class ResultsCsvAppendTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), "results-append-" + Guid.NewGuid().ToString("N") + ".csv");

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
    }

    private static EvaluationResultRow Row(string relPath, string estado) => new()
    {
        RelPath = relPath,
        FileName = relPath.Split('/')[^1],
        ExpectedTdn1 = "ACTE",
        ExpectedTdn2 = "ACTE-01",
        PredictedTdn1 = estado == EvaluationEstado.Ok ? "ACTE" : null,
        Confianza = estado == EvaluationEstado.Ok ? 0.9 : null,
        Estado = estado,
        CorrelationId = "corr-" + relPath
    };

    [Fact]
    public void Append_CreaElFicheroConCabeceraYLuegoSoloAnadeFilas()
    {
        ResultsCsv.Append(_tempFile, Row("ACTE/a.pdf", EvaluationEstado.Ok));
        ResultsCsv.Append(_tempFile, Row("ACTE/b.pdf", EvaluationEstado.Error));

        var lines = File.ReadAllLines(_tempFile);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("rel_path;", lines[0].TrimStart('﻿'));

        var read = ResultsCsv.Read(_tempFile);
        Assert.Equal(2, read.Count);
        Assert.Equal("ACTE/a.pdf", read[0].RelPath);
        Assert.Equal(0.9, read[0].Confianza);
        Assert.Equal(EvaluationEstado.Error, read[1].Estado);
        Assert.Equal("corr-ACTE/b.pdf", read[1].CorrelationId);
    }

    [Fact]
    public void Append_SobreFicheroEscritoConWrite_ConservaLasFilasPrevias()
    {
        ResultsCsv.Write(_tempFile, new[] { Row("ACTE/a.pdf", EvaluationEstado.Ok) });

        ResultsCsv.Append(_tempFile, Row("ACTE/b.pdf", EvaluationEstado.Ok));

        var read = ResultsCsv.Read(_tempFile);
        Assert.Equal(new[] { "ACTE/a.pdf", "ACTE/b.pdf" }, read.Select(r => r.RelPath));
    }
}
