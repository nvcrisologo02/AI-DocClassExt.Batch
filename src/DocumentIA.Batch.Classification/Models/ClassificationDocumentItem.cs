using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace DocumentIA.Batch.Classification.Models;

public class ClassificationDocumentItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string _status = "Pendiente";
    private string _identificacionDocumento = string.Empty;
    private string _tipologiaIdentificada = string.Empty;
    private string _confianzaGlobal = string.Empty;
    private string _mensajeError = string.Empty;

    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string RuntimeStatus { get; set; } = string.Empty;
    public string OutputJsonPath { get; set; } = string.Empty;

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string IdentificacionDocumento
    {
        get => _identificacionDocumento;
        set => SetField(ref _identificacionDocumento, value);
    }

    public string TipologiaIdentificada
    {
        get => _tipologiaIdentificada;
        set => SetField(ref _tipologiaIdentificada, value);
    }

    public string ConfianzaGlobal
    {
        get => _confianzaGlobal;
        set
        {
            if (SetField(ref _confianzaGlobal, value))
            {
                OnPropertyChanged(nameof(ConfidenceDisplay));
            }
        }
    }

    public string MensajeError
    {
        get => _mensajeError;
        set => SetField(ref _mensajeError, value);
    }

    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }

    public string DurationDisplay => (FechaInicio.HasValue && FechaFin.HasValue)
        ? (FechaFin.Value - FechaInicio.Value).ToString(@"mm\:ss")
        : string.Empty;

    public string ConfidenceDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ConfianzaGlobal))
            {
                return string.Empty;
            }

            if (!double.TryParse(ConfianzaGlobal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && !double.TryParse(ConfianzaGlobal, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return ConfianzaGlobal;
            }

            if (value > 1d && value <= 100d)
            {
                value /= 100d;
            }

            return value.ToString("P1", CultureInfo.CurrentCulture);
        }
    }

    private bool SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}