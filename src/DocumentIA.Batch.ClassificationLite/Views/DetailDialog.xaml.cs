using System.Windows;
using DocumentIA.Batch.ClassificationLite.ViewModels;

namespace DocumentIA.Batch.ClassificationLite.Views;

public partial class DetailDialog : Window
{
    public DetailDialog(LiteDocumentRow row)
    {
        InitializeComponent();
        DataContext = row;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
