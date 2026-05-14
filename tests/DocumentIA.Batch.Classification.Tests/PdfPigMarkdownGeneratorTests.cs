using DocumentIA.Batch.Markdown;
using PdfSharp.Pdf;
using System.IO;
using Xunit;

namespace DocumentIA.Batch.Classification.Tests;

public class PdfPigMarkdownGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_WhenPdfHasPages_EmitsPageSections()
    {
        var generator = new PdfPigMarkdownGenerator();
        var pdfBytes = CreatePdfBytes(2);

        var result = await generator.GenerateAsync(pdfBytes, CancellationToken.None);

        Assert.Equal(2, result.Pages);
        Assert.Contains("## Page 1", result.Markdown);
        Assert.Contains("## Page 2", result.Markdown);
        Assert.True(result.Characters > 0);
    }

    private static byte[] CreatePdfBytes(int pages)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            document.AddPage();
        }

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }
}
