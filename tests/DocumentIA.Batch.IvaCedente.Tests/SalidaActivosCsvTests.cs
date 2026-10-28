using DocumentIA.Batch.IvaCedente.Io;
using DocumentIA.Batch.IvaCedente.Models;
using Xunit;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class SalidaActivosCsvTests
{
    [Fact]
    public void Escribir_NeutralizaLasColumnasDeTexto()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            SalidaActivosCsv.Escribir(path, new[]
            {
                new FilaActivo { IdSareb = "=1+1", Documentos = 2, Leidos = 1, ConRespuesta = 1, HuboIVA = "sí", DeduccionIVA = "no", ObjectIdsSoporte = "-5,@x", Revisar = "no" },
            });

            var fila = CsvUtil.ReadRows(path)[1];
            Assert.Equal("'=1+1", fila[0]);
            Assert.Equal("2", fila[1]);
            Assert.Equal("sí", fila[4]);
            Assert.Equal("'-5,@x", fila[6]);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
