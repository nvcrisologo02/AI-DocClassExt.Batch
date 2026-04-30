namespace DocumentIA.Batch.Models;

public class BatchRunSummary
{
    public string OperationName { get; set; } = string.Empty;
    public string ProcessStatus { get; set; } = string.Empty;
    public string Tipologia { get; set; } = string.Empty;
    public int NumeroColas { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public int TotalFiles { get; set; }
    public int FinishedFiles { get; set; }
    public int CompletedFiles { get; set; }
    public int RevisionFiles { get; set; }
    public int ErrorFiles { get; set; }
    public int CanceledFiles { get; set; }
    public int OutputJsonFiles { get; set; }
    public string SuccessRate { get; set; } = string.Empty;
    public string AverageConfidence { get; set; } = string.Empty;
    public string AverageDuration { get; set; } = string.Empty;
    public string TotalDuration { get; set; } = string.Empty;
    public List<BatchRunIssueSummary> Issues { get; set; } = new();
}

public class BatchRunIssueSummary
{
    public string FileName { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string EstadoCalidad { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string MensajeError { get; set; } = string.Empty;
}
