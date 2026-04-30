using System.Windows;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.Views;

public partial class BatchSummaryDialog : Window
{
    public BatchSummaryDialog(BatchRunSummary summary)
    {
        InitializeComponent();
        DataContext = summary;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
