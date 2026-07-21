using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.ViewModels;

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

    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public string PagesIncluded
    {
        get => _pagesIncluded;
        set { _pagesIncluded = value; OnPropertyChanged(); }
    }

    public string Pages
    {
        get => _pages;
        set { _pages = value; OnPropertyChanged(); }
    }

    public string Tdn1
    {
        get => _tdn1;
        set { _tdn1 = value; OnPropertyChanged(); }
    }

    public string Tdn2
    {
        get => _tdn2;
        set { _tdn2 = value; OnPropertyChanged(); }
    }

    public string Confidence
    {
        get => _confidence;
        set { _confidence = value; OnPropertyChanged(); }
    }

    public string ProcessDate
    {
        get => _processDate;
        set { _processDate = value; OnPropertyChanged(); }
    }

    public string TotalDurationMs
    {
        get => _totalDurationMs;
        set { _totalDurationMs = value; OnPropertyChanged(); }
    }

    public string? RequestJson
    {
        get => _requestJson;
        set { _requestJson = value; OnPropertyChanged(); }
    }

    public string? ResponseJson
    {
        get => _responseJson;
        set { _responseJson = value; OnPropertyChanged(); }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Resumen del documento. No se rellena desde la lectura ligera de la rejilla
    /// (<see cref="UpdateFrom"/>): se carga bajo demanda al abrir el dialogo de detalle,
    /// igual que RequestJson/ResponseJson.
    /// </summary>
    public string? Summary
    {
        get => _summary;
        set { _summary = value; OnPropertyChanged(); }
    }

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
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
