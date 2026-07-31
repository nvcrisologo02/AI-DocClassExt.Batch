using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.Evaluation.Engine;
using DocumentIA.Batch.Evaluation.Models;
using DocumentIA.Batch.Evaluation.Tests.Fakes;
using Xunit;

namespace DocumentIA.Batch.Evaluation.Tests;

public class EvaluationClassifierTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "eval-classifier-tests-" + Guid.NewGuid().ToString("N"));

    public EvaluationClassifierTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private ManifestRow SeedDoc(string relPath, string expectedTdn1 = "ACTE", string expectedTdn2 = "ACTE-01")
    {
        var fullPath = Path.Combine(_tempDir, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "pdf-fake");
        return new ManifestRow
        {
            FileName = Path.GetFileName(relPath),
            RelPath = relPath,
            ExpectedTdn1 = expectedTdn1,
            ExpectedTdn2 = expectedTdn2
        };
    }

    private static EvaluationClassifier NewClassifier(FakeIngestBackend backend, LiteConfig? config = null, int maxPagesClassification = 10)
        => new(backend, config ?? new LiteConfig(), maxPagesClassification, delay: (_, _) => Task.CompletedTask);

    [Fact]
    public async Task ClassifyAsync_Completado_RellenaPrediccionYProveedor()
    {
        var backend = new FakeIngestBackend
        {
            OnStatus = _ => FakeIngestBackend.CompletedStatus("ACTE", "ACTE-01", 0.93, provider: "gpt-4.1", fallback: false)
        };
        var doc = SeedDoc("ACTE/a.pdf");

        var result = await NewClassifier(backend).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        Assert.Equal(EvaluationEstado.Ok, result.Estado);
        Assert.Equal("ACTE", result.PredictedTdn1);
        Assert.Equal("ACTE-01", result.PredictedTdn2);
        Assert.Equal(0.93, result.Confianza);
        Assert.Equal("gpt-4.1", result.Proveedor);
        Assert.False(result.Fallback);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ClassifyAsync_CapturaEstadoContratoRateLimitYOrigenMarkdown()
    {
        var backend = new FakeIngestBackend
        {
            OnStatus = _ => FakeIngestBackend.CompletedStatus(
                "ACTE", "ACTE-01", 0.4,
                estadoContrato: "PENDIENTE_REINTENTO",
                rateLimitExcedido: true,
                origenMarkdown: "MarkdownPersistidoBD")
        };
        var doc = SeedDoc("ACTE/a.pdf");

        var result = await NewClassifier(backend).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        Assert.Equal("PENDIENTE_REINTENTO", result.EstadoContrato);
        Assert.True(result.RateLimit);
        Assert.Equal("MarkdownPersistidoBD", result.OrigenMarkdown);
    }

    [Fact]
    public async Task ClassifyAsync_CapturaCamposDelContrato_ConVariantesCamelCase()
    {
        var json = """
            {
              "Identificacion": { "Tdn1": "ACTE", "Tdn2": "ACTE-01", "Paginas": 5, "FechaProceso": "2026-07-20T12:00:00Z" },
              "resultado": { "estado": "REVISION", "confianzaGlobal": 0.6 },
              "detalleEjecucion": {
                "origenMarkdown": "LayoutPreClasificacion",
                "clasificacion": { "clasificador": "gpt-4.1", "fallbackLLM": "false", "rateLimitExcedido": true }
              }
            }
            """;
        var backend = new FakeIngestBackend
        {
            OnStatus = _ => new DocumentIA.Batch.Services.DurableStatusResponse
            {
                RuntimeStatus = "Completed",
                Output = System.Text.Json.JsonDocument.Parse(json).RootElement.Clone()
            }
        };
        var doc = SeedDoc("ACTE/a.pdf");

        var result = await NewClassifier(backend).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        Assert.Equal("REVISION", result.EstadoContrato);
        Assert.True(result.RateLimit);
        Assert.Equal("LayoutPreClasificacion", result.OrigenMarkdown);
    }

    [Fact]
    public async Task ClassifyAsync_PropagaCorrelationIdGeneradoAlConstruirLaRequest()
    {
        var backend = new FakeIngestBackend
        {
            OnStatus = _ => FakeIngestBackend.CompletedStatus("ACTE", "ACTE-01", 0.9)
        };
        var doc = SeedDoc("ACTE/a.pdf");

        var result = await NewClassifier(backend).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(result.CorrelationId));
        Assert.Equal(backend.LastRequest!.Trazabilidad.CorrelationId, result.CorrelationId);
    }

    [Fact]
    public async Task ClassifyAsync_AgotaReintentos_ConservaCorrelationIdDelUltimoIntento()
    {
        var backend = new FakeIngestBackend { OnStatus = _ => FakeIngestBackend.FailedStatus() };
        var doc = SeedDoc("ACTE/a.pdf");
        var config = new LiteConfig { MaxRetries = 2 };

        var result = await NewClassifier(backend, config).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        Assert.Equal(EvaluationEstado.Error, result.Estado);
        Assert.False(string.IsNullOrEmpty(result.CorrelationId));
        Assert.Equal(backend.LastRequest!.Trazabilidad.CorrelationId, result.CorrelationId);
    }

    [Fact]
    public async Task ClassifyAsync_DetectaFallbackActivado()
    {
        var backend = new FakeIngestBackend
        {
            OnStatus = _ => FakeIngestBackend.CompletedStatus("ACTE", "ACTE-01", 0.5, provider: "azure-di", fallback: true)
        };
        var doc = SeedDoc("ACTE/a.pdf");

        var result = await NewClassifier(backend).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        Assert.True(result.Fallback);
        Assert.Equal("azure-di", result.Proveedor);
    }

    [Fact]
    public async Task ClassifyAsync_FalloTransitorio_ReintentaYAcabaOk()
    {
        var calls = 0;
        var backend = new FakeIngestBackend
        {
            OnStatus = _ => ++calls == 1
                ? FakeIngestBackend.FailedStatus()
                : FakeIngestBackend.CompletedStatus("ACTE", "ACTE-01", 0.8)
        };
        var doc = SeedDoc("ACTE/a.pdf");
        var config = new LiteConfig { MaxRetries = 2 };

        var result = await NewClassifier(backend, config).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        Assert.Equal(EvaluationEstado.Ok, result.Estado);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ClassifyAsync_AgotaReintentos_DevuelveError()
    {
        var backend = new FakeIngestBackend { OnStatus = _ => FakeIngestBackend.FailedStatus() };
        var doc = SeedDoc("ACTE/a.pdf");
        var config = new LiteConfig { MaxRetries = 2 };

        var result = await NewClassifier(backend, config).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        Assert.Equal(EvaluationEstado.Error, result.Estado);
        Assert.Contains("Failed", result.Error);
        Assert.Null(result.PredictedTdn1);
    }

    [Fact]
    public async Task ClassifyAsync_FicheroInexistente_DevuelveError()
    {
        var backend = new FakeIngestBackend();
        var doc = new ManifestRow { FileName = "no-existe.pdf", RelPath = "X/no-existe.pdf", ExpectedTdn1 = "X", ExpectedTdn2 = "X-01" };
        var config = new LiteConfig { MaxRetries = 0 };

        var result = await NewClassifier(backend, config).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        Assert.Equal(EvaluationEstado.Error, result.Estado);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ClassifyAsync_PorDefecto_EnviaRecorteDe10Paginas()
    {
        var backend = new FakeIngestBackend();
        var doc = SeedDoc("ACTE/a.pdf");

        await NewClassifier(backend).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        Assert.NotNull(backend.LastRequest);
        Assert.Equal(10, backend.LastRequest!.Instrucciones.MaxPagesForClassificationOnly);
    }

    [Fact]
    public async Task ClassifyAsync_ConRecorteExplicito_SobrescribeMaxPages()
    {
        var backend = new FakeIngestBackend();
        var doc = SeedDoc("ACTE/a.pdf");

        await NewClassifier(backend, maxPagesClassification: 3).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        Assert.Equal(3, backend.LastRequest!.Instrucciones.MaxPagesForClassificationOnly);
    }

    [Fact]
    public async Task ClassifyAsync_ConRecorteCero_RespetaValorDeLiteRequestFactory()
    {
        var backend = new FakeIngestBackend();
        var doc = SeedDoc("ACTE/a.pdf");

        await NewClassifier(backend, maxPagesClassification: 0).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        // LiteConfig por defecto tiene OnlyClassification = true, por lo que LiteRequestFactory
        // calcula 10 sin que el harness lo sobrescriba.
        Assert.Equal(10, backend.LastRequest!.Instrucciones.MaxPagesForClassificationOnly);
    }

    [Fact]
    public async Task ClassifyAsync_CompletadoSinOutput_DevuelveError()
    {
        var backend = new FakeIngestBackend
        {
            OnStatus = _ => new DocumentIA.Batch.Services.DurableStatusResponse { RuntimeStatus = "Completed", Output = null }
        };
        var doc = SeedDoc("ACTE/a.pdf");
        var config = new LiteConfig { MaxRetries = 0 };

        var result = await NewClassifier(backend, config).ClassifyAsync(_tempDir, doc, CancellationToken.None);

        Assert.Equal(EvaluationEstado.Error, result.Estado);
        Assert.Contains("output", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("INRG-08--OP-99-SCXX-00_800822_106820349.PDF", "OP-99-SCXX-00_800822_106820349.PDF")]
    [InlineData("COMU-07--E23006170.PDF", "E23006170.PDF")]
    [InlineData("1143_Nota_Simple.pdf", "1143_Nota_Simple.pdf")]
    public void StripEtiquetaPrefix_QuitaSoloElPrefijoDeEtiquetaGolden(string entrada, string esperado)
    {
        Assert.Equal(esperado, EvaluationClassifier.StripEtiquetaPrefix(entrada));
    }
}
