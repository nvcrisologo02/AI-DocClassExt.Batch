using System.Text.Json;
using System.IO;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.Services;

public class SettingsService
{
    private const string ConfigFileName = "config.json";
    private readonly string? _configPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public SettingsService(string? configPath = null)
    {
        _configPath = configPath;
    }

    public BatchConfig Load()
    {
        var configPath = GetConfigPath();
        if (!File.Exists(configPath))
        {
            return Normalize(new BatchConfig());
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<BatchConfig>(json, _jsonOptions) ?? new BatchConfig();
        return Normalize(config);
    }

    /// <summary>
    /// Garantiza que el config tiene un catálogo de entornos coherente:
    /// migra configs legacy (BackendUrl/FunctionKey sueltos) a un entorno "Producción",
    /// siembra "Producción" si no hay nada, y corrige SelectedEnvironment huérfano.
    /// Idempotente: un config ya migrado no se modifica.
    /// </summary>
    public static BatchConfig Normalize(BatchConfig config)
    {
        const string defaultName = "Producción";
        const string defaultUrl = "https://srbappprodocai.azurewebsites.net";

        if (config.Environments.Count == 0)
        {
            var legacyUrl = config.BackendUrl?.Trim() ?? string.Empty;
            var useDefault = string.IsNullOrWhiteSpace(legacyUrl)
                || string.Equals(legacyUrl, "http://localhost:7071", StringComparison.OrdinalIgnoreCase);

            config.Environments.Add(new EnvironmentConfig
            {
                Name = defaultName,
                BackendUrl = useDefault ? defaultUrl : legacyUrl,
                FunctionKey = config.FunctionKey ?? string.Empty
            });
            config.SelectedEnvironment = defaultName;
        }

        var selectedExists = config.Environments.Any(e =>
            string.Equals(e.Name, config.SelectedEnvironment, StringComparison.OrdinalIgnoreCase));
        if (!selectedExists)
        {
            config.SelectedEnvironment = config.Environments[0].Name;
        }

        return config;
    }

    public void Save(BatchConfig config)
    {
        var configPath = GetConfigPath();
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(configPath, json);
    }

    private string GetConfigPath()
    {
        return _configPath ?? Path.Combine(AppContext.BaseDirectory, ConfigFileName);
    }
}
