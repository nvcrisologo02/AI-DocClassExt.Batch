using System.IO;
using System.Text;
using DocumentIA.Batch.Evaluation.Io;
using Xunit;

namespace DocumentIA.Batch.Evaluation.Tests;

public class ListCsvReaderTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), "list-test-" + Guid.NewGuid().ToString("N") + ".csv");

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
    }

    [Fact]
    public void Read_DerivaNombreYCarpetaDelRelPath()
    {
        File.WriteAllText(_tempFile,
            "rel_path;expected_tdn1;expected_tdn2\r\n" +
            "ACTR/ACTR-01--OP-99_106308212.PDF;ACTR;ACTR-01\r\n" +
            "ESIN/sub/x.pdf;ESIN;ESIN-34\r\n",
            new UTF8Encoding(true));

        var rows = ListCsvReader.Read(_tempFile);

        Assert.Equal(2, rows.Count);
        Assert.Equal("ACTR/ACTR-01--OP-99_106308212.PDF", rows[0].RelPath);
        Assert.Equal("ACTR-01--OP-99_106308212.PDF", rows[0].FileName);
        Assert.Equal("ACTR", rows[0].Tdn1Folder);
        Assert.Equal("ACTR", rows[0].ExpectedTdn1);
        Assert.Equal("ACTR-01", rows[0].ExpectedTdn2);
        Assert.True(rows[0].Validated);
        Assert.Equal("x.pdf", rows[1].FileName);
        Assert.Equal("ESIN", rows[1].Tdn1Folder);
    }

    [Fact]
    public void Read_ToleraOrdenDeColumnasDistinto()
    {
        File.WriteAllText(_tempFile,
            "expected_tdn2;rel_path;expected_tdn1\r\nCOMU-02;COMU/c.pdf;COMU\r\n");

        var rows = ListCsvReader.Read(_tempFile);

        Assert.Single(rows);
        Assert.Equal("COMU/c.pdf", rows[0].RelPath);
        Assert.Equal("COMU", rows[0].ExpectedTdn1);
        Assert.Equal("COMU-02", rows[0].ExpectedTdn2);
    }

    [Fact]
    public void Read_SinColumnaRelPath_LanzaUsageException()
    {
        File.WriteAllText(_tempFile, "file_name;expected_tdn1\r\na.pdf;ACTE\r\n");

        Assert.Throws<EvaluationUsageException>(() => ListCsvReader.Read(_tempFile));
    }

    [Fact]
    public void Read_FicheroVacio_DevuelveListaVacia()
    {
        File.WriteAllText(_tempFile, string.Empty);

        Assert.Empty(ListCsvReader.Read(_tempFile));
    }
}
