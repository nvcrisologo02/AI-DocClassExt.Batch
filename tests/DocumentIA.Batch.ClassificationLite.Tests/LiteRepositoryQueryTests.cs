using System.IO;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteRepositoryQueryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LiteRepository _repository;

    public LiteRepositoryQueryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-repoq-tests-" + Guid.NewGuid().ToString("N"));
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static LiteDocument NewDoc(string executionId, string name, string status, int batch = 1,
        long size = 100, string modified = "2026-07-20T10:00:00.0000000Z")
        => new()
        {
            ExecutionId = executionId,
            FileName = name,
            FullPath = @"c:\docs\" + name,
            FileSize = size,
            LastModifiedUtc = modified,
            Status = status,
            BatchNumber = batch
        };

    [Fact]
    public void FindLastSucceeded_EncuentraEnOtraEjecucion_IgnorandoRuta()
    {
        var old = _repository.CreateExecution(@"c:\vieja", false, "{}");
        var succeeded = NewDoc(old.ExecutionId, "a.pdf", LiteDocumentStatus.Succeeded);
        succeeded.Tdn1 = "T01";
        succeeded.Tdn2 = "T01.02";
        succeeded.Confidence = 0.9;
        _repository.InsertDocuments(new[] { succeeded });

        var found = _repository.FindLastSucceeded("a.pdf", 100, "2026-07-20T10:00:00.0000000Z");

        Assert.NotNull(found);
        Assert.Equal("T01", found!.Tdn1);
        Assert.Equal("T01.02", found.Tdn2);
    }

    [Fact]
    public void FindLastSucceeded_NoEncuentraSiCambiaClave_OSiNoEsSucceeded()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _repository.InsertDocuments(new[]
        {
            NewDoc(execution.ExecutionId, "a.pdf", LiteDocumentStatus.Error),
            NewDoc(execution.ExecutionId, "b.pdf", LiteDocumentStatus.Succeeded, size: 999)
        });

        Assert.Null(_repository.FindLastSucceeded("a.pdf", 100, "2026-07-20T10:00:00.0000000Z"));
        Assert.Null(_repository.FindLastSucceeded("b.pdf", 100, "2026-07-20T10:00:00.0000000Z"));
    }

    [Fact]
    public void GetBatchNumbers_SoloLotesConTrabajoPendiente()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _repository.InsertDocuments(new[]
        {
            NewDoc(execution.ExecutionId, "a.pdf", LiteDocumentStatus.Succeeded, batch: 1),
            NewDoc(execution.ExecutionId, "b.pdf", LiteDocumentStatus.Pending, batch: 2),
            NewDoc(execution.ExecutionId, "c.pdf", LiteDocumentStatus.Error, batch: 3),
            NewDoc(execution.ExecutionId, "d.pdf", LiteDocumentStatus.SkippedHistory, batch: 4)
        });

        Assert.Equal(new[] { 2, 3 }, _repository.GetBatchNumbers(execution.ExecutionId).ToArray());
    }

    [Fact]
    public void GetPendingBatch_GetErrorsInBatch_GetInFlight_FiltranPorEstadoYLote()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _repository.InsertDocuments(new[]
        {
            NewDoc(execution.ExecutionId, "a.pdf", LiteDocumentStatus.Pending, batch: 1),
            NewDoc(execution.ExecutionId, "b.pdf", LiteDocumentStatus.Error, batch: 1),
            NewDoc(execution.ExecutionId, "c.pdf", LiteDocumentStatus.Pending, batch: 2),
            NewDoc(execution.ExecutionId, "d.pdf", LiteDocumentStatus.InFlight, batch: 1)
        });

        Assert.Equal(new[] { "a.pdf" }, _repository.GetPendingBatch(execution.ExecutionId, 1).Select(d => d.FileName).ToArray());
        Assert.Equal(new[] { "b.pdf" }, _repository.GetErrorsInBatch(execution.ExecutionId, 1).Select(d => d.FileName).ToArray());
        Assert.Equal(new[] { "d.pdf" }, _repository.GetInFlight(execution.ExecutionId).Select(d => d.FileName).ToArray());
    }

    [Fact]
    public void GetCounters_AgregaPorEstado()
    {
        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _repository.InsertDocuments(new[]
        {
            NewDoc(execution.ExecutionId, "a.pdf", LiteDocumentStatus.Pending),
            NewDoc(execution.ExecutionId, "b.pdf", LiteDocumentStatus.Pending),
            NewDoc(execution.ExecutionId, "c.pdf", LiteDocumentStatus.InFlight),
            NewDoc(execution.ExecutionId, "d.pdf", LiteDocumentStatus.Succeeded),
            NewDoc(execution.ExecutionId, "e.pdf", LiteDocumentStatus.DefinitiveError),
            NewDoc(execution.ExecutionId, "f.pdf", LiteDocumentStatus.SkippedHistory)
        });

        var counters = _repository.GetCounters(execution.ExecutionId);

        Assert.Equal(6, counters.Total);
        Assert.Equal(2, counters.Pending);
        Assert.Equal(1, counters.InFlight);
        Assert.Equal(1, counters.Succeeded);
        Assert.Equal(1, counters.DefinitiveError);
        Assert.Equal(1, counters.SkippedHistory);
        Assert.Equal(0, counters.Error);
        Assert.Equal(0, counters.Cancelled);
    }
}
