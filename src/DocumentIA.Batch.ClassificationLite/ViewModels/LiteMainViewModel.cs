using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;

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
    }

    public LiteConfig Config { get; private set; }

    public ObservableCollection<LiteDocumentRow> Rows { get; } = new();

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
        if (string.IsNullOrWhiteSpace(CurrentExecutionId))
        {
            return;
        }

        if (!string.Equals(_loadedExecutionId, CurrentExecutionId, StringComparison.Ordinal))
        {
            Rows.Clear();
            _rowsById.Clear();
            _loadedExecutionId = CurrentExecutionId;
        }

        var documents = _repository.GetDocumentsForGrid(CurrentExecutionId);
        var seenIds = new HashSet<long>();

        foreach (var document in documents)
        {
            seenIds.Add(document.Id);

            if (_rowsById.TryGetValue(document.Id, out var existingRow))
            {
                existingRow.UpdateFrom(document);
            }
            else
            {
                var row = LiteDocumentRow.From(document);
                _rowsById[document.Id] = row;
                Rows.Add(row);
            }
        }

        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            var id = Rows[i].Id;
            if (!seenIds.Contains(id))
            {
                Rows.RemoveAt(i);
                _rowsById.Remove(id);
            }
        }

        RowsView.Refresh();
    }

    public void RefreshCounters()
    {
        if (string.IsNullOrWhiteSpace(CurrentExecutionId))
        {
            return;
        }

        var counters = _repository.GetCounters(CurrentExecutionId);
        TotalFound = counters.Total;
        PendingCount = counters.Pending;
        InFlightCount = counters.InFlight;
        SucceededCount = counters.Succeeded;
        DefinitiveErrorCount = counters.DefinitiveError;
        SkippedCount = counters.SkippedHistory;
    }

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
        var hasFilter = !string.IsNullOrWhiteSpace(FilterText)
            || !string.Equals(StatusFilter, "Todos", StringComparison.OrdinalIgnoreCase)
            || ShowOnlyReview;

        if (!hasFilter)
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
