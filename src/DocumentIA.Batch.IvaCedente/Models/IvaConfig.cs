using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.IvaCedente.Models;

/// <summary>Configuración de la consola, leída de config.json (ver config.sample.json).</summary>
public sealed class IvaConfig
{
    public string Entorno { get; set; } = "PRO";

    public List<EnvironmentConfig> Environments { get; set; } = new()
    {
        new EnvironmentConfig { Name = "DEV", BackendUrl = "https://srbappdevdocai.azurewebsites.net", FunctionKey = string.Empty },
        new EnvironmentConfig { Name = "PRO", BackendUrl = "https://srbappprodocai.azurewebsites.net", FunctionKey = string.Empty }
    };

    public int Colas { get; set; } = 3;
    public List<string> CodigosExcluidos { get; set; } = new() { "TASA-11", "SCXX-00" };
    public List<string> CodigosPreclasificar { get; set; } = new() { "SCXX-00" };
    public bool ForzarSinLimitePaginas { get; set; } = true;
    public string? ModelKey { get; set; }

    /// <summary>Código de tipología enviado como ExpectedType en la llamada con prompt; null para clasificar.</summary>
    public string? TipologiaForzada { get; set; }
    public string PromptSystemPath { get; set; } = "prompts/system.md";
    public string PromptUserPath { get; set; } = "prompts/user.md";
    public int PollingIntervalSeconds { get; set; } = 30;
    public int TimeoutMinutos { get; set; } = 30;
    public int MaxReintentos { get; set; } = 2;
    public int MaxPaginasClasificacion { get; set; } = 10;
}
