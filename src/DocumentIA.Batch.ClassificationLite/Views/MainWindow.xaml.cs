using System.IO;
using System.Windows;
using System.Windows.Controls;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Services;
using DocumentIA.Batch.ClassificationLite.ViewModels;
using Microsoft.Win32;

namespace DocumentIA.Batch.ClassificationLite.Views;

public partial class MainWindow : Window
{
    private readonly LiteMainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new LiteMainViewModel(
            new LiteRepository(LiteRepository.GetDefaultDbPath()),
            new LiteConfigService());
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var pending = _viewModel.GetPendingRecovery();
            if (pending is null)
            {
                return;
            }

            var counters = new LiteRepository(LiteRepository.GetDefaultDbPath()).GetCounters(pending.ExecutionId);
            var answer = MessageBox.Show(
                $"Hay una ejecucion incompleta sobre '{pending.RootPath}'.\n\n" +
                $"Pendientes: {counters.Pending}\nEn vuelo: {counters.InFlight}\nCompletados: {counters.Succeeded}\n\n" +
                "¿Quieres continuarla? (No = descartarla)",
                "Ejecucion incompleta",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer == MessageBoxResult.Yes)
            {
                await _viewModel.ResumeExecutionAsync(pending);
            }
            else
            {
                _viewModel.DiscardExecution(pending);
            }
        }
        catch (Exception ex)
        {
            // Un fallo al consultar/recuperar la ejecucion incompleta no debe impedir abrir la ventana.
            MessageBox.Show(
                "No se pudo comprobar si habia una ejecucion incompleta pendiente:\n\n" + ex.Message,
                "Batch Classification Lite",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Selecciona la carpeta con los documentos" };
        if (dialog.ShowDialog() == true)
        {
            await SetPaths(new[] { dialog.FolderName });
        }
    }

    private async Task SetPaths(IReadOnlyList<string> paths)
    {
        SelectedPathText.Text = paths.Count == 1
            ? paths[0]
            : $"{paths.Count} elementos seleccionados";

        // Escanear y previsualizar en el grid inmediatamente para que se vea qué se va a
        // procesar; el motor solo arranca al pulsar Ejecutar.
        await _viewModel.PreviewAsync(paths, IncludeSubfoldersCheck.IsChecked == true);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped && dropped.Length > 0)
        {
            await SetPaths(dropped);
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPaths.Count == 0)
        {
            MessageBox.Show("Selecciona antes una carpeta o arrastra ficheros.", "Batch Classification Lite",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _viewModel.IncludeSubfolders = IncludeSubfoldersCheck.IsChecked == true;
        await _viewModel.StartAsync();
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => _viewModel.PauseExecution();

    private void Resume_Click(object sender, RoutedEventArgs e) => _viewModel.ResumeExecution();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _viewModel.CancelExecution();

    private void Config_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfigDialog(_viewModel.Config) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.SaveConfig();
        }
    }

    private void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "clasificacion.xlsx" };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.ExportExcel(dialog.FileName);
            MessageBox.Show("Exportacion completada.", "Batch Classification Lite");
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "clasificacion.csv" };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.ExportCsv(dialog.FileName);
            MessageBox.Show("Exportacion completada.", "Batch Classification Lite");
        }
    }

    private void ResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is LiteDocumentRow row)
        {
            // La rejilla se carga con GetDocumentsForGrid (sin RequestJson/ResponseJson, ver
            // arreglo del refresco ligero): el detalle los pide bajo demanda aqui, solo al
            // abrir el dialogo por doble clic.
            var full = _viewModel.GetDocument(row.Id);
            if (full is not null)
            {
                row.RequestJson = full.RequestJson;
                row.ResponseJson = full.ResponseJson;
                row.Summary = full.Summary;
            }

            new DetailDialog(row) { Owner = this }.ShowDialog();
        }
    }
}
