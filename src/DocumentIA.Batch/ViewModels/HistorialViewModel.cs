using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using DocumentIA.Batch.Models;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.ViewModels;

/// <summary>
/// ViewModel para la pestaña de Historial.
/// Muestra directamente los ficheros procesados (JOIN BatchRunFile + BatchRun).
/// </summary>
public class HistorialViewModel : ObservableObject
{
    private readonly BatchHistorialService _historialService;
    private readonly HistorialExportService _exportService;

    private DateTime? _fechaDesde;
    private DateTime? _fechaHasta;
    private string? _filtroTipologia;
    private int _totalFiles;
    private string _statusText = "Sin datos";
    private bool _isImporting;
    private bool? _selectAllFiles = false;
    private int _selectedFilesCount;
    private bool _isUpdatingSelection;

    public ObservableCollection<HistorialFileRow> Files { get; }
    public ObservableCollection<string> AvailableTipologias { get; }

    public ICommand LoadFilesCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportExcelCommand { get; }
    public ICommand ImportarRunsCommand { get; }
    public ICommand ClearAndReimportCommand { get; }

    public bool IsImporting
    {
        get => _isImporting;
        private set { _isImporting = value; OnPropertyChanged(); }
    }

    public bool? SelectAllFiles
    {
        get => _selectAllFiles;
        set
        {
            if (_selectAllFiles == value)
            {
                return;
            }

            _selectAllFiles = value;
            OnPropertyChanged();

            if (_isUpdatingSelection || value is null)
            {
                return;
            }

            SetAllSelection(value.Value);
        }
    }

    public int SelectedFilesCount
    {
        get => _selectedFilesCount;
        private set => SetProperty(ref _selectedFilesCount, value);
    }

    public HistorialViewModel() : this(new BatchHistorialService(), new HistorialExportService())
    {
    }

    public HistorialViewModel(BatchHistorialService historialService, HistorialExportService exportService)
    {
        _historialService = historialService;
        _exportService = exportService;

        var today = DateTime.Today;
        _fechaDesde = today.AddDays(-30);
        _fechaHasta = today;
        _filtroTipologia = "Todas";

        Files = new ObservableCollection<HistorialFileRow>();
        AvailableTipologias = new ObservableCollection<string> { "Todas" };

        LoadFilesCommand        = new RelayCommand(_ => _ = LoadFilesAsync(),         _ => true);
        RefreshCommand          = new RelayCommand(_ => _ = RefreshAsync(),            _ => true);
        ExportCsvCommand        = new RelayCommand(_ => _ = ExportCsvAsync(),          _ => SelectedFilesCount > 0);
        ExportExcelCommand      = new RelayCommand(_ => _ = ExportExcelAsync(),        _ => SelectedFilesCount > 0);
        ImportarRunsCommand     = new RelayCommand(_ => _ = ImportarRunsAsync(),       _ => !IsImporting);
        ClearAndReimportCommand = new RelayCommand(_ => _ = ClearAndReimportAsync(),   _ => !IsImporting);

        _ = LoadAvailableTipologiasAsync();
        _ = LoadFilesAsync();
    }

    // ── Propiedades de filtro ─────────────────────────────────────────────────

    public DateTime? FechaDesde
    {
        get => _fechaDesde;
        set => SetProperty(ref _fechaDesde, value);
    }

    public DateTime? FechaHasta
    {
        get => _fechaHasta;
        set => SetProperty(ref _fechaHasta, value);
    }

    public string? FiltroTipologia
    {
        get => _filtroTipologia;
        set => SetProperty(ref _filtroTipologia, value);
    }

    public int TotalFiles
    {
        get => _totalFiles;
        set => SetProperty(ref _totalFiles, value);
    }

    public int UnselectedFilesCount => Files.Count - SelectedFilesCount;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // ── Carga ─────────────────────────────────────────────────────────────────

