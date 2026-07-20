using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteExportServiceTests : IDisposable
{
    private readonly string _tempDir;

    public LiteExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-export-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static LiteDocument SampleDoc() => new()
    {
        FileName = "informe.pdf",
        Status = LiteDocumentStatus.Succeeded,
        PagesIncluded = "1-5",
        Pages = 8,
        Tdn1 = "T01",
        Tdn2 = "T01.02",
        Confidence = 0.9123,
        ProcessDate = "2026-07-20T12:00:00Z",
        DurationMs = 4321
    };

    [Fact]
    public void ToRow_MapeaLasNueveColumnas()
    {
        var row = LiteExportService.ToRow(SampleDoc());

        Assert.Equal(9, row.Length);
        Assert.Equal(new[] { "informe.pdf", "Succeeded", "1-5", "8", "T01", "T01.02", "0,9123".Replace(',', '.'), "2026-07-20T12:00:00Z", "4321" }, row);
    }

    [Fact]
    public void ToRow_ConNulos_DevuelveCadenasVacias()
    {
        var row = LiteExportService.ToRow(new LiteDocument { FileName = "x.pdf", Status = LiteDocumentStatus.Pending });

        Assert.Equal("x.pdf", row[0]);
        Assert.Equal("Pending", row[1]);
        Assert.Equal(string.Empty, row[2]);
        Assert.Equal(string.Empty, row[3]);
        Assert.Equal(string.Empty, row[6]);
        Assert.Equal(string.Empty, row[8]);
    }

    [Fact]
    public void ExportCsv_EscribeCabeceraYFilas_ConBom()
    {
        var path = Path.Combine(_tempDir, "salida.csv");

        LiteExportService.ExportCsv(new[] { SampleDoc() }, path);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.Equal("FileName;Status;PagesIncluded;Pages;TDN1;TDN2;Confidence;ProcessDate;TotalDurationMs", lines[0]);
        Assert.Contains("informe.pdf;Succeeded;1-5;8;T01;T01.02;0.9123;", lines[1]);
    }

    [Fact]
    public void ExportCsv_EntrecomillaValoresConSeparador()
    {
        var path = Path.Combine(_tempDir, "comillas.csv");
        var doc = SampleDoc();
        doc.FileName = "raro;con \"comillas\".pdf";

        LiteExportService.ExportCsv(new[] { doc }, path);

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.StartsWith("\"raro;con \"\"comillas\"\".pdf\";", lines[1]);
    }

    [Fact]
    public void ExportExcel_GeneraPaqueteOoxmlValido()
    {
        var path = Path.Combine(_tempDir, "salida.xlsx");

        LiteExportService.ExportExcel(new[] { SampleDoc() }, path);

        using var archive = ZipFile.OpenRead(path);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("_rels/.rels"));
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/_rels/workbook.xml.rels"));
        var sheet = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(sheet);
        using var reader = new StreamReader(sheet!.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("FileName", xml);
        Assert.Contains("informe.pdf", xml);
        Assert.Contains("T01.02", xml);
    }

    [Fact]
    public void ExportExcel_ConCaracteresEspeciales_GeneraXmlValido()
    {
        var path = Path.Combine(_tempDir, "especiales.xlsx");
        var doc = SampleDoc();
        doc.FileName = "informe & anexo <final>.pdf" + (char)1;

        LiteExportService.ExportExcel(new[] { doc }, path);

        using var archive = ZipFile.OpenRead(path);
        var sheet = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(sheet);
        using var stream = sheet!.Open();
        var xdoc = XDocument.Load(stream);

        var cellText = xdoc.Descendants().First(e => e.Name.LocalName == "t" && (e.Value.Contains("informe") || e.Value.Contains("anexo"))).Value;
        Assert.Contains("informe & anexo", cellText);
        Assert.DoesNotContain((char)1, cellText);
    }

    [Fact]
    public void ExportCsv_ValorQueEmpiezaPorIgual_SeNeutraliza()
    {
        var path = Path.Combine(_tempDir, "formula.csv");
        var doc = SampleDoc();
        doc.FileName = "=1+1";

        LiteExportService.ExportCsv(new[] { doc }, path);

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.False(lines[1].StartsWith("="));
        Assert.StartsWith("\"'=1+1\"", lines[1]);
    }

    [Fact]
    public void ExportCsv_NumerosNegativos_NoSeNeutralizan()
    {
        var path = Path.Combine(_tempDir, "negativo.csv");
        var doc = SampleDoc();
        doc.DurationMs = -5;

        LiteExportService.ExportCsv(new[] { doc }, path);

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.EndsWith(";-5", lines[1]);
    }
}
