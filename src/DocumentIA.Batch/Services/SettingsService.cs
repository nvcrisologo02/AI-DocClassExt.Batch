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
            return new BatchConfig();
        }

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<BatchConfig>(json, _jsonOptions) ?? new BatchConfig();
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
