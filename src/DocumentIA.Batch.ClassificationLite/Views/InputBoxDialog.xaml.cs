using System.Windows;

namespace DocumentIA.Batch.ClassificationLite.Views;

/// <summary>
/// Cuadro de texto simple para pedir un unico valor (p. ej. el nombre de un entorno nuevo),
/// sin depender de Microsoft.VisualBasic.Interaction.InputBox ni de paquetes adicionales.
/// </summary>
public partial class InputBoxDialog : Window
{
    public string Value { get; private set; } = string.Empty;

    public InputBoxDialog(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Value = ValueBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
