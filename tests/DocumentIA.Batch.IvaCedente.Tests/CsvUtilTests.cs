using DocumentIA.Batch.IvaCedente.Io;
using Xunit;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class CsvUtilTests
{
    [Fact]
    public void AppendRow_YReadRows_ConservanCamposConSeparadorComillasYSaltos()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            CsvUtil.AppendRow(path, new[] { "a", "b" }, new[] { "x;y", "dice \"hola\"\nsegunda" });
            var rows = CsvUtil.ReadRows(path);
            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { "x;y", "dice \"hola\"\nsegunda" }, rows[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteRowsAtomic_NoDejaTmpYSustituyeElContenido()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            CsvUtil.WriteRows(path, new[] { "a" }, new[] { (IEnumerable<string?>)new string?[] { "viejo" } });
            CsvUtil.WriteRowsAtomic(path, new[] { "a", "b" }, new[] { (IEnumerable<string?>)new string?[] { "x", "y;z" } });

            Assert.False(File.Exists(path + ".tmp"));
            var rows = CsvUtil.ReadRows(path);
            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { "x", "y;z" }, rows[1]);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }
}
