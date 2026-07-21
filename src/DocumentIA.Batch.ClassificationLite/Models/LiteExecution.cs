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
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string Aborted = "Aborted";
}
