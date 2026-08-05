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
            var normalized = Normalize(config);
            foreach (var environment in normalized.Environments)
            {
                environment.FunctionKey = KeyObfuscator.Unprotect(environment.FunctionKey);
            }

            return normalized;
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
        var toSerialize = CloneWithEnvironments(normalized, env => new EnvironmentConfig
        {
            Name = env.Name,
            BackendUrl = env.BackendUrl,
            FunctionKey = KeyObfuscator.Protect(env.FunctionKey)
        });

        File.WriteAllText(_configPath, JsonSerializer.Serialize(toSerialize, JsonOptions));
    }

    /// <summary>
    /// Serializa una copia de <paramref name="config"/> con la Function Key de cada entorno
    /// redactada (vacia), para guardar como snapshot en la BD sin filtrar keys en claro. No
    /// muta la config original.
    /// </summary>
    public static string SerializeRedacted(LiteConfig config)
    {
        var redacted = CloneWithEnvironments(config, env => new EnvironmentConfig
        {
            Name = env.Name,
            BackendUrl = env.BackendUrl,
            FunctionKey = string.Empty
        });

        return JsonSerializer.Serialize(redacted);
    }

    private static LiteConfig CloneWithEnvironments(LiteConfig config, Func<EnvironmentConfig, EnvironmentConfig> mapEnvironment)
    {
        return new LiteConfig
        {
            SelectedEnvironment = config.SelectedEnvironment,
            Environments = config.Environments.Select(mapEnvironment).ToList(),
            ParallelQueries = config.ParallelQueries,
            InternalBatchSize = config.InternalBatchSize,
            PollingIntervalSeconds = config.PollingIntervalSeconds,
            ClassificationLevel = config.ClassificationLevel,
            Provider = config.Provider,
            Model = config.Model,
            OnlyClassification = config.OnlyClassification,
            ForceReprocess = config.ForceReprocess,
            MaxRetries = config.MaxRetries,
            SkipAlreadyProcessed = config.SkipAlreadyProcessed,
            GenerateSummary = config.GenerateSummary,
            Solicitante = config.Solicitante
        };
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
