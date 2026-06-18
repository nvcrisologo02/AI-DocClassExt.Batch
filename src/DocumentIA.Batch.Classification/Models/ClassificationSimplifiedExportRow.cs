namespace DocumentIA.Batch.Classification.Models;

public sealed record ClassificationSimplifiedExportRow(
    string Filename,
    string Status,
    string Rsultado,
    string Tipologia,
    string PaginasIncluidas,
    string Paginas,
    string Tdn1,
    string Tdn2,
    string Matricula,
    string Clasificador,
    string Confidence,
    string DuracionTotalMs,
    string Resumen,
    string TiplogiaVirtual,
    string EsTipologiaVirtual)
{
    public string[] ToValues() =>
    [
        Filename,
        Status,
        Rsultado,
        Tipologia,
        PaginasIncluidas,
        Paginas,
        Tdn1,
        Tdn2,
        Matricula,
        Clasificador,
        Confidence,
        DuracionTotalMs,
        Resumen,
        TiplogiaVirtual,
        EsTipologiaVirtual
    ];
}
