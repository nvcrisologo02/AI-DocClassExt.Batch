namespace DocumentIA.Batch.ClassificationLite.Models;

public class LiteDocument
{
    public long Id { get; set; }
    public string ExecutionId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    /// <summary>Fecha de última modificación del fichero, ISO 8601 UTC ("O"). Parte de la clave de dedup.</summary>
    public string LastModifiedUtc { get; set; } = string.Empty;
    public string Status { get; set; } = LiteDocumentStatus.Pending;
    public int BatchNumber { get; set; }
    public int RetryCount { get; set; }
    public string? InstanceId { get; set; }
    public string? StatusQueryUri { get; set; }
    public string? Tdn1 { get; set; }
    public string? Tdn2 { get; set; }
    public double? Confidence { get; set; }
    public int? Pages { get; set; }
    public string? PagesIncluded { get; set; }
    public string? ProcessDate { get; set; }
    public long? DurationMs { get; set; }
    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Summary { get; set; }
}

public static class LiteDocumentStatus
{
    public const string Pending = "Pending";
    public const string InFlight = "InFlight";
    public const string Succeeded = "Succeeded";
    public const string Error = "Error";
    public const string DefinitiveError = "DefinitiveError";
    public const string SkippedHistory = "SkippedHistory";
    public const string Cancelled = "Cancelled";
}
