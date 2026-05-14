namespace DocumentIA.Batch.Markdown;

public interface IPdfMarkdownGenerator
{
    Task<PdfMarkdownResult> GenerateAsync(byte[] pdfBytes, CancellationToken cancellationToken = default);
}

public sealed class PdfMarkdownResult
{
    public string Markdown { get; init; } = string.Empty;
    public int Pages { get; init; }
    public int Characters { get; init; }
}
