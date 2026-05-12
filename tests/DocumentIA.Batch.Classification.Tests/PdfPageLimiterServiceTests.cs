using System.IO;
using DocumentIA.Batch.Services;
using PdfSharp.Pdf;
using Xunit;

namespace DocumentIA.Batch.Classification.Tests;

public class PdfPageLimiterServiceTests
{
    [Fact]
    public void LimitForClassificationOnly_WhenMaxPagesIsZero_ReturnsOriginalDocument()
    {
        var service = new PdfPageLimiterService();
        var original = CreatePdfBytes(4);

        var result = service.LimitForClassificationOnly(original, 0);

        Assert.False(result.Applied);
        Assert.Equal(4, result.OriginalPages);
        Assert.Equal(4, result.UsedPages);
        Assert.Equal(original.Length, result.Base64Bytes.Length);
    }

    [Fact]
    public void LimitForClassificationOnly_WhenMaxPagesLowerThanOriginal_TruncatesDocument()
    {
        var service = new PdfPageLimiterService();
        var original = CreatePdfBytes(10);

        var result = service.LimitForClassificationOnly(original, 5);

        Assert.True(result.Applied);
        Assert.Equal(10, result.OriginalPages);
        Assert.Equal(5, result.UsedPages);

        using var stream = new MemoryStream(result.Base64Bytes);
        using var doc = PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(5, doc.PageCount);
    }

    [Fact]
    public void LimitForClassificationOnly_WhenMaxPagesHigherThanOriginal_ReturnsOriginalDocument()
    {
        var service = new PdfPageLimiterService();
        var original = CreatePdfBytes(3);

        var result = service.LimitForClassificationOnly(original, 7);

        Assert.False(result.Applied);
        Assert.Equal(3, result.OriginalPages);
        Assert.Equal(3, result.UsedPages);
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
