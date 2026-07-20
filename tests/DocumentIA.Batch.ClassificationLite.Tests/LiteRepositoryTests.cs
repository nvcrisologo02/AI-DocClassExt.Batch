using System.IO;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LiteRepository _repository;

    public LiteRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-repo-tests-" + Guid.NewGuid().ToString("N"));
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static LiteDocument NewDoc(string executionId, string name, string status = LiteDocumentStatus.Pending, int batch = 1)
        => new()
        {
            ExecutionId = executionId,
            FileName = name,
            FullPath = @"c:\docs\" + name,
            FileSize = 100,
            LastModifiedUtc = "2026-07-20T10:00:00.0000000Z",
            Status = status,
            BatchNumber = batch
        };

    [Fact]
    public void CreateExecution_YGetIncomplete_LaDevuelve()
    {
        var execution = _repository.CreateExecution(@"\\server\share\docs", includeSubfolders: true, configSnapshotJson: "{}");

        var incomplete = _repository.GetIncompleteExecution();

        Assert.NotNull(incomplete);
        Assert.Equal(execution.ExecutionId, incomplete!.ExecutionId);
        Assert.Equal(LiteExecutionStatus.Running, incomplete.Status);
        Assert.Equal(@"\\server\share\docs", incomplete.RootPath);
        Assert.True(incomplete.IncludeSubfolders);
    }

    [Fact]
    public void UpdateExecutionStatus_Completed_YaNoEsIncompleta()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");

        _repository.UpdateExecutionStatus(execution.ExecutionId, LiteExecutionStatus.Completed, setCompletedAt: true);

        Assert.Null(_repository.GetIncompleteExecution());
    }

    [Fact]
    public void InsertDocuments_YGetDocuments_HacenRoundTrip()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _repository.InsertDocuments(new[] { NewDoc(execution.ExecutionId, "a.pdf"), NewDoc(execution.ExecutionId, "b.pdf") });

        var docs = _repository.GetDocuments(execution.ExecutionId);

        Assert.Equal(2, docs.Count);
        Assert.All(docs, d => Assert.True(d.Id > 0));
        Assert.Equal(new[] { "a.pdf", "b.pdf" }, docs.Select(d => d.FileName).ToArray());
    }

    [Fact]
    public void UpdateDocument_PersisteResultado()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _repository.InsertDocuments(new[] { NewDoc(execution.ExecutionId, "a.pdf") });
        var doc = _repository.GetDocuments(execution.ExecutionId).Single();

        doc.Status = LiteDocumentStatus.Succeeded;
        doc.Tdn1 = "T01";
        doc.Tdn2 = "T01.03";
        doc.Confidence = 0.93;
        doc.Pages = 12;
        doc.PagesIncluded = "1-10";
        doc.DurationMs = 4200;
        doc.InstanceId = "abc";
        doc.StatusQueryUri = "https://x/runtime/webhooks/durabletask/instances/abc";
        doc.RequestJson = "{\"a\":1}";
        doc.ResponseJson = "{\"b\":2}";
        _repository.UpdateDocument(doc);

        var reloaded = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.Succeeded, reloaded.Status);
        Assert.Equal("T01", reloaded.Tdn1);
        Assert.Equal("T01.03", reloaded.Tdn2);
        Assert.Equal(0.93, reloaded.Confidence);
        Assert.Equal(12, reloaded.Pages);
        Assert.Equal("1-10", reloaded.PagesIncluded);
        Assert.Equal(4200, reloaded.DurationMs);
        Assert.Equal("{\"b\":2}", reloaded.ResponseJson);
    }

    [Fact]
    public void GetDocument_DevuelveElDocumentoCompletoConAmbosJson()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        var seed = NewDoc(execution.ExecutionId, "a.pdf");
        seed.RequestJson = "{\"request\":true}";
        seed.ResponseJson = "{\"response\":true}";
        _repository.InsertDocuments(new[] { seed });
        var id = _repository.GetDocuments(execution.ExecutionId).Single().Id;

        var full = _repository.GetDocument(id);

        Assert.NotNull(full);
        Assert.Equal("a.pdf", full!.FileName);
        Assert.Equal("{\"request\":true}", full.RequestJson);
        Assert.Equal("{\"response\":true}", full.ResponseJson);
    }
}
