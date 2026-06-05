namespace DocumentIA.Batch.Classification.Models;

public sealed record ClassificationSimplifiedExportRow(
    string FileName,
    string Resumen,
    string Typology,
    string Confidence)
{
    public string[] ToValues() =>
    [
        FileName,
        Resumen,
        Typology,
        Confidence
    ];
}
