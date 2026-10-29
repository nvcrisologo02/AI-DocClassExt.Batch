using DocumentIA.Batch.IvaCedente.Models;
using DocumentIA.Batch.IvaCedente.Services;
using Xunit;
namespace DocumentIA.Batch.IvaCedente.Tests;

// Las pruebas tocan una variable de entorno de proceso: se serializan entre sí.
[Collection("VariableEntorno")]
public class IvaConfigLoaderTests
{
    [Fact]
    public void Cargar_SampleDelProyecto_TieneLosValoresPorDefecto()
    {
        var config = IvaConfigLoader.Cargar(Path.Combine(AppContext.BaseDirectory, "config.sample.json"));
        Assert.Equal("PRO", config.Entorno);
        Assert.Equal(3, config.Colas);
        Assert.Equal(new[] { "TASA-11", "SCXX-00" }, config.CodigosExcluidos);
        Assert.Equal(new[] { "SCXX-00" }, config.CodigosPreclasificar);
        Assert.True(config.ForzarSinLimitePaginas);
        Assert.Null(config.TipologiaForzada);
    }

    [Fact]
    public void Cargar_FicheroInexistente_Lanza()
    {
        Assert.Throws<IvaCedenteUsageException>(() => IvaConfigLoader.Cargar(Path.Combine(AppContext.BaseDirectory, "no-existe.json")));
    }

    [Fact]
    public void EntornoSeleccionado_SinKeyEnFicheroPeroConVariable_UsaLaVariable()
    {
        var config = new IvaConfig { Entorno = "PRO" };
        Environment.SetEnvironmentVariable(IvaConfigLoader.VariableFunctionKey, "clave-prueba");
        try { Assert.Equal("clave-prueba", IvaConfigLoader.EntornoSeleccionado(config).FunctionKey); }
        finally { Environment.SetEnvironmentVariable(IvaConfigLoader.VariableFunctionKey, null); }
    }

    [Fact]
    public void EntornoSeleccionado_SinKeyNiVariable_Lanza()
    {
        Environment.SetEnvironmentVariable(IvaConfigLoader.VariableFunctionKey, null);
        Assert.Throws<IvaCedenteUsageException>(() => IvaConfigLoader.EntornoSeleccionado(new IvaConfig()));
    }

    [Fact]
    public void EntornoSeleccionado_EntornoInexistente_Lanza()
    {
        Environment.SetEnvironmentVariable(IvaConfigLoader.VariableFunctionKey, "x");
        try { Assert.Throws<IvaCedenteUsageException>(() => IvaConfigLoader.EntornoSeleccionado(new IvaConfig { Entorno = "XXX" })); }
        finally { Environment.SetEnvironmentVariable(IvaConfigLoader.VariableFunctionKey, null); }
    }

    [Fact]
    public void CargarPrompts_FicherosDelProyecto_UserContieneMarcadorContenido()
    {
        var prompts = IvaConfigLoader.CargarPrompts(new IvaConfig(), AppContext.BaseDirectory);
        Assert.Contains("{contenido}", prompts.User);
        Assert.Contains("JSON", prompts.System);
    }

    [Fact]
    public void CargarPrompts_FicheroInexistente_Lanza()
    {
        var config = new IvaConfig { PromptSystemPath = "prompts/no-existe.md" };
        Assert.Throws<IvaCedenteUsageException>(() => IvaConfigLoader.CargarPrompts(config, AppContext.BaseDirectory));
    }
}
