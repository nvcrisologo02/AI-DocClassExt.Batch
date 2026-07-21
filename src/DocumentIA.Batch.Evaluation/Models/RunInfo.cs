namespace DocumentIA.Batch.Evaluation.Models;

/// <summary>Metadatos de una ejecucion de 'run', persistidos en run-info.json junto a results.csv.</summary>
public sealed class RunInfo
{
    public string Env { get; set; } = string.Empty;
    public string Set { get; set; } = string.Empty;
    public string CorpusRoot { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string AppVersion { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public int Total { get; set; }
    public int Ok { get; set; }
    public int Error { get; set; }
    public int Timeout { get; set; }
    public int Parallel { get; set; }
}
