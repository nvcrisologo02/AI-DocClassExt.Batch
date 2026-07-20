using System.IO;
using System.Windows.Data;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using DocumentIA.Batch.ClassificationLite.ViewModels;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteMainViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LiteRepository _repository;
    private readonly LiteMainViewModel _viewModel;
    private readonly string _executionId;

    public LiteMainViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-vm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
        _viewModel = new LiteMainViewModel(_repository, new LiteConfigService(Path.Combine(_tempDir, "config.json")));

        var execution = _repository.CreateExecution(@"c:\docs", false, "{}");
        _executionId = execution.ExecutionId;
        _repository.InsertDocuments(new[]
        {
            new LiteDocument { ExecutionId = _executionId, FileName = "alfa.pdf", FullPath = @"c:\docs\alfa.pdf", FileSize = 1, LastModifiedUtc = "x", Status = LiteDocumentStatus.Succeeded, Tdn1 = "T01", Tdn2 = "T01.02", Confidence = 0.9, Pages = 5, PagesIncluded = "1-5", ProcessDate = "2026-07-20", DurationMs = 1000 },
            new LiteDocument { ExecutionId = _executionId, FileName = "beta.pdf", FullPath = @"c:\docs\beta.pdf", FileSize = 1, LastModifiedUtc = "x", Status = LiteDocumentStatus.Pending },
            new LiteDocument { ExecutionId = _executionId, FileName = "gamma.pdf", FullPath = @"c:\docs\gamma.pdf", FileSize = 1, LastModifiedUtc = "x", Status = LiteDocumentStatus.DefinitiveError, ErrorMessage = "kaput" },
            new LiteDocument { ExecutionId = _executionId, FileName = "delta.pdf", FullPath = @"c:\docs\delta.pdf", FileSize = 1, LastModifiedUtc = "x", Status = LiteDocumentStatus.SkippedHistory, Tdn1 = "T09" }
        });
        _viewModel.CurrentExecutionId = _executionId;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void ReloadRows_ProyectaLosDocumentos()
    {
        _viewModel.ReloadRows();

        Assert.Equal(4, _viewModel.Rows.Count);
        var alfa = _viewModel.Rows.Single(r => r.FileName == "alfa.pdf");
        Assert.Equal("Succeeded", alfa.Status);
        Assert.Equal("T01", alfa.Tdn1);
        Assert.Equal("1-5", alfa.PagesIncluded);
        Assert.Equal("5", alfa.Pages);
        Assert.Equal("1000", alfa.TotalDurationMs);
    }

    [Fact]
    public void RefreshCounters_CalculaLosSeisIndicadores()
    {
        _viewModel.RefreshCounters();

        Assert.Equal(4, _viewModel.TotalFound);
        Assert.Equal(1, _viewModel.PendingCount);
        Assert.Equal(0, _viewModel.InFlightCount);
        Assert.Equal(1, _viewModel.SucceededCount);
        Assert.Equal(1, _viewModel.DefinitiveErrorCount);
        Assert.Equal(1, _viewModel.SkippedCount);
    }

    [Fact]
    public void FilterText_FiltraPorNombre()
    {
        _viewModel.ReloadRows();
        _viewModel.FilterText = "AMM";

        var visible = _viewModel.RowsView.Cast<LiteDocumentRow>().ToList();

        Assert.Single(visible);
        Assert.Equal("gamma.pdf", visible[0].FileName);
    }

    [Fact]
    public void StatusFilter_FiltraPorEstado_YTodosLoQuita()
    {
        _viewModel.ReloadRows();
        _viewModel.StatusFilter = LiteDocumentStatus.Pending;
        Assert.Single(_viewModel.RowsView.Cast<LiteDocumentRow>());

        _viewModel.StatusFilter = "Todos";
        Assert.Equal(4, _viewModel.RowsView.Cast<LiteDocumentRow>().Count());
    }

    [Fact]
    public void ExportCsv_ConFiltroActivo_ExportaSoloLoVisible()
    {
        _viewModel.ReloadRows();
        _viewModel.StatusFilter = LiteDocumentStatus.Succeeded;
        var path = Path.Combine(_tempDir, "export.csv");

        _viewModel.ExportCsv(path);

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("alfa.pdf", lines[1]);
    }

    [Fact]
    public void ReloadRows_DosVeces_ReutilizaLasInstanciasDeFila()
    {
        _viewModel.ReloadRows();
        var betaRow = _viewModel.Rows.Single(r => r.FileName == "beta.pdf");

        var beta = _repository.GetDocuments(_executionId).Single(d => d.FileName == "beta.pdf");
        beta.Status = LiteDocumentStatus.Succeeded;
        beta.Tdn1 = "T05";
        _repository.UpdateDocument(beta);

        _viewModel.ReloadRows();
        var betaRowAfter = _viewModel.Rows.Single(r => r.FileName == "beta.pdf");

        Assert.Same(betaRow, betaRowAfter);
        Assert.Equal("Succeeded", betaRowAfter.Status);
        Assert.Equal("T05", betaRowAfter.Tdn1);
    }

    [Fact]
    public void ReloadRows_DocumentoNuevo_SeAnadeALaColeccion()
    {
        _viewModel.ReloadRows();
        var originalRows = _viewModel.Rows.ToDictionary(r => r.Id, r => r);
        Assert.Equal(4, _viewModel.Rows.Count);

        _repository.InsertDocuments(new[]
        {
            new LiteDocument { ExecutionId = _executionId, FileName = "epsilon.pdf", FullPath = @"c:\docs\epsilon.pdf", FileSize = 1, LastModifiedUtc = "x", Status = LiteDocumentStatus.Pending }
        });

        _viewModel.ReloadRows();

        Assert.Equal(5, _viewModel.Rows.Count);
        Assert.Contains(_viewModel.Rows, r => r.FileName == "epsilon.pdf");
        foreach (var (id, row) in originalRows)
        {
            Assert.Same(row, _viewModel.Rows.Single(r => r.Id == id));
        }
    }

    [Fact]
    public void ExportCsv_ConFiltroDeEstado_UsaElEstadoActualDeLaBaseDeDatos()
    {
        _viewModel.ReloadRows();
        _viewModel.StatusFilter = LiteDocumentStatus.Pending;
        Assert.Single(_viewModel.RowsView.Cast<LiteDocumentRow>());

        var beta = _repository.GetDocuments(_executionId).Single(d => d.FileName == "beta.pdf");
        beta.Status = LiteDocumentStatus.Succeeded;
        _repository.UpdateDocument(beta);

        var path = Path.Combine(_tempDir, "export-filtro.csv");
        _viewModel.ExportCsv(path);

        var lines = File.ReadAllLines(path);
        Assert.DoesNotContain(lines, l => l.Contains("beta.pdf"));
    }
}
