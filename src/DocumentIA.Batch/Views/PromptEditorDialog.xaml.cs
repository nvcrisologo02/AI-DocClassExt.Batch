using System.Windows;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.Views;

public partial class PromptEditorDialog : Window
{
    public PromptEditorDialog(string tipologiaCode, PromptOverride? promptOverride)
    {
        InitializeComponent();

        TipologiaText.Text = $"Editar Prompts - {tipologiaCode}";
        SystemPromptTextBox.Text = promptOverride?.SystemPrompt ?? string.Empty;
        UserPromptTemplateTextBox.Text = promptOverride?.UserPromptTemplate ?? string.Empty;
    }

    public PromptOverride? PromptOverride { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var systemPrompt = SystemPromptTextBox.Text.Trim();
        var userPromptTemplate = UserPromptTemplateTextBox.Text.Trim();

        PromptOverride = string.IsNullOrWhiteSpace(systemPrompt) && string.IsNullOrWhiteSpace(userPromptTemplate)
            ? null
            : new PromptOverride
            {
                SystemPrompt = systemPrompt,
                UserPromptTemplate = userPromptTemplate
            };

        DialogResult = true;
    }

    private void RestoreDefault_Click(object sender, RoutedEventArgs e)
    {
        PromptOverride = null;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
