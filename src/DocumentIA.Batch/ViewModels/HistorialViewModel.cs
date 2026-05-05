using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using DocumentIA.Batch.Models;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.ViewModels;

/// <summary>
/// ViewModel para la pestaña de Historial.
/// Gestiona consulta, filtrado y exportación del historial de ejecuciones.
/// </summary>
public class HistorialViewModel : ObservableObject
{
    private readonly BatchHistorialService _historialService;
    private readonly HistorialExportService _exportService;

    private DateTime? _fechaDesde;
    private DateTime? _fechaHasta;
    private string? _filtroTipologia;
    private BatchRunRecord? _selectedRun;
    private int _totalRuns;
    private string _statusText = "Sin datos";
    private int _currentPage;

    public ObservableCollection<BatchRunRecord> Runs { get; }
    public ObservableCollection<BatchRunFileRecord> SelectedRunFiles { get; }
    public ObservableCollection<string> AvailableTipologias { get; }

    public ICommand LoadRunsCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportExcelCommand { get; }
    public ICommand ImportarRunsCommand { get; }
    public ICommand DeleteRunCommand { get; }

    private bool _isImporting;
    public bool IsImporting
    {
        get => _isImporting;
        private set { _isImporting = value; OnPropertyChanged(); }
    }

    private const int PageSize = 50;

    public HistorialViewModel() : this(new BatchHistorialService(), new HistorialExportService())
    {
    }

    public HistorialViewModel(BatchHistorialService historialService, HistorialExportService exportService)
    {
        _historialService = historialService;
        _exportService = exportService;

        Runs = new ObservableCollection<BatchRunRecord>();
        SelectedRunFiles = new ObservableCollection<BatchRunFileRecord>();
        AvailableTipologias = new ObservableCollection<string> { "Todas" };

        LoadRunsCommand = new RelayCommand(_ => _ = LoadRunsAsync(), _ => true);
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => true);
        ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => SelectedRun is not null && SelectedRunFiles.Count > 0);
        ExportExcelCommand = new RelayCommand(_ => ExportExcel(), _ => SelectedRun is not null && SelectedRunFiles.Count > 0);
        ImportarRunsCommand = new RelayCommand(_ => _ = ImportarRunsAsync(), _ => !IsImporting);
        DeleteRunCommand = new RelayCommand(_ => _ = DeleteRunAsync(), _ => SelectedRun is not null);

        // Cargar tipologías disponibles al inicializar
        _ = LoadAvailableTipologiasAsync();
    }

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

    public BatchRunRecord? SelectedRun
    {
        get => _selectedRun;
        set
        {
            if (SetProperty(ref _selectedRun, value))
            {
                if (value is not null)
                {
                    _ = LoadRunFilesAsync(value.Id);
                }
                else
                {
                    SelectedRunFiles.Clear();
                }

                ((RelayCommand)ExportCsvCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ExportExcelCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public int TotalRuns
    {
        get => _totalRuns;
        set => SetProperty(ref _totalRuns, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private async Task LoadAvailableTipologiasAsync()
    {
        try
        {
            var tipologias = await _historialService.GetAvailableTipologiasAsync();
            AvailableTipologias.Clear();
            AvailableTipologias.Add("Todas");
            foreach (var tip in tipologias)
            {
                AvailableTipologias.Add(tip);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando tipologías: {ex.Message}");
        }
    }

    private async Task LoadRunsAsync()
    {
        try
        {
            _currentPage = 0;
            StatusText = "Cargando...";

            var runs = await _historialService.GetRunsAsync(
                from: FechaDesde,
                to: FechaHasta,
                tipologia: FiltroTipologia,
                page: _currentPage,
                pageSize: PageSize);

            var total = await _historialService.GetTotalRunsAsync(
                from: FechaDesde,
                to: FechaHasta,
                tipologia: FiltroTipologia);

            Runs.Clear();
            foreach (var run in runs)
            {
                Runs.Add(run);
            }

            TotalRuns = total;
            StatusText = $"{total} ejecuciones encontradas";

            SelectedRun = null;
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error cargando runs: {ex.Message}");
        }
    }

    private async Task LoadRunFilesAsync(int runId)
    {
        try
        {
            var files = await _historialService.GetRunFilesAsync(runId);
            SelectedRunFiles.Clear();
            foreach (var file in files)
            {
                SelectedRunFiles.Add(file);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando archivos del run: {ex.Message}");
        }
    }

    private async Task RefreshAsync()
    {
        await LoadAvailableTipologiasAsync();
        await LoadRunsAsync();
    }

    private void ExportCsv()
    {
        if (SelectedRun is null)
            return;

        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = $"historial_{SelectedRun.RunKey}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                _exportService.ExportCsv(dialog.FileName, SelectedRun, SelectedRunFiles);
                StatusText = $"Exportado a CSV: {Path.GetFileName(dialog.FileName)}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error exportando CSV: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error en ExportCsv: {ex.Message}");
        }
    }

    private void ExportExcel()
    {
        if (SelectedRun is null)
            return;

        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel files (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName = $"historial_{SelectedRun.RunKey}.xlsx"
            };

            if (dialog.ShowDialog() == true)
            {
                _exportService.ExportExcel(dialog.FileName, SelectedRun, SelectedRunFiles);
                StatusText = $"Exportado a Excel: {Path.GetFileName(dialog.FileName)}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error exportando Excel: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error en ExportExcel: {ex.Message}");
        }
    }

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

    private async Task DeleteRunAsync()
    {
        if (SelectedRun is null)
            return;

        var confirm = System.Windows.MessageBox.Show(
            $"¿Eliminar la ejecución '{SelectedRun.RunKey}' y todos sus archivos del historial?",
            "Confirmar eliminación",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            var runId = SelectedRun.Id;
            await _historialService.DeleteRunAsync(runId);
            Runs.Remove(SelectedRun);
            SelectedRunFiles.Clear();
            SelectedRun = null;
            StatusText = "Ejecución eliminada del historial.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error eliminando: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error en DeleteRunAsync: {ex.Message}");
        }
    }
}
