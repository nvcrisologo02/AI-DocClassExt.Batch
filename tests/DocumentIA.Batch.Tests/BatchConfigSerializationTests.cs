using System.Text.Json;
using DocumentIA.Batch.Models;
using Xunit;

namespace DocumentIA.Batch.Tests;

public class BatchConfigSerializationTests
{
    [Fact]
    public void BatchConfig_ConEntornos_RoundTripPreservaTodo()
    {
        var original = new BatchConfig
        {
            Environments = new List<EnvironmentConfig>
            {
                new() { Name = "Dev", BackendUrl = "https://dev.example", FunctionKey = "key-dev" },
                new() { Name = "Pre", BackendUrl = "https://pre.example", FunctionKey = "key-pre" },
                new() { Name = "Producción", BackendUrl = "https://pro.example", FunctionKey = "key-pro" }
            },
            SelectedEnvironment = "Pre"
        };

        var json = JsonSerializer.Serialize(original, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<BatchConfig>(json)!;

        Assert.Equal(3, restored.Environments.Count);
        Assert.Equal("Pre", restored.SelectedEnvironment);
        Assert.Equal("https://pre.example", restored.Environments[1].BackendUrl);
        Assert.Equal("key-pro", restored.Environments[2].FunctionKey);
    }
}