    private async Task LoadFilesAsync()
    {
        try
        {
            StatusText = "Cargando...";

            UnsubscribeFromFileSelectionChanges();

            var files = await _historialService.GetAllFilesAsync(
                from: FechaDesde,
                to: FechaHasta,
                tipologia: FiltroTipologia,
                page: 0,
                pageSize: 500);

            var total = await _historialService.GetTotalFilesAsync(
                from: FechaDesde,
                to: FechaHasta,
                tipologia: FiltroTipologia);

            Files.Clear();
            foreach (var f in files)
            {
                f.IsSelected = false;
                Files.Add(f);
                f.PropertyChanged += OnFilePropertyChanged;
            }

            TotalFiles = total;
            RefreshSelectionState();
            StatusText = total == 0
                ? "No se encontraron ficheros."
                : $"{total} ficheros encontrados. Selecciona los que quieras exportar.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error cargando ficheros: {ex.Message}");
        }
    }

    private async Task LoadAvailableTipologiasAsync()
    {
        try
        {
            var tipologias = await _historialService.GetAvailableTipologiasAsync();

            var current = FiltroTipologia;
            AvailableTipologias.Clear();
            AvailableTipologias.Add("Todas");
            foreach (var t in tipologias)
                if (!string.IsNullOrWhiteSpace(t))
                    AvailableTipologias.Add(t);

            FiltroTipologia = AvailableTipologias.Contains(current ?? "Todas")
                ? current
                : "Todas";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando tipologías: {ex.Message}");
        }
    }

    private async Task RefreshAsync()
    {
        await LoadAvailableTipologiasAsync();
        await LoadFilesAsync();
    }

    // ── Exportación ───────────────────────────────────────────────────────────

    private async Task ExportCsvAsync()
    {
        var snapshot = Files.Where(file => file.IsSelected).ToList();
        if (snapshot.Count == 0)
        {
            StatusText = "Selecciona al menos un fichero para exportar.";
            return;
        }

        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = $"historial_{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                await Task.Run(() => _exportService.ExportCsv(dialog.FileName, snapshot));
                StatusText = $"Exportados {snapshot.Count} ficheros a CSV: {Path.GetFileName(dialog.FileName)}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error exportando CSV: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error en ExportCsv: {ex.Message}");
        }
    }

    private async Task ExportExcelAsync()
    {
        var snapshot = Files.Where(file => file.IsSelected).ToList();
        if (snapshot.Count == 0)
        {
            StatusText = "Selecciona al menos un fichero para exportar.";
            return;
        }

        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel files (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName = $"historial_{DateTime.Now:yyyyMMdd-HHmmss}.xlsx"
            };

            if (dialog.ShowDialog() == true)
            {
                await Task.Run(() => _exportService.ExportExcel(dialog.FileName, snapshot));
                StatusText = $"Exportados {snapshot.Count} ficheros a Excel: {Path.GetFileName(dialog.FileName)}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error exportando Excel: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error en ExportExcel: {ex.Message}");
        }
    }

    private void RaiseCanExecuteExport()
    {
        ((RelayCommand)ExportCsvCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ExportExcelCommand).RaiseCanExecuteChanged();
    }

    private void SetAllSelection(bool selected)
    {
        _isUpdatingSelection = true;
        try
        {
            foreach (var file in Files)
            {
                file.IsSelected = selected;
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }

        RefreshSelectionState();
    }

    private void RefreshSelectionState()
    {
        var selectedCount = Files.Count(file => file.IsSelected);
        SelectedFilesCount = selectedCount;
        OnPropertyChanged(nameof(UnselectedFilesCount));

        _isUpdatingSelection = true;
        try
        {
            SelectAllFiles = Files.Count switch
            {
                0 => false,
                _ when selectedCount == 0 => false,
                _ when selectedCount == Files.Count => true,
                _ => null
            };
        }
        finally
        {
            _isUpdatingSelection = false;
        }

        RaiseCanExecuteExport();
    }

    private void OnFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistorialFileRow.IsSelected))
        {
            RefreshSelectionState();
        }
    }

    private void UnsubscribeFromFileSelectionChanges()
    {
        foreach (var file in Files)
        {
            file.PropertyChanged -= OnFilePropertyChanged;
        }
    }

    // ── Importación / limpieza ────────────────────────────────────────────────

    private async Task ImportarRunsAsync()
    {
        IsImporting = true;
        StatusText = "Importando ejecuciones previas...";

        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            var (imported, skipped) = await _historialService.ImportExistingRunsAsync(progress);

            StatusText = imported == 0
                ? $"Sin nuevas ejecuciones para importar ({skipped} ya registradas)."
                : $"Importadas {imported} ejecuciones. {skipped} ya existían en historial.";

            if (imported > 0)
                await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error durante la importación: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error en ImportarRunsAsync: {ex.Message}");
        }
        finally
        {
            IsImporting = false;
        }
    }

    private async Task ClearAndReimportAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            "¿Borrar TODO el historial y regenerarlo desde las carpetas runs/?\n\nEsta operación no se puede deshacer.",
            "Confirmar borrado y regeneración",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes)
            return;

        IsImporting = true;
        StatusText = "Borrando historial...";

        try
        {
            await _historialService.ClearAllHistoryAsync();
            UnsubscribeFromFileSelectionChanges();
            Files.Clear();
            RefreshSelectionState();

            StatusText = "Historial borrado. Importando desde runs/...";
            var progress = new Progress<string>(msg => StatusText = msg);
            var (imported, _) = await _historialService.ImportExistingRunsAsync(progress);

            StatusText = imported == 0
                ? "No se encontraron ejecuciones en runs/."
                : $"Regenerado: {imported} ejecuciones importadas.";

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error al regenerar historial: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error en ClearAndReimportAsync: {ex.Message}");
        }
        finally
        {
            IsImporting = false;
        }
    }
}
