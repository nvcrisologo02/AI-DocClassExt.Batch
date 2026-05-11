namespace DocumentIA.Batch.Classification.Models;

public sealed record ClassificationExportRow(
    string FileName,
    string IdentificacionDocumento,
    string TipologiaIdentificada,
    string ConfianzaGlobal);