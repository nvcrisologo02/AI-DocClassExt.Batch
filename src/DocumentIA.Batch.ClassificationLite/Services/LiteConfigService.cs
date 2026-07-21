using System.IO;
using System.Text.Json;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.ClassificationLite.Services;

public class LiteConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _configPath;

    public LiteConfigService(string? configPath = null)
    {
        _configPath = configPath ?? Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    public LiteConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            return Normalize(new LiteConfig());
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<LiteConfig>(json) ?? new LiteConfig();
            return Normalize(config);
        }
        catch (JsonException)
        {
            // config.json corrupto: se parte de defaults sin romper el arranque.
            return Normalize(new LiteConfig());
        }
    }

    public void Save(LiteConfig config)
    {
        var normalized = Normalize(config);
        File.WriteAllText(_configPath, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    public static LiteConfig Normalize(LiteConfig config)
    {
        config.ParallelQueries = Math.Clamp(config.ParallelQueries, 1, 10);
        config.InternalBatchSize = Math.Clamp(config.InternalBatchSize, 50, 10000);
        config.PollingIntervalSeconds = Math.Clamp(config.PollingIntervalSeconds, 10, 600);
        config.MaxRetries = Math.Clamp(config.MaxRetries, 0, 10);

        if (config.Environments.Count == 0)
        {
            config.Environments.Add(new EnvironmentConfig
            {
                Name = "PRO",
                BackendUrl = "https://srbappprodocai.azurewebsites.net",
                FunctionKey = string.Empty
            });
        }

        if (!config.Environments.Any(e => string.Equals(e.Name, config.SelectedEnvironment, StringComparison.OrdinalIgnoreCase)))
        {
            config.SelectedEnvironment = config.Environments[0].Name;
        }

        return config;
    }
}
