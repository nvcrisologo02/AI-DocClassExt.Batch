using System.Collections.Specialized;
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
            new LiteDocument { ExecutionId = _executionId, FileName = "alfa.pdf", FullPath = @"c:\docs\alfa.pdf", FileSize = 1, LastModifiedUtc = "x", Status = LiteDocumentStatus.Succeeded, Tdn1 = "T01", Tdn2 = "T01.02", Confidence = 0.9, Pages = 5, PagesIncluded = "1-5", ProcessDate = "2026-07-20", DurationMs = 1000, Estado = "OK" },
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
    public void ReloadRows_NoCargaLosJsonPesados()
    {
        _repository.InsertDocuments(new[]
        {
            new LiteDocument
            {
                ExecutionId = _executionId,
                FileName = "pesado.pdf",
                FullPath = @"c:\docs\pesado.pdf",
                FileSize = 1,
                LastModifiedUtc = "x",
                Status = LiteDocumentStatus.Succeeded,
                RequestJson = "{\"request\":\"" + new string('x', 500) + "\"}",
                ResponseJson = "{\"response\":\"" + new string('y', 500) + "\"}"
            }
        });

        _viewModel.ReloadRows();

        var row = _viewModel.Rows.Single(r => r.FileName == "pesado.pdf");
        Assert.True(string.IsNullOrEmpty(row.RequestJson));
        Assert.True(string.IsNullOrEmpty(row.ResponseJson));
    }

    [Fact]
    public void ShowOnlyReview_DejaVisibleSoloLosDocumentosDeBajaConfianza()
    {
        _repository.InsertDocuments(new[]
        {
            new LiteDocument { ExecutionId = _executionId, FileName = "epsilon.pdf", FullPath = @"c:\docs\epsilon.pdf", FileSize = 1, LastModifiedUtc = "x", Status = LiteDocumentStatus.Succeeded, Confidence = 0.45, Estado = "BAJA_CONFIANZA_CLASIFICACION" }
        });
        _viewModel.ReloadRows();

        _viewModel.ShowOnlyReview = true;

        var visible = _viewModel.RowsView.Cast<LiteDocumentRow>().ToList();
        Assert.Single(visible);
        Assert.Equal("epsilon.pdf", visible[0].FileName);
    }

    [Fact]
    public void ShowOnlyReview_LaExportacionSoloIncluyeLosDeBajaConfianza()
    {
        _repository.InsertDocuments(new[]
        {
            new LiteDocument { ExecutionId = _executionId, FileName = "epsilon.pdf", FullPath = @"c:\docs\epsilon.pdf", FileSize = 1, LastModifiedUtc = "x", Status = LiteDocumentStatus.Succeeded, Confidence = 0.45, Estado = "BAJA_CONFIANZA_CLASIFICACION" }
        });
        _viewModel.ReloadRows();
        _viewModel.ShowOnlyReview = true;
        var path = Path.Combine(_tempDir, "export-revision.csv");

        _viewModel.ExportCsv(path);

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("epsilon.pdf", lines[1]);
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

    /// <summary>
    /// Un Reset de la vista obliga al DataGrid a regenerar sus contenedores y a recalcular el
    /// desplazamiento. Emitirlo en cada refresco periodico, aunque no haya cambiado ninguna
    /// fila, es la parte mas cara del tick y ademas hace saltar la rejilla mientras se mira.
    /// </summary>
    [Fact]
    public void ReloadRows_SinCambios_NoReconstruyeLaVista()
    {
        _viewModel.ReloadRows();
        var eventos = ObservarLaVista();

        _viewModel.ReloadRows();

        Assert.Empty(eventos);
    }

    [Fact]
    public void ReloadRows_SoloCambiaElEstadoDeUnaFila_NoReconstruyeLaVistaSinFiltro()
    {
        _viewModel.ReloadRows();
        var eventos = ObservarLaVista();

        var beta = _repository.GetDocuments(_executionId).Single(d => d.FileName == "beta.pdf");
        beta.Status = LiteDocumentStatus.Succeeded;
        _repository.UpdateDocument(beta);
        _viewModel.ReloadRows();

        Assert.Empty(eventos);
        Assert.Equal("Succeeded", _viewModel.Rows.Single(r => r.FileName == "beta.pdf").Status);
    }

    [Fact]
    public void ReloadRows_ConFiltroDeEstadoActivo_ReconstruyeLaVistaCuandoUnaFilaCambiaDeEstado()
    {
        _viewModel.ReloadRows();
        _viewModel.StatusFilter = LiteDocumentStatus.Pending;
        Assert.Single(_viewModel.RowsView.Cast<LiteDocumentRow>());
        var eventos = ObservarLaVista();

        var beta = _repository.GetDocuments(_executionId).Single(d => d.FileName == "beta.pdf");
        beta.Status = LiteDocumentStatus.Succeeded;
        _repository.UpdateDocument(beta);
        _viewModel.ReloadRows();

        Assert.Contains(NotifyCollectionChangedAction.Reset, eventos);
        Assert.Empty(_viewModel.RowsView.Cast<LiteDocumentRow>());
    }

    /// <summary>
    /// Anadir las filas de una en una a la coleccion observable ligada al DataGrid cuesta un
    /// evento por fila; con decenas de miles de documentos el primer refresco tras el escaneo
    /// se lleva el hilo de interfaz durante minutos. Las altas deben viajar en un unico Reset.
    /// </summary>
    [Fact]
    public void ReloadRows_ConMuchasFilasNuevas_LasAnadeEnUnUnicoReset()
    {
        _viewModel.ReloadRows();
        _repository.InsertDocuments(Enumerable.Range(0, 50).Select(i => new LiteDocument
        {
            ExecutionId = _executionId,
            FileName = $"nuevo-{i:D3}.pdf",
            FullPath = $@"c:\docs\nuevo-{i:D3}.pdf",
            FileSize = 1,
            LastModifiedUtc = "x",
            Status = LiteDocumentStatus.Pending
        }).ToList());
        var eventos = ObservarLaVista();

        _viewModel.ReloadRows();

        Assert.Equal(54, _viewModel.Rows.Count);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, eventos);
    }

    /// <summary>
    /// El refresco periodico corre en un hilo del ThreadPool y marshaliza a la interfaz. Si cada
    /// refresco tarda mas que el periodo del temporizador y los ticks se encolan sin control, la
    /// cola del despachador crece sin limite y la ventana no vuelve a pintarse ni a atender el
    /// raton. Solo debe haber un refresco pendiente en cada momento.
    /// </summary>
    [Fact]
    public void SolicitarRefresco_DesdeOtroHilo_NoAcumulaMasDeUnoPendiente()
    {
        _viewModel.ReloadRows();

        // Este hilo de test es el que construyo el ViewModel, asi que es el "hilo de interfaz":
        // desde otro hilo el refresco pasa por el despachador, que aqui no tiene bucle de
        // mensajes y por tanto no ejecuta la operacion encolada. Ese es exactamente el escenario
        // del bloqueo, y lo que se comprueba es que los ticks siguientes se descartan.
        var hilo = new Thread(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                _viewModel.RequestRefresh();
            }
        });
        hilo.Start();
        hilo.Join();

        Assert.Equal(1, _viewModel.QueuedRefreshCount);
    }

    /// <summary>
    /// La lectura de SQLite del refresco (todas las filas de la ejecucion mas el recuento
    /// agregado) no puede ejecutarse en el hilo de interfaz: con muchos documentos son cientos
    /// de milisegundos de I/O y de materializacion de objetos por cada tick.
    /// </summary>
    [Fact]
    public void RequestRefresh_DesdeOtroHilo_LeeLaBaseDeDatosAntesDeMarshalizar()
    {
        var hilo = new Thread(() => _viewModel.RequestRefresh());
        hilo.Start();
        hilo.Join();

        // El despachador de este test no tiene bucle de mensajes, asi que la operacion encolada
        // no ha corrido: si el snapshot ya esta leido, la consulta se hizo en el hilo llamante y
        // no en el de interfaz. Las filas, en cambio, siguen sin aplicarse.
        Assert.NotNull(_viewModel.PendingSnapshot);
        Assert.Equal(4, _viewModel.PendingSnapshot!.Counters.Total);
        Assert.Equal(4, _viewModel.PendingSnapshot.Documents.Count);
        Assert.Empty(_viewModel.Rows);
    }

    /// <summary>Acciones que la vista emite; vacio significa que la rejilla no se ha tocado.</summary>
    private List<NotifyCollectionChangedAction> ObservarLaVista()
    {
        var eventos = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)_viewModel.RowsView).CollectionChanged += (_, e) => eventos.Add(e.Action);
        return eventos;
    }
}
