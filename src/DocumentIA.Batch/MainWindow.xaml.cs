using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DocumentIA.Batch.Models;
using DocumentIA.Batch.ViewModels;

namespace DocumentIA.Batch;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        _viewModel.AddFiles(files);
    }

    private void FilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || grid.SelectedItem is not BatchFileItem item)
        {
            return;
        }

        if (_viewModel.ShowFileOutputCommand.CanExecute(item))
        {
            _viewModel.ShowFileOutputCommand.Execute(item);
        }
    }
}