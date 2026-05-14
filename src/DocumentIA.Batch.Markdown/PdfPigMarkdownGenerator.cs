using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocumentIA.Batch.Markdown;

public sealed class PdfPigMarkdownGenerator : IPdfMarkdownGenerator
{
    public Task<PdfMarkdownResult> GenerateAsync(byte[] pdfBytes, CancellationToken cancellationToken = default)
    {
        if (pdfBytes is null || pdfBytes.Length == 0)
        {
            throw new ArgumentException("PDF input cannot be null or empty.", nameof(pdfBytes));
        }

        using var stream = new MemoryStream(pdfBytes, writable: false);
        using var document = PdfDocument.Open(stream);

        var builder = new StringBuilder();

        for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = document.GetPage(pageNumber);
            var pageText = Normalize(ContentOrderTextExtractor.GetText(page));

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.AppendLine($"## Page {pageNumber}");
            builder.AppendLine();

            if (string.IsNullOrWhiteSpace(pageText))
            {
                builder.AppendLine("_No extractable text on this page._");
            }
            else
            {
                builder.Append(pageText);
            }
        }

        var markdown = builder.ToString();
        return Task.FromResult(new PdfMarkdownResult
        {
            Markdown = markdown,
            Pages = document.NumberOfPages,
            Characters = markdown.Length
        });
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
