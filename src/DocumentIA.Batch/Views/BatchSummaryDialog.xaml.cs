using System.Windows;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.Views;

public partial class BatchSummaryDialog : Window
{
    private readonly Action? _exportCsv;
    private readonly Action? _exportExcel;

    public BatchSummaryDialog(BatchRunSummary summary, Action? exportCsv = null, Action? exportExcel = null)
    {
        InitializeComponent();
        DataContext = summary;
        _exportCsv = exportCsv;
        _exportExcel = exportExcel;
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        _exportCsv?.Invoke();
    }

    private void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        _exportExcel?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
