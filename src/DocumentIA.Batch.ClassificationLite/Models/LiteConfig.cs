using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.ClassificationLite.Models;

public class LiteConfig
{
    public string SelectedEnvironment { get; set; } = "PRO";

    public List<EnvironmentConfig> Environments { get; set; } = new()
    {
        new EnvironmentConfig
        {
            Name = "PRO",
            BackendUrl = "https://srbappprodocai.azurewebsites.net",
            FunctionKey = string.Empty
        }
    };

    public int ParallelQueries { get; set; } = 2;
    public int InternalBatchSize { get; set; } = 1000;
    public int PollingIntervalSeconds { get; set; } = 60;
    public string ClassificationLevel { get; set; } = "TDN1_TDN2";
    public string Provider { get; set; } = "auto";
    public string Model { get; set; } = "auto";
    public bool OnlyClassification { get; set; } = true;
    public bool ForceReprocess { get; set; } = false;
    public int MaxRetries { get; set; } = 3;
    public bool SkipAlreadyProcessed { get; set; } = true;
}
