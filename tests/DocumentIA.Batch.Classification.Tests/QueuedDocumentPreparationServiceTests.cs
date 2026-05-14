using DocumentIA.Batch.Classification.Services;
using DocumentIA.Batch.Markdown;
using DocumentIA.Batch.Services;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.IO;
using Xunit;

namespace DocumentIA.Batch.Classification.Tests;

public class QueuedDocumentPreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_WhenMarkdownEnabled_AppliesPageLimitBeforeMarkdownGeneration()
    {
        var filePath = CreatePdfFile(6);
        var generator = new RecordingMarkdownGenerator();
        var service = new QueuedDocumentPreparationService(new PdfPageLimiterService(), generator);

        try
        {
            var result = await service.PrepareAsync(
                filePath,
                classificationOnly: true,
                maxPagesForClassificationOnly: 3,
                generateMarkdown: true,
                CancellationToken.None);

            Assert.Equal(1, generator.CallCount);
            Assert.Equal(3, CountPages(generator.LastInputBytes));
            Assert.Equal(3, CountPages(result.DocumentBytes));
            Assert.Equal("generated-markdown", result.Markdown);
            Assert.Empty(result.TemporaryArtifacts);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task PrepareAsync_WhenMarkdownDisabled_DoesNotInvokeGenerator()
    {
        var filePath = CreatePdfFile(4);
        var generator = new RecordingMarkdownGenerator();
        var service = new QueuedDocumentPreparationService(new PdfPageLimiterService(), generator);

        try
        {
            var result = await service.PrepareAsync(
                filePath,
                classificationOnly: true,
                maxPagesForClassificationOnly: 2,
                generateMarkdown: false,
                CancellationToken.None);

            Assert.Equal(0, generator.CallCount);
            Assert.Equal(2, CountPages(result.DocumentBytes));
            Assert.Null(result.Markdown);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Cleanup_WhenFilesDoNotExist_DoesNotThrow()
    {
        var cleaner = new TemporaryArtifactsCleaner();
        cleaner.Cleanup(new[]
        {
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.tmp")
        });
    }

    private static string CreatePdfFile(int pages)
    {
        var path = Path.Combine(Path.GetTempPath(), $"batch-classification-{Guid.NewGuid():N}.pdf");
        using var document = new PdfDocument();

        for (var i = 0; i < pages; i++)
        {
            document.AddPage();
        }

        document.Save(path);
        return path;
    }

    private static int CountPages(byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }

    private sealed class RecordingMarkdownGenerator : IPdfMarkdownGenerator
    {
        public int CallCount { get; private set; }
        public byte[] LastInputBytes { get; private set; } = Array.Empty<byte>();

        public Task<PdfMarkdownResult> GenerateAsync(byte[] pdfBytes, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastInputBytes = pdfBytes;

            return Task.FromResult(new PdfMarkdownResult
            {
                Markdown = "generated-markdown",
                Pages = CountPages(pdfBytes),
                Characters = "generated-markdown".Length
            });
        }
    }
}
