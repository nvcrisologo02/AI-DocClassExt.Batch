using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Tests.Fakes;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteEngineCoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _docsDir;
    private readonly LiteRepository _repository;
    private readonly FakeIngestBackend _backend = new();

    public LiteEngineCoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-engine-tests-" + Guid.NewGuid().ToString("N"));
        _docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(_docsDir);
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private LiteEngine NewEngine(LiteConfig? config = null)
        => new(_repository, _backend, config ?? new LiteConfig(), delay: (_, _) => Task.CompletedTask);

    private LiteDocument SeedDoc(string executionId, string name)
    {
        var path = Path.Combine(_docsDir, name);
        File.WriteAllText(path, "pdf-" + name);
        var info = new FileInfo(path);
        var doc = new LiteDocument
        {
            ExecutionId = executionId,
            FileName = name,
            FullPath = path,
            FileSize = info.Length,
            LastModifiedUtc = info.LastWriteTimeUtc.ToString("O"),
            Status = LiteDocumentStatus.Pending,
            BatchNumber = 1
        };
        _repository.InsertDocuments(new[] { doc });
        return _repository.GetDocuments(executionId).Single(d => d.FileName == name);
    }

    [Fact]
    public async Task ProcessDocument_Completed_MarcaSucceededConResultado()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var doc = SeedDoc(execution.ExecutionId, "ok.pdf");

        await NewEngine().ProcessDocumentAsync(doc, CancellationToken.None);

        var stored = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.Succeeded, stored.Status);
        Assert.Equal("T01", stored.Tdn1);
        Assert.Equal("T01.02", stored.Tdn2);
        Assert.Equal(0.9, stored.Confidence);
        Assert.Equal(5, stored.Pages);
        Assert.Equal("1-5", stored.PagesIncluded);
        Assert.Equal(3000, stored.DurationMs);
        Assert.Equal("inst-ok.pdf", stored.InstanceId);
        Assert.NotNull(stored.StatusQueryUri);
        Assert.NotNull(stored.ResponseJson);
        Assert.Contains("base64 omitido", stored.RequestJson);
    }

    [Fact]
    public async Task ProcessDocument_Failed_MarcaError()
    {
        _backend.OnStatus = _ => FakeIngestBackend.FailedStatus();
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var doc = SeedDoc(execution.ExecutionId, "mal.pdf");

        await NewEngine().ProcessDocumentAsync(doc, CancellationToken.None);

        var stored = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.Error, stored.Status);
        Assert.Contains("Failed", stored.ErrorMessage);
    }

    [Fact]
    public async Task ProcessDocument_ExcepcionDeIngest_MarcaError()
    {
        _backend.OnIngest = _ => throw new InvalidOperationException("Error al invocar ingest: 500");
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var doc = SeedDoc(execution.ExecutionId, "boom.pdf");

        await NewEngine().ProcessDocumentAsync(doc, CancellationToken.None);

        var stored = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.Error, stored.Status);
        Assert.Contains("500", stored.ErrorMessage);
    }

    [Fact]
    public async Task ProcessDocument_FicheroInexistente_MarcaError()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var doc = SeedDoc(execution.ExecutionId, "borrado.pdf");
        File.Delete(doc.FullPath);

        await NewEngine().ProcessDocumentAsync(doc, CancellationToken.None);

        Assert.Equal(LiteDocumentStatus.Error, _repository.GetDocuments(execution.ExecutionId).Single().Status);
    }

    [Fact]
    public async Task ProcessDocuments_RespetaElParalelismo()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var docs = Enumerable.Range(1, 6).Select(i => SeedDoc(execution.ExecutionId, $"p{i}.pdf")).ToList();

        var concurrent = 0;
        var maxConcurrent = 0;
        var gate = new object();
        _backend.OnIngest = request =>
        {
            lock (gate)
            {
                concurrent++;
                maxConcurrent = Math.Max(maxConcurrent, concurrent);
            }
            Thread.Sleep(30);
            lock (gate) { concurrent--; }
            return new DocumentIA.Batch.Services.IngestResponse
            {
                InstanceId = "inst-" + request.Documento.Name,
                StatusQueryUri = "https://backend/" + request.Documento.Name
            };
        };

        var config = new LiteConfig { ParallelQueries = 2 };
        await NewEngine(config).ProcessDocumentsAsync(docs, CancellationToken.None);

        Assert.True(maxConcurrent <= 2, $"Concurrencia maxima {maxConcurrent}, esperada <= 2");
        Assert.All(_repository.GetDocuments(execution.ExecutionId), d => Assert.Equal(LiteDocumentStatus.Succeeded, d.Status));
    }

    [Fact]
    public async Task ProcessDocument_PollingReintentaHastaCompleted()
    {
        var calls = 0;
        _backend.OnStatus = _ => ++calls < 3
            ? FakeIngestBackend.RunningStatus()
            : FakeIngestBackend.CompletedStatus("T02", "T02.01", 0.8);
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var doc = SeedDoc(execution.ExecutionId, "lento.pdf");

        await NewEngine().ProcessDocumentAsync(doc, CancellationToken.None);

        Assert.Equal(3, calls);
        Assert.Equal(LiteDocumentStatus.Succeeded, _repository.GetDocuments(execution.ExecutionId).Single().Status);
    }
}
