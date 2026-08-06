using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.ViewModels;

/// <summary>
/// Fila de la rejilla. Todos los setters comparan antes de notificar: el refresco periodico
/// vuelca la ejecucion completa sobre las filas una vez por segundo y casi ninguna ha cambiado,
/// asi que notificar a ciegas multiplicaba el trabajo del hilo de interfaz por el numero de
/// documentos hasta dejar la ventana sin responder.
/// </summary>
public class LiteDocumentRow : INotifyPropertyChanged
{
    private string _status = string.Empty;
    private string _pagesIncluded = string.Empty;
    private string _pages = string.Empty;
    private string _tdn1 = string.Empty;
    private string _tdn2 = string.Empty;
    private string _confidence = string.Empty;
    private string _processDate = string.Empty;
    private string _totalDurationMs = string.Empty;
    private string? _requestJson;
    private string? _responseJson;
    private string? _errorMessage;
    private string? _summary;
    private string? _estado;

    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;

    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    public string PagesIncluded
    {
        get => _pagesIncluded;
        set { if (_pagesIncluded != value) { _pagesIncluded = value; OnPropertyChanged(); } }
    }

    public string Pages
    {
        get => _pages;
        set { if (_pages != value) { _pages = value; OnPropertyChanged(); } }
    }

    public string Tdn1
    {
        get => _tdn1;
        set { if (_tdn1 != value) { _tdn1 = value; OnPropertyChanged(); } }
    }

    public string Tdn2
    {
        get => _tdn2;
        set { if (_tdn2 != value) { _tdn2 = value; OnPropertyChanged(); } }
    }

    public string Confidence
    {
        get => _confidence;
        set { if (_confidence != value) { _confidence = value; OnPropertyChanged(); } }
    }

    public string ProcessDate
    {
        get => _processDate;
        set { if (_processDate != value) { _processDate = value; OnPropertyChanged(); } }
    }

    public string TotalDurationMs
    {
        get => _totalDurationMs;
        set { if (_totalDurationMs != value) { _totalDurationMs = value; OnPropertyChanged(); } }
    }

    public string? RequestJson
    {
        get => _requestJson;
        set { if (_requestJson != value) { _requestJson = value; OnPropertyChanged(); } }
    }

    public string? ResponseJson
    {
        get => _responseJson;
        set { if (_responseJson != value) { _responseJson = value; OnPropertyChanged(); } }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set { if (_errorMessage != value) { _errorMessage = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Resumen del documento. No se rellena desde la lectura ligera de la rejilla
    /// (<see cref="UpdateFrom"/>): se carga bajo demanda al abrir el dialogo de detalle,
    /// igual que RequestJson/ResponseJson.
    /// </summary>
    public string? Summary
    {
        get => _summary;
        set { if (_summary != value) { _summary = value; OnPropertyChanged(); } }
    }

    public string? Estado
    {
        get => _estado;
        set
        {
            if (_estado == value)
            {
                return;
            }

            _estado = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ResultadoDisplay));
            OnPropertyChanged(nameof(IsLowConfidence));
        }
    }

    /// <summary>
    /// Texto amigable para la columna "Resultado" de la rejilla: vacio en el caso normal
    /// (Estado vacio u "OK", para no ensuciar la vista), "Baja confianza" para el rechazo
    /// por baja confianza, y el Estado tal cual para cualquier otro caso no contemplado.
    /// </summary>
    public string ResultadoDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Estado) || string.Equals(Estado, "OK", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (Estado.Contains("BAJA_CONFIANZA", StringComparison.OrdinalIgnoreCase))
            {
                return "Baja confianza";
            }

            return Estado;
        }
    }

    public bool IsLowConfidence => IsReviewableEstado(Estado);

    /// <summary>
    /// Condicion unica de "a revisar": Estado presente y distinto de "OK" (baja confianza u
    /// otro rechazo del backend). Compartida por la rejilla (via <see cref="IsLowConfidence"/>)
    /// y por la exportacion, para que nunca diverjan.
    /// </summary>
    public static bool IsReviewableEstado(string? estado)
        => !string.IsNullOrWhiteSpace(estado)
           && !estado.Equals("OK", StringComparison.OrdinalIgnoreCase);

    public static LiteDocumentRow From(LiteDocument document)
    {
        var row = new LiteDocumentRow
        {
            Id = document.Id,
            FileName = document.FileName
        };
        row.UpdateFrom(document);
        return row;
    }

    public void UpdateFrom(LiteDocument document)
    {
        Status = document.Status;
        PagesIncluded = document.PagesIncluded ?? string.Empty;
        Pages = document.Pages?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        Tdn1 = document.Tdn1 ?? string.Empty;
        Tdn2 = document.Tdn2 ?? string.Empty;
        Confidence = document.Confidence?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
        ProcessDate = document.ProcessDate ?? string.Empty;
        TotalDurationMs = document.DurationMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        RequestJson = document.RequestJson;
        ResponseJson = document.ResponseJson;
        ErrorMessage = document.ErrorMessage;
        Estado = document.Estado;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
