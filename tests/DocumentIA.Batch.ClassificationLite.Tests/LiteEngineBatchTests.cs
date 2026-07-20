using System.IO;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Tests.Fakes;
using DocumentIA.Batch.Services;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteEngineBatchTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _docsDir;
    private readonly LiteRepository _repository;
    private readonly FakeIngestBackend _backend = new();

    public LiteEngineBatchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-batch-tests-" + Guid.NewGuid().ToString("N"));
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

    private void SeedDocs(string executionId, int count, int batchNumber)
    {
        var docs = new List<LiteDocument>();
        for (var i = 1; i <= count; i++)
        {
            var name = $"b{batchNumber}-d{i}.pdf";
            var path = Path.Combine(_docsDir, name);
            File.WriteAllText(path, "pdf");
            var info = new FileInfo(path);
            docs.Add(new LiteDocument
            {
                ExecutionId = executionId,
                FileName = name,
                FullPath = path,
                FileSize = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc.ToString("O"),
                Status = LiteDocumentStatus.Pending,
                BatchNumber = batchNumber
            });
        }

        _repository.InsertDocuments(docs);
    }

    [Fact]
    public async Task RunAsync_ProcesaTodosLosLotes_YMarcaEjecucionCompleted()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDocs(execution.ExecutionId, 3, batchNumber: 1);
        SeedDocs(execution.ExecutionId, 2, batchNumber: 2);

        await NewEngine().RunAsync(execution.ExecutionId, CancellationToken.None);

        var docs = _repository.GetDocuments(execution.ExecutionId);
        Assert.Equal(5, docs.Count);
        Assert.All(docs, d => Assert.Equal(LiteDocumentStatus.Succeeded, d.Status));
        Assert.Null(_repository.GetIncompleteExecution());

        var stored = _repository.GetExecution(execution.ExecutionId);
        Assert.NotNull(stored);
        Assert.Equal(LiteExecutionStatus.Completed, stored!.Status);
        Assert.NotNull(stored.CompletedAt);
    }

    [Fact]
    public async Task RunAsync_ReintentaErrores_HastaExito()
    {
        // "malo.pdf" falla en el ingest las 2 primeras veces y funciona a la tercera.
        var failures = 0;
        _backend.OnIngest = request =>
        {
            if (request.Documento.Name.StartsWith("b1-d2") && failures < 2)
            {
                failures++;
                throw new InvalidOperationException("fallo transitorio");
            }

            return new IngestResponse
            {
                InstanceId = "inst",
                StatusQueryUri = "https://backend/" + request.Documento.Name
            };
        };

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDocs(execution.ExecutionId, 3, batchNumber: 1);

        await NewEngine(new LiteConfig { MaxRetries = 3 }).RunAsync(execution.ExecutionId, CancellationToken.None);

        var docs = _repository.GetDocuments(execution.ExecutionId);
        Assert.All(docs, d => Assert.Equal(LiteDocumentStatus.Succeeded, d.Status));
        var retried = docs.Single(d => d.FileName.StartsWith("b1-d2"));
        Assert.Equal(2, retried.RetryCount);
    }

    [Fact]
    public async Task RunAsync_AgotaReintentos_MarcaDefinitiveError()
    {
        _backend.OnIngest = request => request.Documento.Name.StartsWith("b1-d1")
            ? throw new InvalidOperationException("fallo permanente")
            : new IngestResponse { InstanceId = "i", StatusQueryUri = "https://backend/" + request.Documento.Name };

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDocs(execution.ExecutionId, 2, batchNumber: 1);

        await NewEngine(new LiteConfig { MaxRetries = 3 }).RunAsync(execution.ExecutionId, CancellationToken.None);

        var docs = _repository.GetDocuments(execution.ExecutionId);
        var failed = docs.Single(d => d.FileName.StartsWith("b1-d1"));
        Assert.Equal(LiteDocumentStatus.DefinitiveError, failed.Status);
        Assert.Equal(3, failed.RetryCount);
        Assert.Contains("fallo permanente", failed.ErrorMessage);
        Assert.Equal(LiteDocumentStatus.Succeeded, docs.Single(d => d.FileName.StartsWith("b1-d2")).Status);
    }

    [Fact]
    public async Task RunAsync_MaxRetriesCero_NoReintenta()
    {
        _backend.OnIngest = _ => throw new InvalidOperationException("fallo");
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDocs(execution.ExecutionId, 1, batchNumber: 1);

        await NewEngine(new LiteConfig { MaxRetries = 0 }).RunAsync(execution.ExecutionId, CancellationToken.None);

        var doc = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.DefinitiveError, doc.Status);
        Assert.Equal(0, doc.RetryCount);
        Assert.Equal(1, _backend.IngestCalls);
    }

    [Fact]
    public async Task RunAsync_Cancelado_MarcaEjecucionCancelled()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        SeedDocs(execution.ExecutionId, 2, batchNumber: 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await NewEngine().RunAsync(execution.ExecutionId, cts.Token);

        Assert.Null(_repository.GetIncompleteExecution());

        var stored = _repository.GetExecution(execution.ExecutionId);
        Assert.NotNull(stored);
        Assert.Equal(LiteExecutionStatus.Cancelled, stored!.Status);
        Assert.NotNull(stored.CompletedAt);
    }

    [Fact]
    public async Task RunAsync_FalloNoRecuperable_MarcaEjecucionAborted()
    {
        // No hay una via limpia para forzar un fallo no relacionado con cancelacion en la
        // lectura/escritura de la BD sin corromper el fichero SQLite (fragil y no determinista).
        // En su lugar, se aprovecha que RunAsync invoca ProgressChanged al final de cada lote
        // (dentro del propio try) y se hace fallar esa suscripcion. Para que sea la UNICA
        // invocacion durante la ejecucion (y no una de las varias que dispara el procesado de
        // documentos por lote), el documento se siembra ya en estado Error con MaxRetries = 0:
        // asi no hay lote pendiente que procesar ni reintentos, solo el marcado a
        // DefinitiveError y el ProgressChanged de cierre de lote.
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var name = "b1-d1.pdf";
        var path = Path.Combine(_docsDir, name);
        File.WriteAllText(path, "pdf");
        var info = new FileInfo(path);
        _repository.InsertDocuments(new[]
        {
            new LiteDocument
            {
                ExecutionId = execution.ExecutionId,
                FileName = name,
                FullPath = path,
                FileSize = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc.ToString("O"),
                Status = LiteDocumentStatus.Error,
                BatchNumber = 1
            }
        });

        var engine = NewEngine(new LiteConfig { MaxRetries = 0 });
        var thrown = false;
        engine.ProgressChanged += () =>
        {
            if (thrown)
            {
                return;
            }

            thrown = true;
            throw new InvalidOperationException("fallo no recuperable en ProgressChanged");
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.RunAsync(execution.ExecutionId, CancellationToken.None));

        var stored = _repository.GetExecution(execution.ExecutionId);
        Assert.NotNull(stored);
        Assert.Equal(LiteExecutionStatus.Aborted, stored!.Status);
        Assert.NotNull(stored.CompletedAt);
    }
}
