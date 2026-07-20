using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.ViewModels;

public class LiteDocumentRow : INotifyPropertyChanged
{
    private string _status = string.Empty;

    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public string PagesIncluded { get; set; } = string.Empty;
    public string Pages { get; set; } = string.Empty;
    public string Tdn1 { get; set; } = string.Empty;
    public string Tdn2 { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public string ProcessDate { get; set; } = string.Empty;
    public string TotalDurationMs { get; set; } = string.Empty;

    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }
    public string? ErrorMessage { get; set; }

    public static LiteDocumentRow From(LiteDocument document) => new()
    {
        Id = document.Id,
        FileName = document.FileName,
        Status = document.Status,
        PagesIncluded = document.PagesIncluded ?? string.Empty,
        Pages = document.Pages?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        Tdn1 = document.Tdn1 ?? string.Empty,
        Tdn2 = document.Tdn2 ?? string.Empty,
        Confidence = document.Confidence?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
        ProcessDate = document.ProcessDate ?? string.Empty,
        TotalDurationMs = document.DurationMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        RequestJson = document.RequestJson,
        ResponseJson = document.ResponseJson,
        ErrorMessage = document.ErrorMessage
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
