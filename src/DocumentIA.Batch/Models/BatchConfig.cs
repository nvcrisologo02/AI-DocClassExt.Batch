namespace DocumentIA.Batch.Models;

public class BatchConfig
{
    public string BackendUrl { get; set; } = "http://localhost:7071";
    public string FunctionKey { get; set; } = string.Empty;
    public string SelectedTipologia { get; set; } = "nota.simple.1_4";
    public bool PromptingEnabled { get; set; } = true;
    public int UmbralConfianza { get; set; } = 85;
    public int NumeroColas { get; set; } = 4;
    public bool EjecutarConAssetResolver { get; set; } = true;
    public bool SubirAGdc { get; set; } = true;
    public Dictionary<string, PromptOverride> PromptOverrides { get; set; } = new();
}

public class PromptOverride
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPromptTemplate { get; set; } = string.Empty;
}
