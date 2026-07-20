using System.IO;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using DocumentIA.Batch.ClassificationLite.Tests.Fakes;
using DocumentIA.Batch.ClassificationLite.ViewModels;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteMainViewModelOrchestrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _docsDir;
    private readonly LiteRepository _repository;
    private readonly FakeIngestBackend _backend = new();
    private readonly LiteMainViewModel _viewModel;

    public LiteMainViewModelOrchestrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-orch-tests-" + Guid.NewGuid().ToString("N"));
        _docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(_docsDir);
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
        _viewModel = new LiteMainViewModel(_repository, new LiteConfigService(Path.Combine(_tempDir, "config.json")))
        {
            BackendFactory = () => _backend
        };
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private void CreatePdfs(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            File.WriteAllText(Path.Combine(_docsDir, $"doc{i}.pdf"), "pdf");
        }
    }

    [Fact]
    public async Task StartAsync_EscaneaYProcesaTodo()
    {
        CreatePdfs(3);
        _viewModel.SelectedPaths = new[] { _docsDir };

        await _viewModel.StartAsync();

        Assert.False(_viewModel.IsRunning);
        Assert.Equal(3, _viewModel.TotalFound);
        Assert.Equal(3, _viewModel.SucceededCount);
        Assert.Equal(3, _backend.IngestCalls);
        Assert.Null(_repository.GetIncompleteExecution());
    }

    [Fact]
    public async Task StartAsync_SegundaVez_OmitePorHistorico()
    {
        CreatePdfs(2);
        _viewModel.SelectedPaths = new[] { _docsDir };
        await _viewModel.StartAsync();
        var callsAfterFirst = _backend.IngestCalls;

        await _viewModel.StartAsync();

        Assert.Equal(callsAfterFirst, _backend.IngestCalls);
        Assert.Equal(2, _viewModel.SkippedCount);
        Assert.Equal(0, _viewModel.PendingCount);
    }

    [Fact]
    public void GetPendingRecovery_DevuelveEjecucionIncompleta_YDiscardLaAborta()
    {
        var execution = _repository.CreateExecution(_docsDir, false, "{}");

        var pending = _viewModel.GetPendingRecovery();
        Assert.NotNull(pending);
        Assert.Equal(execution.ExecutionId, pending!.ExecutionId);

        _viewModel.DiscardExecution(pending);

        Assert.Null(_repository.GetIncompleteExecution());
    }

    [Fact]
    public async Task ResumeExecutionAsync_ReenganchaEnVueloYTerminaPendientes()
    {
        CreatePdfs(2);
        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var enVuelo = new LiteDocument
        {
            ExecutionId = execution.ExecutionId,
            FileName = "doc1.pdf",
            FullPath = Path.Combine(_docsDir, "doc1.pdf"),
            FileSize = 3,
            LastModifiedUtc = "x",
            Status = LiteDocumentStatus.InFlight,
            InstanceId = "inst-1",
            StatusQueryUri = "https://backend/inst-1",
            BatchNumber = 1
        };
        var pendiente = new LiteDocument
        {
            ExecutionId = execution.ExecutionId,
            FileName = "doc2.pdf",
            FullPath = Path.Combine(_docsDir, "doc2.pdf"),
            FileSize = 3,
            LastModifiedUtc = "x",
            Status = LiteDocumentStatus.Pending,
            BatchNumber = 1
        };
        _repository.InsertDocuments(new[] { enVuelo, pendiente });

        await _viewModel.ResumeExecutionAsync(execution);

        var docs = _repository.GetDocuments(execution.ExecutionId);
        Assert.All(docs, d => Assert.Equal(LiteDocumentStatus.Succeeded, d.Status));
        Assert.Equal(1, _backend.IngestCalls); // solo el pendiente se reenvia
    }

    [Fact]
    public async Task PauseExecution_SinEjecucionEnCurso_NoAlteraLaUltimaEjecucion()
    {
        CreatePdfs(2);
        _viewModel.SelectedPaths = new[] { _docsDir };

        await _viewModel.StartAsync();

        var executionId = _viewModel.CurrentExecutionId!;
        Assert.Equal(LiteExecutionStatus.Completed, _repository.GetExecution(executionId)!.Status);

        _viewModel.PauseExecution();
        _viewModel.ResumeExecution();

        Assert.Equal(LiteExecutionStatus.Completed, _repository.GetExecution(executionId)!.Status);
        Assert.Null(_repository.GetIncompleteExecution());
    }

    [Fact]
    public async Task EjecucionExitosaTrasAutoPausa_TerminaConMensajeDeFinalizacion()
    {
        CreatePdfs(2);
        _viewModel.SelectedPaths = new[] { _docsDir };

        await _viewModel.StartAsync();

        Assert.Equal("Ejecucion finalizada.", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task CancelExecution_DetieneLaEjecucion()
    {
        CreatePdfs(20);
        _viewModel.SelectedPaths = new[] { _docsDir };
        _backend.OnIngest = request =>
        {
            Thread.Sleep(20);
            return new DocumentIA.Batch.Services.IngestResponse
            {
                InstanceId = "i",
                StatusQueryUri = "https://backend/" + request.Documento.Name
            };
        };

        var run = _viewModel.StartAsync();
        await Task.Delay(80);
        _viewModel.CancelExecution();
        await run;

        Assert.False(_viewModel.IsRunning);
        Assert.Null(_repository.GetIncompleteExecution());
    }
}
