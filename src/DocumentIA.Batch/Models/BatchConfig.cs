namespace DocumentIA.Batch.Models;

public class BatchConfig
{
    public string BackendUrl { get; set; } = "https://srbappprodocai.azurewebsites.net";
    public string FunctionKey { get; set; } = string.Empty;
    public string SelectedTipologia { get; set; } = "nota.simple.1_4";
    public bool PromptingEnabled { get; set; } = true;
    public bool SobreescribirUmbrales { get; set; } = false;
    public string UmbralExtraccion { get; set; } = "0.80";
    public string UmbralExtraccionCompletitud { get; set; } = string.Empty;
    public string UmbralExtraccionConfianza { get; set; } = string.Empty;
    public int NumeroColas { get; set; } = 4;
    public bool EjecutarConAssetResolver { get; set; } = true;
    public bool SubirAGdc { get; set; } = true;
    public bool ForceReprocess { get; set; } = false;
    /// <summary>Campos a solicitar al AssetResolver (separados por coma). Vacío = todos los disponibles.</summary>
    public string AssetResolverCamposSolicitados { get; set; } = string.Empty;
    public Dictionary<string, PromptOverride> PromptOverrides { get; set; } = new();
}

public class PromptOverride
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPromptTemplate { get; set; } = string.Empty;
}
