namespace DocumentIA.Batch.Models;

/// <summary>
/// Record de un archivo procesado en una ejecución batch.
/// Detalle por documento dentro de un BatchRun.
/// </summary>
public class BatchRunFileRecord
{
    public int Id { get; set; }
    public int RunId { get; set; }                     // FK a BatchRunRecord
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    
    public string Estado { get; set; } = string.Empty;  // Completado, Revision, Error, Cancelado
    public string? InstanceId { get; set; }
    public string? CorrelationId { get; set; }
    public string? RuntimeStatus { get; set; }          // Completed, Failed, Terminated
    public string? EstadoCalidad { get; set; }
    public double? ConfianzaGlobal { get; set; }
    public string? MensajeError { get; set; }
    
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public string? OutputJsonPath { get; set; }

    // Propiedades de visualización formateadas
    public string ConfidenceDisplay => ConfianzaGlobal.HasValue 
        ? $"{ConfianzaGlobal.Value:P1}" 
        : string.Empty;

    public string DurationDisplay => (FechaInicio.HasValue && FechaFin.HasValue)
        ? (FechaFin.Value - FechaInicio.Value).ToString(@"mm\:ss")
        : string.Empty;

    public string SizeDisplay => SizeBytes > 0
        ? $"{(SizeBytes / 1024.0):F1} KB"
        : "0 KB";
}
