using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.ClassificationLite.ViewModels;

public partial class LiteMainViewModel : INotifyPropertyChanged
{
    private readonly LiteRepository _repository;
    private readonly LiteConfigService _configService;
    private readonly Dictionary<long, LiteDocumentRow> _rowsById = new();
    private string? _loadedExecutionId;

    private string _filterText = string.Empty;
    private string _statusFilter = "Todos";
    private bool _showOnlyReview;
    private int _totalFound;
    private int _pendingCount;
    private int _inFlightCount;
    private int _succeededCount;
    private int _definitiveErrorCount;
    private int _skippedCount;

    public LiteMainViewModel(LiteRepository repository, LiteConfigService configService)
    {
        _repository = repository;
        _configService = configService;
        Config = _configService.Load();
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = FilterRow;
        _ = InicializarSolicitanteAsync();
    }

    /// <summary>
    /// Resuelve el solicitante fuera del hilo de UI: la consulta del UPN puede necesitar el
    /// controlador de dominio y no debe bloquear el arranque.
    /// </summary>
    private async Task InicializarSolicitanteAsync()
    {
        Config.Solicitante = await Task.Run(
            () => SolicitanteProvider.ObtenerSolicitante(LiteConfig.ProgramaSolicitante));
    }

    public LiteConfig Config { get; private set; }

    public LiteRowCollection Rows { get; } = new();

    public ICollectionView RowsView { get; }

    public IReadOnlyList<string> StatusFilterOptions { get; } = new[]
    {
        "Todos",
        LiteDocumentStatus.Pending,
        LiteDocumentStatus.InFlight,
        LiteDocumentStatus.Succeeded,
        LiteDocumentStatus.Error,
        LiteDocumentStatus.DefinitiveError,
        LiteDocumentStatus.SkippedHistory,
        LiteDocumentStatus.Cancelled
    };

    public string? CurrentExecutionId { get; set; }

    public string FilterText
    {
        get => _filterText;
        set { _filterText = value ?? string.Empty; OnPropertyChanged(); RowsView.Refresh(); }
    }

    public string StatusFilter
    {
        get => _statusFilter;
        set { _statusFilter = value ?? "Todos"; OnPropertyChanged(); RowsView.Refresh(); }
    }

    public bool ShowOnlyReview
    {
        get => _showOnlyReview;
        set { _showOnlyReview = value; OnPropertyChanged(); RowsView.Refresh(); }
    }

    public int TotalFound { get => _totalFound; private set { _totalFound = value; OnPropertyChanged(); } }
    public int PendingCount { get => _pendingCount; private set { _pendingCount = value; OnPropertyChanged(); } }
    public int InFlightCount { get => _inFlightCount; private set { _inFlightCount = value; OnPropertyChanged(); } }
    public int SucceededCount { get => _succeededCount; private set { _succeededCount = value; OnPropertyChanged(); } }
    public int DefinitiveErrorCount { get => _definitiveErrorCount; private set { _definitiveErrorCount = value; OnPropertyChanged(); } }
    public int SkippedCount { get => _skippedCount; private set { _skippedCount = value; OnPropertyChanged(); } }

    public void ReloadRows()
    {
        var snapshot = ReadSnapshot();
        if (snapshot is not null)
        {
            ApplyRows(snapshot);
        }
    }

    public void RefreshCounters()
    {
        var snapshot = ReadSnapshot();
        if (snapshot is not null)
        {
            ApplyCounters(snapshot);
        }
    }

    /// <summary>
    /// Lee de SQLite todo lo que necesita el refresco. Puede (y debe) llamarse desde un hilo
    /// distinto al de interfaz: con muchos documentos son cientos de milisegundos entre la
    /// consulta y la materializacion de las filas.
    /// </summary>
    internal LiteGridSnapshot? ReadSnapshot()
    {
        var executionId = CurrentExecutionId;
        if (string.IsNullOrWhiteSpace(executionId))
        {
            return null;
        }

        return new LiteGridSnapshot(
            executionId,
            _repository.GetDocumentsForGrid(executionId),
            _repository.GetCounters(executionId));
    }

    /// <summary>Proyecta el snapshot sobre la rejilla y los indicadores. Solo en el hilo de interfaz.</summary>
    internal void ApplySnapshot(LiteGridSnapshot snapshot)
    {
        // Entre la lectura y esta proyeccion el usuario pudo arrancar otra ejecucion: aplicar un
        // snapshot de la anterior haria parpadear la rejilla con filas que ya no corresponden.
        if (!string.Equals(snapshot.ExecutionId, CurrentExecutionId, StringComparison.Ordinal))
        {
            return;
        }

        ApplyRows(snapshot);
        ApplyCounters(snapshot);
    }

