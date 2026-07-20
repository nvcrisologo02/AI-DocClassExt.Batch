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
}
