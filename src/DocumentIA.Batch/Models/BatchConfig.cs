namespace DocumentIA.Batch.Models;

public class BatchConfig
{
    // Legacy: BackendUrl/FunctionKey se conservan solo para migrar config.json
    // anteriores (ver SettingsService.Normalize). La fuente de verdad es Environments.
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
    public bool ClassificationOnly { get; set; } = false;
    public bool EjecutarIntegridad { get; set; } = false;
    public bool ForzarResumenPorDefecto { get; set; } = true;
    public string ClassificationProvider { get; set; } = "auto";
    public string ClassificationModel { get; set; } = "auto";
    public string ClassificationLevel { get; set; } = string.Empty;
    public int MaxPagesForClassificationOnly { get; set; } = 10;
    /// <summary>Campos a solicitar al AssetResolver (separados por coma). Vacío = todos los disponibles.</summary>
    public string AssetResolverCamposSolicitados { get; set; } = string.Empty;
    public Dictionary<string, PromptOverride> PromptOverrides { get; set; } = new();

    /// <summary>Catálogo de entornos configurables (dev/pre/pro/…). Fuente de verdad del backend a usar.</summary>
    public List<EnvironmentConfig> Environments { get; set; } = new();

    /// <summary>Nombre del entorno activo dentro de Environments.</summary>
    public string SelectedEnvironment { get; set; } = string.Empty;
}

public class PromptOverride
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPromptTemplate { get; set; } = string.Empty;
}

public class EnvironmentConfig
{
    public string Name { get; set; } = string.Empty;
    public string BackendUrl { get; set; } = string.Empty;
    public string FunctionKey { get; set; } = string.Empty;
}
