using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using DocumentIA.Batch.Views;
using DocumentIA.Batch.Classification.Models;
using DocumentIA.Batch.Classification.ViewModels;

namespace DocumentIA.Batch.Classification;

public partial class MainWindow : Window
{
    private readonly ClassificationMainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new ClassificationMainViewModel();
        DataContext = _viewModel;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        _viewModel.AddFiles((string[])e.Data.GetData(DataFormats.FileDrop));
    }

    private void FilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || grid.SelectedItem is not ClassificationDocumentItem item)
        {
            return;
        }

        var outputText = BuildOutputText(item);
        var dialog = new BatchOutputDialog(item.FileName, outputText)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    private static string BuildOutputText(ClassificationDocumentItem item)
    {
        var summary = $$"""
        ==== SUMMARY ====
        FileName: {{item.FileName}}
        Status: {{item.Status}}
        RuntimeStatus: {{item.RuntimeStatus}}
        DocumentId: {{item.IdentificacionDocumento}}
        Typology: {{item.TipologiaIdentificada}}
        Confidence: {{item.ConfidenceDisplay}} (raw: {{item.ConfianzaGlobal}})
        Error: {{item.MensajeError}}
        OutputJsonPath: {{item.OutputJsonPath}}
        """;

        if (!string.IsNullOrWhiteSpace(item.OutputJsonPath) && File.Exists(item.OutputJsonPath))
        {
            var rawJson = File.ReadAllText(item.OutputJsonPath);
            return $"{summary}{Environment.NewLine}{Environment.NewLine}==== RAW JSON ===={Environment.NewLine}{rawJson}";
        }

        return summary;
    }
}