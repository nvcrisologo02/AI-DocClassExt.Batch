using System.Windows;
using System.Windows.Controls;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.Views;

public partial class EnvironmentEditorDialog : Window
{
    private readonly List<EnvironmentConfig> _environments;
    private bool _updatingFields;

    public EnvironmentEditorDialog(List<EnvironmentConfig> environments)
    {
        InitializeComponent();

        _environments = environments;
        EnvironmentsListBox.ItemsSource = _environments;
        if (_environments.Count > 0)
        {
            EnvironmentsListBox.SelectedIndex = 0;
        }
    }

    /// <summary>Catálogo resultante. Solo válido cuando DialogResult == true.</summary>
    public List<EnvironmentConfig>? Result { get; private set; }

    private EnvironmentConfig? Selected => EnvironmentsListBox.SelectedItem as EnvironmentConfig;

    private void EnvironmentsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _updatingFields = true;
        try
        {
            var env = Selected;
            EditorPanel.IsEnabled = env is not null;
            NameTextBox.Text = env?.Name ?? string.Empty;
            UrlTextBox.Text = env?.BackendUrl ?? string.Empty;
            KeyPasswordBox.Password = env?.FunctionKey ?? string.Empty;
        }
        finally
        {
            _updatingFields = false;
        }
    }

    private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFields || Selected is null)
        {
            return;
        }

        Selected.Name = NameTextBox.Text;
        EnvironmentsListBox.Items.Refresh();
    }

    private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFields || Selected is null)
        {
            return;
        }

        Selected.BackendUrl = UrlTextBox.Text;
    }

    private void KeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingFields || Selected is null)
        {
            return;
        }

        Selected.FunctionKey = KeyPasswordBox.Password;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var env = new EnvironmentConfig { Name = UniqueName("Nuevo entorno") };
        _environments.Add(env);
        EnvironmentsListBox.Items.Refresh();
        EnvironmentsListBox.SelectedItem = env;
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null)
        {
            return;
        }

        var index = EnvironmentsListBox.SelectedIndex;
        _environments.Remove(Selected);
        EnvironmentsListBox.Items.Refresh();
        EnvironmentsListBox.SelectedIndex = Math.Min(index, _environments.Count - 1);
        if (_environments.Count == 0)
        {
            EnvironmentsListBox_SelectionChanged(EnvironmentsListBox, null!);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var error = Validate();
        if (error is not null)
        {
            ValidationText.Text = error;
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        Result = _environments;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private string? Validate()
    {
        foreach (var env in _environments)
        {
            if (string.IsNullOrWhiteSpace(env.Name))
            {
                return "Todos los entornos deben tener nombre.";
            }

            if (string.IsNullOrWhiteSpace(env.BackendUrl))
            {
                return $"El entorno '{env.Name}' no tiene Backend URL.";
            }
        }

        var duplicated = _environments
            .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        return duplicated is not null
            ? $"Hay más de un entorno llamado '{duplicated.Key}'. Los nombres deben ser únicos."
            : null;
    }

    private string UniqueName(string baseName)
    {
        if (!_environments.Any(e => string.Equals(e.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        var i = 2;
        while (_environments.Any(e => string.Equals(e.Name, $"{baseName} {i}", StringComparison.OrdinalIgnoreCase)))
        {
            i++;
        }

        return $"{baseName} {i}";
    }
}
