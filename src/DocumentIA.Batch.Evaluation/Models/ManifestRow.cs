namespace DocumentIA.Batch.Evaluation.Models;

/// <summary>Fila de eval/manifest.csv o eval/golden.csv (mismas columnas en ambos ficheros).</summary>
public sealed class ManifestRow
{
    public string Tdn1Folder { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string RelPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string MtimeUtc { get; init; } = string.Empty;
    public string ExpectedTdn1 { get; init; } = string.Empty;
    public string ExpectedTdn2 { get; init; } = string.Empty;
    public bool Validated { get; init; }
    public bool FolderIsTdn1 { get; init; }
    public bool InCata100 { get; init; }
    public bool LabelMatchesFolder { get; init; }
}
