using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DocumentIA.Batch.Models;

/// <summary>
/// Fila plana para la vista de Historial: combina datos de BatchRunFile y BatchRun.
/// Permite mostrar directamente los ficheros procesados con contexto del run.
/// </summary>
public class HistorialFileRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool _isSelected;

    // ── Campos de BatchRunFile ────────────────────────────────────────────────
    public int Id { get; set; }
    public int RunId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? InstanceId { get; set; }
    public string? CorrelationId { get; set; }
    public string? RuntimeStatus { get; set; }
    public string? EstadoCalidad { get; set; }
    public double? ConfianzaGlobal { get; set; }
    public string? MensajeError { get; set; }
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public string? OutputJsonPath { get; set; }

    // ── Campos de BatchRun ────────────────────────────────────────────────────
    public string RunKey { get; set; } = string.Empty;
    public string Tipologia { get; set; } = string.Empty;
    public string RunCreatedAt { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    // ── Propiedades de visualización ──────────────────────────────────────────
    public string ConfidenceDisplay => ConfianzaGlobal.HasValue
        ? $"{ConfianzaGlobal.Value:P1}"
        : string.Empty;

    public string DurationDisplay => (FechaInicio.HasValue && FechaFin.HasValue)
        ? (FechaFin.Value - FechaInicio.Value).ToString(@"mm\:ss")
        : string.Empty;

    public string SizeDisplay => SizeBytes > 0
        ? $"{SizeBytes / 1024.0:F1} KB"
        : string.Empty;

    public string RunDateDisplay => DateTime.TryParse(RunCreatedAt, null,
        System.Globalization.DateTimeStyles.RoundtripKind, out var d)
        ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
        : RunCreatedAt;
}
