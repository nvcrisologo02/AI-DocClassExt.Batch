using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.IO;

namespace DocumentIA.Batch.Services;

public class PdfPageLimiterService
{
    public PdfPageLimitResult LimitForClassificationOnly(byte[] pdfBytes, int maxPages)
    {
        if (pdfBytes is null || pdfBytes.Length == 0)
        {
            throw new ArgumentException("PDF input cannot be null or empty.", nameof(pdfBytes));
        }

        using var inputStream = new MemoryStream(pdfBytes, writable: false);
        using var source = PdfReader.Open(inputStream, PdfDocumentOpenMode.Import);
        var originalPages = source.PageCount;

        if (maxPages <= 0 || originalPages <= maxPages)
        {
            return new PdfPageLimitResult
            {
                Base64Bytes = pdfBytes,
                OriginalPages = originalPages,
                UsedPages = originalPages,
                Applied = false
            };
        }

        using var output = new PdfDocument();
        for (var i = 0; i < maxPages; i++)
        {
            output.AddPage(source.Pages[i]);
        }

        using var outputStream = new MemoryStream();
        output.Save(outputStream, false);

        return new PdfPageLimitResult
        {
            Base64Bytes = outputStream.ToArray(),
            OriginalPages = originalPages,
            UsedPages = maxPages,
            Applied = true
        };
    }
}

public class PdfPageLimitResult
{
    public byte[] Base64Bytes { get; set; } = Array.Empty<byte>();
    public int OriginalPages { get; set; }
    public int UsedPages { get; set; }
    public bool Applied { get; set; }
}
