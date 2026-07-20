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

    public ConfigDialog(LiteConfig config)
    {
        InitializeComponent();
        _config = config;
        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        EnvironmentCombo.ItemsSource = _config.Environments;
        EnvironmentCombo.SelectedItem = _config.Environments.FirstOrDefault(e =>
            string.Equals(e.Name, _config.SelectedEnvironment, StringComparison.OrdinalIgnoreCase))
            ?? _config.Environments.FirstOrDefault();

        ParallelBox.Text = _config.ParallelQueries.ToString(CultureInfo.InvariantCulture);
        BatchSizeBox.Text = _config.InternalBatchSize.ToString(CultureInfo.InvariantCulture);
        PollingBox.Text = _config.PollingIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        MaxRetriesBox.Text = _config.MaxRetries.ToString(CultureInfo.InvariantCulture);
        ModelBox.Text = _config.Model;
        ProviderCombo.Text = _config.Provider;
        LevelCombo.Text = _config.ClassificationLevel;
        OnlyClassificationCheck.IsChecked = _config.OnlyClassification;
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (EnvironmentCombo.SelectedItem is EnvironmentConfig environment)
        {
            environment.BackendUrl = BackendUrlBox.Text.Trim();
            environment.FunctionKey = FunctionKeyBox.Text.Trim();
            _config.SelectedEnvironment = environment.Name;
        }

        _config.ParallelQueries = ParseInt(ParallelBox.Text, _config.ParallelQueries);
        _config.InternalBatchSize = ParseInt(BatchSizeBox.Text, _config.InternalBatchSize);
        _config.PollingIntervalSeconds = ParseInt(PollingBox.Text, _config.PollingIntervalSeconds);
        _config.MaxRetries = ParseInt(MaxRetriesBox.Text, _config.MaxRetries);
        _config.Model = string.IsNullOrWhiteSpace(ModelBox.Text) ? "auto" : ModelBox.Text.Trim();
        _config.Provider = string.IsNullOrWhiteSpace(ProviderCombo.Text) ? "auto" : ProviderCombo.Text.Trim();
        _config.ClassificationLevel = string.IsNullOrWhiteSpace(LevelCombo.Text) ? "TDN1_TDN2" : LevelCombo.Text.Trim();
        _config.OnlyClassification = OnlyClassificationCheck.IsChecked == true;
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
