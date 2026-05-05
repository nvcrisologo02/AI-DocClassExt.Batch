using System.Windows.Controls;
using DocumentIA.Batch.ViewModels;

namespace DocumentIA.Batch.Views;

/// <summary>
/// Interaction logic for HistorialView.xaml
/// </summary>
public partial class HistorialView : UserControl
{
    public HistorialView()
    {
        InitializeComponent();
        DataContext = new HistorialViewModel();
    }
}
