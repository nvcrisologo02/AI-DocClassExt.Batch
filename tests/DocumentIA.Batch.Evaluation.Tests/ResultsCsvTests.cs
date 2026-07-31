using System.IO;
using DocumentIA.Batch.Evaluation.Io;
using DocumentIA.Batch.Evaluation.Models;
using Xunit;

namespace DocumentIA.Batch.Evaluation.Tests;

public class ResultsCsvTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), "results-test-" + Guid.NewGuid().ToString("N") + ".csv");

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
    }

    [Fact]
    public void WriteYRead_HacenRoundTrip()
    {
        var rows = new List<EvaluationResultRow>
        {
            new()
            {
                RelPath = "ACTE/a.pdf",
                FileName = "a.pdf",
                ExpectedTdn1 = "ACTE",
                ExpectedTdn2 = "ACTE-01",
                PredictedTdn1 = "ACTE",
                PredictedTdn2 = "ACTE-01",
                Confianza = 0.87,
                Proveedor = "gpt-4.1",
                Fallback = false,
                DuracionMs = 4321,
                Estado = EvaluationEstado.Ok,
                Error = null,
                EstadoContrato = "OK",
                RateLimit = false,
                OrigenMarkdown = "LayoutPreClasificacion",
                CorrelationId = "corr-a"
            },
            new()
            {
                RelPath = "COMU/b.pdf",
                FileName = "b.pdf",
                ExpectedTdn1 = "COMU",
                ExpectedTdn2 = "COMU-01",
                PredictedTdn1 = null,
                PredictedTdn2 = null,
                Confianza = null,
                Proveedor = null,
                Fallback = false,
                DuracionMs = 0,
                Estado = EvaluationEstado.Timeout,
                Error = "Timeout esperando el resultado (30 min).",
                EstadoContrato = "PENDIENTE_REINTENTO",
                RateLimit = true,
                OrigenMarkdown = "MarkdownPersistidoBD",
                CorrelationId = "corr-b"
            }
        };

        ResultsCsv.Write(_tempFile, rows);
        var read = ResultsCsv.Read(_tempFile);

        Assert.Equal(2, read.Count);
        Assert.Equal("ACTE/a.pdf", read[0].RelPath);
        Assert.Equal(0.87, read[0].Confianza);
        Assert.Equal("gpt-4.1", read[0].Proveedor);
        Assert.False(read[0].Fallback);
        Assert.Equal(EvaluationEstado.Ok, read[0].Estado);
        Assert.Equal("OK", read[0].EstadoContrato);
        Assert.False(read[0].RateLimit);
        Assert.Equal("LayoutPreClasificacion", read[0].OrigenMarkdown);
        Assert.Equal("corr-a", read[0].CorrelationId);

        Assert.Equal(EvaluationEstado.Timeout, read[1].Estado);
        Assert.Null(read[1].PredictedTdn1);
        Assert.Null(read[1].Confianza);
        Assert.Contains("Timeout", read[1].Error);
        Assert.Equal("PENDIENTE_REINTENTO", read[1].EstadoContrato);
        Assert.True(read[1].RateLimit);
        Assert.Equal("MarkdownPersistidoBD", read[1].OrigenMarkdown);
        Assert.Equal("corr-b", read[1].CorrelationId);
    }

    [Fact]
    public void Write_UsaBomUtf8YSeparadorPuntoYComa()
    {
        ResultsCsv.Write(_tempFile, new List<EvaluationResultRow>());

        var bytes = File.ReadAllBytes(_tempFile);

        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var text = File.ReadAllText(_tempFile);
        Assert.Contains("rel_path;file_name;expected_tdn1", text);
        Assert.Contains("estado_contrato;rate_limit;origen_markdown;correlation_id", text);
    }
}
