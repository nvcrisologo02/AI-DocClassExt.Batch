using DocumentIA.Batch.Models;
using DocumentIA.Batch.Services;
using Xunit;

namespace DocumentIA.Batch.Tests;

public class SettingsServiceNormalizeTests
{
    private const string ProdUrl = "https://srbappprodocai.azurewebsites.net";

    [Fact]
    public void Normalize_ConfigLegacyConUrlYKey_MigraAEntornoProduccion()
    {
        var config = new BatchConfig
        {
            BackendUrl = "https://mi-backend-dev.azurewebsites.net",
            FunctionKey = "clave-legacy"
        };

        var result = SettingsService.Normalize(config);

        var env = Assert.Single(result.Environments);
        Assert.Equal("Producción", env.Name);
        Assert.Equal("https://mi-backend-dev.azurewebsites.net", env.BackendUrl);
        Assert.Equal("clave-legacy", env.FunctionKey);
        Assert.Equal("Producción", result.SelectedEnvironment);
    }

    [Fact]
    public void Normalize_ConfigYaMigrado_NoSeToca()
    {
        var config = new BatchConfig
        {
            Environments = new List<EnvironmentConfig>
            {
                new() { Name = "Dev", BackendUrl = "https://dev", FunctionKey = "k1" },
                new() { Name = "Pro", BackendUrl = "https://pro", FunctionKey = "k2" }
            },
            SelectedEnvironment = "Pro"
        };

        var result = SettingsService.Normalize(config);

        Assert.Equal(2, result.Environments.Count);
        Assert.Equal("Dev", result.Environments[0].Name);
        Assert.Equal("Pro", result.Environments[1].Name);
        Assert.Equal("Pro", result.SelectedEnvironment);
    }

    [Fact]
    public void Normalize_ConfigVacio_SiembraProduccionConUrlProdYKeyVacia()
    {
        var config = new BatchConfig { BackendUrl = string.Empty, FunctionKey = string.Empty };

        var result = SettingsService.Normalize(config);

        var env = Assert.Single(result.Environments);
        Assert.Equal("Producción", env.Name);
        Assert.Equal(ProdUrl, env.BackendUrl);
        Assert.Equal(string.Empty, env.FunctionKey);
        Assert.Equal("Producción", result.SelectedEnvironment);
    }

    [Fact]
    public void Normalize_LegacyLocalhost_SeSustituyePorUrlDeProd()
    {
        var config = new BatchConfig { BackendUrl = "http://localhost:7071", FunctionKey = "k" };

        var result = SettingsService.Normalize(config);

        var env = Assert.Single(result.Environments);
        Assert.Equal(ProdUrl, env.BackendUrl);
    }

    [Fact]
    public void Normalize_SelectedEnvironmentInexistente_SeleccionaElPrimero()
    {
        var config = new BatchConfig
        {
            Environments = new List<EnvironmentConfig>
            {
                new() { Name = "Dev", BackendUrl = "https://dev", FunctionKey = "" }
            },
            SelectedEnvironment = "NoExiste"
        };

        var result = SettingsService.Normalize(config);

        Assert.Equal("Dev", result.SelectedEnvironment);
    }
}
