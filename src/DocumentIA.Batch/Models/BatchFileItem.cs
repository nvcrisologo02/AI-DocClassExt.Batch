namespace DocumentIA.Batch.Models;

public class BatchFileItem
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Estado { get; set; } = "Pendiente";
    public string InstanceId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string RuntimeStatus { get; set; } = string.Empty;
    public string ActividadActual { get; set; } = string.Empty;
    public string ProgresoActividades { get; set; } = string.Empty;
    public string DetalleActividad { get; set; } = string.Empty;
    public string EstadoCalidad { get; set; } = string.Empty;
    public double? ConfianzaGlobal { get; set; }
    public string MensajeError { get; set; } = string.Empty;
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public string OutputJsonPath { get; set; } = string.Empty;

    public string ConfidenceDisplay => ConfianzaGlobal.HasValue
        ? $"{ConfianzaGlobal.Value:P1}"
        : string.Empty;

    public string DurationDisplay
    {
        get
        {
            if (!FechaInicio.HasValue || !FechaFin.HasValue)
            {
                return string.Empty;
            }

            return (FechaFin.Value - FechaInicio.Value).ToString(@"mm\:ss");
        }
    }

    public string DisplaySize
    {
        get
        {
            if (SizeBytes < 1024)
            {
                return $"{SizeBytes} B";
            }

            if (SizeBytes < 1024 * 1024)
            {
                return $"{SizeBytes / 1024d:F1} KB";
            }

            return $"{SizeBytes / (1024d * 1024d):F1} MB";
        }
    }
}
