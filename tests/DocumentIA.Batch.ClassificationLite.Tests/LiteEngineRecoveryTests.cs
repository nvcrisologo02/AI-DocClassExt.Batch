using System.IO;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Tests.Fakes;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteEngineRecoveryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _docsDir;
    private readonly LiteRepository _repository;
    private readonly FakeIngestBackend _backend = new();

    public LiteEngineRecoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-recovery-tests-" + Guid.NewGuid().ToString("N"));
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

    private LiteDocument SeedDoc(string executionId, string name, string status, string? statusUri)
    {
        var path = Path.Combine(_docsDir, name);
        File.WriteAllText(path, "pdf");
        var info = new FileInfo(path);
        _repository.InsertDocuments(new[]
        {
            new LiteDocument
            {
                ExecutionId = executionId,
                FileName = name,
                FullPath = path,
                FileSize = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc.ToString("O"),
                Status = status,
                StatusQueryUri = statusUri,
                InstanceId = statusUri is null ? null : "inst",
                BatchNumber = 1
            }
        });
        return _repository.GetDocuments(executionId).Single(d => d.FileName == name);
    }

    [Fact]
    public async Task ReattachInFlight_RetomaPollingSinReenviar()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDoc(execution.ExecutionId, "envuelo.pdf", LiteDocumentStatus.InFlight, "https://backend/inst-1");

        await NewEngine().ReattachInFlightAsync(execution.ExecutionId, CancellationToken.None);

        Assert.Equal(0, _backend.IngestCalls);
        Assert.True(_backend.StatusCalls > 0);
        var doc = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.Succeeded, doc.Status);
        Assert.Equal("T01", doc.Tdn1);
    }

    [Fact]
    public async Task ReattachInFlight_SinStatusUri_VuelveAPending()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDoc(execution.ExecutionId, "huerfano.pdf", LiteDocumentStatus.InFlight, statusUri: null);

        await NewEngine().ReattachInFlightAsync(execution.ExecutionId, CancellationToken.None);

        Assert.Equal(0, _backend.StatusCalls);
        Assert.Equal(LiteDocumentStatus.Pending, _repository.GetDocuments(execution.ExecutionId).Single().Status);
    }

    [Fact]
    public async Task Errores401Consecutivos_AutoPausanElMotor()
    {
        _backend.OnIngest = _ => throw new InvalidOperationException(
            "Error 401: La Function Key no es valida o ha expirado.");

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var docs = new List<LiteDocument>();
        for (var i = 1; i <= 5; i++)
        {
            docs.Add(SeedDoc(execution.ExecutionId, $"k{i}.pdf", LiteDocumentStatus.Pending, null));
        }

        var engine = NewEngine(new LiteConfig { ParallelQueries = 1 });
        string? aviso = null;
        engine.AutoPaused += message => aviso = message;

        foreach (var doc in docs)
        {
            await engine.ProcessDocumentAsync(doc, CancellationToken.None);
        }

        Assert.True(engine.IsPaused);
        Assert.NotNull(aviso);
        Assert.Contains("401", aviso);
    }

    [Fact]
    public async Task UnExitoResetea_ElContadorDe401()
    {
        var calls = 0;
        _backend.OnIngest = request =>
        {
            calls++;
            // Falla con 401 en las llamadas 1-4, funciona en la 5, vuelve a fallar despues.
            if (calls == 5)
            {
                return new DocumentIA.Batch.Services.IngestResponse
                {
                    InstanceId = "i",
                    StatusQueryUri = "https://backend/ok"
                };
            }

            throw new InvalidOperationException("Error 401: La Function Key no es valida.");
        };

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var docs = new List<LiteDocument>();
        for (var i = 1; i <= 6; i++)
        {
            docs.Add(SeedDoc(execution.ExecutionId, $"m{i}.pdf", LiteDocumentStatus.Pending, null));
        }

        var engine = NewEngine(new LiteConfig { ParallelQueries = 1 });
        foreach (var doc in docs)
        {
            await engine.ProcessDocumentAsync(doc, CancellationToken.None);
        }

        Assert.False(engine.IsPaused);
    }
}
