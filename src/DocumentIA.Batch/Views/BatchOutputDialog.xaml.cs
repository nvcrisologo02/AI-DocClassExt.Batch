using System.Diagnostics;
using System.IO;
using System.Windows;

namespace DocumentIA.Batch.Views;

public partial class BatchOutputDialog : Window
{
    private readonly string _filePath;

    public BatchOutputDialog(string fileName, string outputText, string? filePath = null)
    {
        InitializeComponent();

        _filePath = filePath ?? string.Empty;
        TitleText.Text = $"Salida completa - {fileName}";
        OutputTextBox.Text = outputText;
        OutputTextBox.CaretIndex = 0;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(OutputTextBox.Text ?? string.Empty);
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
        {
            MessageBox.Show("El fichero no se encontró.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir el fichero: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
