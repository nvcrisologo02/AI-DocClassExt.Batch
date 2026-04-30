using System.Windows;

namespace DocumentIA.Batch.Views;

public partial class BatchOutputDialog : Window
{
    public BatchOutputDialog(string fileName, string outputText)
    {
        InitializeComponent();

        TitleText.Text = $"Salida completa - {fileName}";
        OutputTextBox.Text = outputText;
        OutputTextBox.CaretIndex = 0;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(OutputTextBox.Text ?? string.Empty);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
