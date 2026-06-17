using DocumentIA.Batch.Markdown;
using DocumentIA.Batch.Services;
using System.IO;

namespace DocumentIA.Batch.Classification.Services;

public sealed class QueuedDocumentPreparationService
{
    private readonly PdfPageLimiterService _pageLimiterService;
    private readonly IPdfMarkdownGenerator _markdownGenerator;

    public QueuedDocumentPreparationService(
        PdfPageLimiterService pageLimiterService,
        IPdfMarkdownGenerator markdownGenerator)
    {
        _pageLimiterService = pageLimiterService;
        _markdownGenerator = markdownGenerator;
    }

    public async Task<QueuedDocumentPreparationResult> PrepareAsync(
        string filePath,
        bool classificationOnly,
        int maxPagesForClassificationOnly,
        bool generateMarkdown,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        }

        var originalBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var normalizedMaxPages = Math.Max(0, maxPagesForClassificationOnly);

        var pageLimitResult = !classificationOnly || normalizedMaxPages <= 0
            ? new PdfPageLimitResult
            {
                Base64Bytes = originalBytes,
                OriginalPages = 0,
                UsedPages = 0,
                Applied = false
            }
            : _pageLimiterService.LimitForClassificationOnly(originalBytes, normalizedMaxPages);

        var payloadBytes = !classificationOnly || normalizedMaxPages <= 0
            ? originalBytes
            : pageLimitResult.Base64Bytes;

        string? markdown = null;
        if (generateMarkdown)
        {
            var markdownResult = await _markdownGenerator.GenerateAsync(payloadBytes, cancellationToken);
            markdown = markdownResult.Markdown;
        }

        return new QueuedDocumentPreparationResult
        {
            DocumentBytes = payloadBytes,
            Markdown = markdown,
            TemporaryArtifacts = Array.Empty<string>()
        };
    }
}

public sealed class QueuedDocumentPreparationResult
{
    public byte[] DocumentBytes { get; init; } = Array.Empty<byte>();
    public string? Markdown { get; init; }
    public IReadOnlyList<string> TemporaryArtifacts { get; init; } = Array.Empty<string>();
}

public sealed class TemporaryArtifactsCleaner
{
    public void Cleanup(IReadOnlyList<string> artifactPaths)
    {
        if (artifactPaths is null || artifactPaths.Count == 0)
        {
            return;
        }

        foreach (var path in artifactPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Avoid surfacing cleanup failures to the main processing flow.
            }
        }
    }
}