    private void ApplyRows(LiteGridSnapshot snapshot)
    {
        if (!string.Equals(_loadedExecutionId, snapshot.ExecutionId, StringComparison.Ordinal))
        {
            Rows.Clear();
            _rowsById.Clear();
            _loadedExecutionId = snapshot.ExecutionId;
        }

        var seenIds = new HashSet<long>(snapshot.Documents.Count);
        var added = new List<LiteDocumentRow>();
        var filterRelevantChange = false;

        foreach (var document in snapshot.Documents)
        {
            seenIds.Add(document.Id);

            if (_rowsById.TryGetValue(document.Id, out var existingRow))
            {
                var statusBefore = existingRow.Status;
                var reviewableBefore = existingRow.IsLowConfidence;
                existingRow.UpdateFrom(document);
                filterRelevantChange = filterRelevantChange
                    || !string.Equals(statusBefore, existingRow.Status, StringComparison.Ordinal)
                    || reviewableBefore != existingRow.IsLowConfidence;
            }
            else
            {
                var row = LiteDocumentRow.From(document);
                _rowsById[document.Id] = row;
                added.Add(row);
            }
        }

        var removedIndexes = new List<int>();
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!seenIds.Contains(Rows[i].Id))
            {
                removedIndexes.Add(i);
            }
        }

        if (added.Count > 0 || removedIndexes.Count > 0)
        {
            foreach (var index in removedIndexes)
            {
                _rowsById.Remove(Rows[index].Id);
            }

            // Un unico Reset para todas las altas y bajas del refresco: ver LiteRowCollection.
            Rows.ApplyBatch(removedIndexes, added);
        }
        else if (filterRelevantChange && HasActiveFilter)
        {
            // Reconstruir la vista solo cuando un cambio puede hacer entrar o salir filas del
            // filtro activo. Hacerlo en cada refresco obliga al DataGrid a regenerar sus
            // contenedores y hace saltar la rejilla mientras se esta mirando.
            RowsView.Refresh();
        }
    }

    private void ApplyCounters(LiteGridSnapshot snapshot)
    {
        var counters = snapshot.Counters;
        TotalFound = counters.Total;
        PendingCount = counters.Pending;
        InFlightCount = counters.InFlight;
        SucceededCount = counters.Succeeded;
        DefinitiveErrorCount = counters.DefinitiveError;
        SkippedCount = counters.SkippedHistory;
    }

    private bool HasActiveFilter
        => !string.IsNullOrWhiteSpace(FilterText)
           || !string.Equals(StatusFilter, "Todos", StringComparison.OrdinalIgnoreCase)
           || ShowOnlyReview;

    /// <summary>
    /// Documento completo (incluyendo RequestJson/ResponseJson) para el dialogo de detalle,
    /// que la rejilla ya no lleva desde el refresco ligero. Ver GetDocumentsForGrid.
    /// </summary>
    public LiteDocument? GetDocument(long id) => _repository.GetDocument(id);

    public void SaveConfig() => _configService.Save(Config);

    public void ExportCsv(string path) => LiteExportService.ExportCsv(GetExportDocuments(), path);

    public void ExportExcel(string path) => LiteExportService.ExportExcel(GetExportDocuments(), path);

    private List<LiteDocument> GetExportDocuments()
    {
        if (string.IsNullOrWhiteSpace(CurrentExecutionId))
        {
            return new List<LiteDocument>();
        }

        var all = _repository.GetDocuments(CurrentExecutionId);
        if (!HasActiveFilter)
        {
            return all;
        }

        return all.Where(d => MatchesFilter(d.FileName, d.Status, LiteDocumentRow.IsReviewableEstado(d.Estado))).ToList();
    }

    private bool FilterRow(object item)
    {
        if (item is not LiteDocumentRow row)
        {
            return false;
        }

        return MatchesFilter(row.FileName, row.Status, row.IsLowConfidence);
    }

    private bool MatchesFilter(string fileName, string status, bool isReviewable)
    {
        if (!string.IsNullOrWhiteSpace(FilterText)
            && fileName.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (ShowOnlyReview && !isReviewable)
        {
            return false;
        }

        return string.Equals(StatusFilter, "Todos", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, StatusFilter, StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
