namespace DocumentIA.Batch.Models;

/// <summary>
/// Record de una ejecución batch completa en SQLite.
/// Cabecera del run con metadatos y estadísticas agregadas.
/// </summary>
public class BatchRunRecord
{
    public int Id { get; set; }
    public string RunKey { get; set; } = string.Empty;  // yyyyMMdd-HHmmss
    public string RunFolderPath { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public string Tipologia { get; set; } = string.Empty;
    public int NumeroColas { get; set; }
    public int UmbralConfianza { get; set; }
    public bool SubirAGdc { get; set; }
    public bool EjecutarConAssetResolver { get; set; }

    // Estadísticas
    public int TotalFiles { get; set; }
    public int CompletedFiles { get; set; }
    public int RevisionFiles { get; set; }
    public int ErrorFiles { get; set; }
    public int CanceledFiles { get; set; }
    public string? SuccessRate { get; set; }           // Ej: "87,5%"
    public string? AverageConfidence { get; set; }     // Ej: "91,2%"
    public string? AverageDuration { get; set; }       // Ej: "02:14"
    public string? TotalDuration { get; set; }         // Ej: "08:47"
    public string? ProcessStatus { get; set; }

    public DateTime CreatedAt { get; set; }

    // Propiedades de visualización
    public string CreatedAtDisplay => CreatedAt.ToString("dd/MM/yyyy HH:mm:ss");
    public string SummaryDisplay =>
        $"{CompletedFiles}/{TotalFiles} completados · Rev: {RevisionFiles} · Err: {ErrorFiles}";
}
