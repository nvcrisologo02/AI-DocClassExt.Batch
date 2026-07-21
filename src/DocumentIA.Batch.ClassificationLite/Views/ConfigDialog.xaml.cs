using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.ClassificationLite.Views;

public partial class ConfigDialog : Window
{
    private readonly LiteConfig _config;

    /// <summary>
    /// Copia de trabajo de la lista de entornos: Añadir/Eliminar la modifican libremente sin
    /// tocar _config.Environments hasta Guardar, para que Cancelar descarte esos cambios igual
    /// que descarta el resto de campos del dialogo (que solo se aplican en Save_Click).
    /// </summary>
    private readonly List<EnvironmentConfig> _environments;

    public ConfigDialog(LiteConfig config)
    {
        InitializeComponent();
        _config = config;
        _environments = new List<EnvironmentConfig>(config.Environments);
        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        var initialSelection = _environments.FirstOrDefault(e =>
            string.Equals(e.Name, _config.SelectedEnvironment, StringComparison.OrdinalIgnoreCase))
            ?? _environments.FirstOrDefault();
        RefreshEnvironmentCombo(initialSelection);

        ParallelBox.Text = _config.ParallelQueries.ToString(CultureInfo.InvariantCulture);
        BatchSizeBox.Text = _config.InternalBatchSize.ToString(CultureInfo.InvariantCulture);
        PollingBox.Text = _config.PollingIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        MaxRetriesBox.Text = _config.MaxRetries.ToString(CultureInfo.InvariantCulture);
        ModelBox.Text = _config.Model;
        ProviderCombo.Text = _config.Provider;
        LevelCombo.Text = _config.ClassificationLevel;
        OnlyClassificationCheck.IsChecked = _config.OnlyClassification;
        GenerateSummaryCheck.IsChecked = _config.GenerateSummary;
        ForceReprocessCheck.IsChecked = _config.ForceReprocess;
        SkipProcessedCheck.IsChecked = _config.SkipAlreadyProcessed;
    }

    private void EnvironmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EnvironmentCombo.SelectedItem is EnvironmentConfig environment)
        {
            BackendUrlBox.Text = environment.BackendUrl;
            FunctionKeyBox.Text = environment.FunctionKey;
        }
    }

    private void AddEnvironment_Click(object sender, RoutedEventArgs e)
    {
        // Guarda en el objeto en edicion lo que el usuario haya tecleado en el entorno
        // seleccionado antes de cambiar de seleccion, igual que hace Save_Click.
        if (EnvironmentCombo.SelectedItem is EnvironmentConfig current)
        {
            current.BackendUrl = BackendUrlBox.Text.Trim();
            current.FunctionKey = FunctionKeyBox.Text.Trim();
        }

        var dialog = new InputBoxDialog("Nuevo entorno", "Nombre del entorno (por ejemplo DEV o PRE):") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var name = dialog.Value;
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("El nombre del entorno no puede estar vacio.", "Configuracion",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_environments.Any(env => string.Equals(env.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"Ya existe un entorno llamado '{name}'.", "Configuracion",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var environment = new EnvironmentConfig { Name = name, BackendUrl = string.Empty, FunctionKey = string.Empty };
        _environments.Add(environment);
        RefreshEnvironmentCombo(environment);
    }

    private void RemoveEnvironment_Click(object sender, RoutedEventArgs e)
    {
        if (EnvironmentCombo.SelectedItem is not EnvironmentConfig environment)
        {
            return;
        }

        if (_environments.Count <= 1)
        {
            MessageBox.Show("Debe quedar al menos un entorno configurado: no se puede eliminar el ultimo.",
                "Configuracion", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"¿Eliminar el entorno '{environment.Name}'? Esta accion no se puede deshacer.",
            "Configuracion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _environments.Remove(environment);
        RefreshEnvironmentCombo(_environments.FirstOrDefault());
    }

    /// <summary>
    /// El desplegable enlaza contra _environments, un List&lt;T&gt; simple sin notificacion de
    /// cambios: reasignar ItemsSource fuerza a la ComboBox a releer la lista tras anadir o
    /// quitar un entorno.
    /// </summary>
    private void RefreshEnvironmentCombo(EnvironmentConfig? selected)
    {
        EnvironmentCombo.ItemsSource = null;
        EnvironmentCombo.ItemsSource = _environments;
        EnvironmentCombo.SelectedItem = selected ?? _environments.FirstOrDefault();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (EnvironmentCombo.SelectedItem is EnvironmentConfig environment)
        {
            environment.BackendUrl = BackendUrlBox.Text.Trim();
            environment.FunctionKey = FunctionKeyBox.Text.Trim();
            _config.SelectedEnvironment = environment.Name;
        }

        _config.Environments = _environments;
        _config.ParallelQueries = ParseInt(ParallelBox.Text, _config.ParallelQueries);
        _config.InternalBatchSize = ParseInt(BatchSizeBox.Text, _config.InternalBatchSize);
        _config.PollingIntervalSeconds = ParseInt(PollingBox.Text, _config.PollingIntervalSeconds);
        _config.MaxRetries = ParseInt(MaxRetriesBox.Text, _config.MaxRetries);
        _config.Model = string.IsNullOrWhiteSpace(ModelBox.Text) ? "auto" : ModelBox.Text.Trim();
        _config.Provider = string.IsNullOrWhiteSpace(ProviderCombo.Text) ? "auto" : ProviderCombo.Text.Trim();
        _config.ClassificationLevel = string.IsNullOrWhiteSpace(LevelCombo.Text) ? "TDN1_TDN2" : LevelCombo.Text.Trim();
        _config.OnlyClassification = OnlyClassificationCheck.IsChecked == true;
        _config.GenerateSummary = GenerateSummaryCheck.IsChecked == true;
        _config.ForceReprocess = ForceReprocessCheck.IsChecked == true;
        _config.SkipAlreadyProcessed = SkipProcessedCheck.IsChecked == true;

        LiteConfigService.Normalize(_config);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static int ParseInt(string text, int fallback)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
