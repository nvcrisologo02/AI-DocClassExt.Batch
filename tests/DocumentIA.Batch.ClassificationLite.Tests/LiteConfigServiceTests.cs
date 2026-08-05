using System.IO;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteConfigServiceTests : IDisposable
{
    private readonly string _tempDir;

    public LiteConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string ConfigPath => Path.Combine(_tempDir, "config.json");

    [Fact]
    public void Load_SinFichero_DevuelveDefaultsConProPrecargado()
    {
        var service = new LiteConfigService(ConfigPath);

        var config = service.Load();

        Assert.Equal("PRO", config.SelectedEnvironment);
        var pro = Assert.Single(config.Environments);
        Assert.Equal("PRO", pro.Name);
        Assert.Equal("https://srbappprodocai.azurewebsites.net", pro.BackendUrl);
        Assert.Equal(2, config.ParallelQueries);
        Assert.Equal(1000, config.InternalBatchSize);
        Assert.Equal(60, config.PollingIntervalSeconds);
        Assert.Equal("TDN1_TDN2", config.ClassificationLevel);
        Assert.Equal("auto", config.Provider);
        Assert.Equal("auto", config.Model);
        Assert.True(config.OnlyClassification);
        Assert.False(config.ForceReprocess);
        Assert.Equal(3, config.MaxRetries);
        Assert.True(config.SkipAlreadyProcessed);
    }

    [Fact]
    public void Save_NoPersisteElSolicitante()
    {
        var service = new LiteConfigService(ConfigPath);
        var config = service.Load();
        config.Solicitante = "DocumentIA.Batch.ClassificationLite/nombre.apellido@sareb.es";

        service.Save(config);

        Assert.DoesNotContain("nombre.apellido@sareb.es", File.ReadAllText(ConfigPath));
        // Al recargar se vuelve a la etiqueta del programa: el valor real lo repone el arranque.
        Assert.Equal(LiteConfig.ProgramaSolicitante, service.Load().Solicitante);
    }

    [Fact]
    public void SaveYLoad_HacenRoundTrip()
    {
        var service = new LiteConfigService(ConfigPath);
        var config = service.Load();
        config.ParallelQueries = 4;
        config.ForceReprocess = true;
        config.Model = "gpt-4o";

        service.Save(config);
        var reloaded = new LiteConfigService(ConfigPath).Load();

        Assert.Equal(4, reloaded.ParallelQueries);
        Assert.True(reloaded.ForceReprocess);
        Assert.Equal("gpt-4o", reloaded.Model);
    }

    [Fact]
    public void Normalize_AplicaLimites()
    {
        var config = new LiteConfig
        {
            ParallelQueries = 99,
            InternalBatchSize = 5,
            PollingIntervalSeconds = 1,
            MaxRetries = -1
        };

        var normalized = LiteConfigService.Normalize(config);

        Assert.Equal(10, normalized.ParallelQueries);
        Assert.Equal(50, normalized.InternalBatchSize);
        Assert.Equal(10, normalized.PollingIntervalSeconds);
        Assert.Equal(0, normalized.MaxRetries);
    }

    [Fact]
    public void Normalize_SinEntornos_RestauraPro()
    {
        var config = new LiteConfig();
        config.Environments.Clear();

        var normalized = LiteConfigService.Normalize(config);

        Assert.Contains(normalized.Environments, e => e.Name == "PRO");
        Assert.Equal("PRO", normalized.SelectedEnvironment);
    }

    [Fact]
    public void Normalize_SeleccionInexistenteConEntornos_CaeAlPrimero()
    {
        var config = new LiteConfig
        {
            SelectedEnvironment = "NO_EXISTE"
        };
        config.Environments.Clear();
        config.Environments.Add(new DocumentIA.Batch.Models.EnvironmentConfig
        {
            Name = "DEV",
            BackendUrl = "https://dev.example.com",
            FunctionKey = "k"
        });

        var normalized = LiteConfigService.Normalize(config);

        Assert.Equal("DEV", normalized.SelectedEnvironment);
        Assert.Single(normalized.Environments);
    }

    [Fact]
    public void Save_EscribeLaKeyCifradaEnDisco()
    {
        var service = new LiteConfigService(ConfigPath);
        var config = service.Load();
        config.Environments[0].FunctionKey = "SECRETO";

        service.Save(config);
        var raw = File.ReadAllText(ConfigPath);

        Assert.Contains("enc:", raw);
        Assert.DoesNotContain("SECRETO", raw);
    }

    [Fact]
    public void Load_DevuelveLaKeyEnClaro()
    {
        var service = new LiteConfigService(ConfigPath);
        var config = service.Load();
        config.Environments[0].FunctionKey = "SECRETO";
        service.Save(config);

        var reloaded = new LiteConfigService(ConfigPath).Load();

        Assert.Equal("SECRETO", reloaded.Environments[0].FunctionKey);
    }

    [Fact]
    public void Save_NoMutaLaConfigEnMemoria()
    {
        var service = new LiteConfigService(ConfigPath);
        var config = service.Load();
        config.Environments[0].FunctionKey = "SECRETO";

        service.Save(config);

        Assert.Equal("SECRETO", config.Environments[0].FunctionKey);
    }

    [Fact]
    public void Load_DeConfigConKeyEnClaro_LaLeeYAlGuardarLaCifra()
    {
        File.WriteAllText(ConfigPath, """
        {
          "SelectedEnvironment": "PRO",
          "Environments": [
            { "Name": "PRO", "BackendUrl": "https://srbappprodocai.azurewebsites.net", "FunctionKey": "SECRETO" }
          ]
        }
        """);
        var service = new LiteConfigService(ConfigPath);

        var config = service.Load();
        Assert.Equal("SECRETO", config.Environments[0].FunctionKey);

        service.Save(config);
        var raw = File.ReadAllText(ConfigPath);

        Assert.Contains("enc:", raw);
        Assert.DoesNotContain("SECRETO", raw);
    }

    [Fact]
    public void SerializeRedacted_NoIncluyeLaKey()
    {
        var config = new LiteConfig();
        config.Environments[0].FunctionKey = "SECRETO";

        var json = LiteConfigService.SerializeRedacted(config);

        Assert.DoesNotContain("SECRETO", json);
        Assert.Equal("SECRETO", config.Environments[0].FunctionKey);
    }
}
