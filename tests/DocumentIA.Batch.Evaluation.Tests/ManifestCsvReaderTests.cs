using System.IO;
using DocumentIA.Batch.Evaluation.Io;
using Xunit;

namespace DocumentIA.Batch.Evaluation.Tests;

public class ManifestCsvReaderTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), "manifest-test-" + Guid.NewGuid().ToString("N") + ".csv");

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
    }

    [Fact]
    public void Read_ParseaCamposYBooleanos()
    {
        var csv =
            "tdn1_folder;file_name;rel_path;size_bytes;mtime_utc;expected_tdn1;expected_tdn2;validated;folder_is_tdn1;in_cata100;label_matches_folder\r\n"
            + "ACTE;ACTE-02--x.pdf;ACTE/ACTE-02--x.pdf;3328245;2026-06-17T08:07:34Z;ACTE;ACTE-02;True;True;False;True\r\n"
            + "COMU;sin_etiqueta.pdf;COMU/sin_etiqueta.pdf;100;2026-06-17T08:07:34Z;;;False;True;False;False\r\n";
        File.WriteAllText(_tempFile, csv, new System.Text.UTF8Encoding(true));

        var rows = ManifestCsvReader.Read(_tempFile);

        Assert.Equal(2, rows.Count);
        var first = rows[0];
        Assert.Equal("ACTE", first.Tdn1Folder);
        Assert.Equal("ACTE-02--x.pdf", first.FileName);
        Assert.Equal("ACTE/ACTE-02--x.pdf", first.RelPath);
        Assert.Equal(3328245, first.SizeBytes);
        Assert.Equal("ACTE", first.ExpectedTdn1);
        Assert.Equal("ACTE-02", first.ExpectedTdn2);
        Assert.True(first.Validated);
        Assert.True(first.FolderIsTdn1);
        Assert.False(first.InCata100);
        Assert.True(first.LabelMatchesFolder);

        var second = rows[1];
        Assert.False(second.Validated);
        Assert.Equal(string.Empty, second.ExpectedTdn1);
    }

    [Fact]
    public void Read_FicheroSoloCabecera_DevuelveListaVacia()
    {
        File.WriteAllText(
            _tempFile,
            "tdn1_folder;file_name;rel_path;size_bytes;mtime_utc;expected_tdn1;expected_tdn2;validated;folder_is_tdn1;in_cata100;label_matches_folder\r\n",
            new System.Text.UTF8Encoding(true));

        var rows = ManifestCsvReader.Read(_tempFile);

        Assert.Empty(rows);
    }
}
