namespace DocumentIA.Batch.ClassificationLite.Models;

public class LiteExecution
{
    public string ExecutionId { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public bool IncludeSubfolders { get; set; }
    public string Status { get; set; } = LiteExecutionStatus.Running;
    public string StartedAt { get; set; } = string.Empty;
    public string? CompletedAt { get; set; }
    public string ConfigSnapshotJson { get; set; } = string.Empty;
}

public static class LiteExecutionStatus
{
    /// <summary>
    /// Ejecución escaneada como previsualización pero aún no procesada. No la recupera
    /// <see cref="Data.LiteRepository.GetIncompleteExecution"/> (solo Running/Paused), así que
    /// arrastrar ficheros y cerrar la app no dispara el aviso de "ejecución incompleta".
    /// </summary>
    public const string Scanned = "Scanned";
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string Aborted = "Aborted";
}
