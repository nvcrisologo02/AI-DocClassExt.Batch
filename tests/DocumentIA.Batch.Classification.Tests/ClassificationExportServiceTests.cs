using System.IO;
using System.IO.Compression;
using System.Text;
using DocumentIA.Batch.Classification.Models;
using DocumentIA.Batch.Classification.Services;
using Xunit;

namespace DocumentIA.Batch.Classification.Tests;

public class ClassificationExportServiceTests
{
    [Fact]
    public void ExportCsv_WritesOnlyRequestedColumns()
    {
        var service = new ClassificationExportService();
        var path = Path.Combine(Path.GetTempPath(), $"classification-{Guid.NewGuid():N}.csv");

        try
        {
            service.ExportCsv(path, new[]
            {
                new ClassificationDocumentItem
                {
                    FileName = "doc1.pdf",
                    IdentificacionDocumento = "ID-001",
                    TipologiaIdentificada = "nota.simple.1_4",
                    ConfianzaGlobal = "0.97"
                }
            });

            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 3);
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);

            var text = File.ReadAllText(path, Encoding.UTF8);
            Assert.Contains("FileName;Identificacion_TipoDocumento;Identificacion_TipologiaDetectada;Resultado_ConfianzaGlobal", text);
            Assert.Contains("doc1.pdf;ID-001;nota.simple.1_4;0.97", text);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ExportExcel_WritesValidZipWorkbook()
    {
        var service = new ClassificationExportService();
        var path = Path.Combine(Path.GetTempPath(), $"classification-{Guid.NewGuid():N}.xlsx");

        try
        {
            service.ExportExcel(path, new[]
            {
                new ClassificationDocumentItem
                {
                    FileName = "doc1.pdf",
                    IdentificacionDocumento = "ID-001",
                    TipologiaIdentificada = "nota.simple.1_4",
                    ConfianzaGlobal = "0.97"
                }
            });

            using var archive = ZipFile.OpenRead(path);
            Assert.Contains(archive.Entries, entry => entry.FullName == "xl/workbook.xml");
            Assert.Contains(archive.Entries, entry => entry.FullName == "xl/worksheets/sheet1.xml");

            using var sheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            using var reader = new StreamReader(sheetStream, Encoding.UTF8);
            var worksheetXml = reader.ReadToEnd();

            Assert.Contains("FileName", worksheetXml);
            Assert.Contains("Resultado_ConfianzaGlobal", worksheetXml);
            Assert.Contains("doc1.pdf", worksheetXml);
            Assert.Contains("0.97", worksheetXml);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
