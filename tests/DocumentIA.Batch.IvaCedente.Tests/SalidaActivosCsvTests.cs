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
            Assert.Equal("'-5,@x", fila[14]);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public void Escribir_ColumnasNuevasEnOrdenYNumerosConPunto()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            SalidaActivosCsv.Escribir(path, new[]
            {
                new FilaActivo
                {
                    IdSareb = "1", HuboIVA = "sí", DeduccionIVA = "sí", DocIVA = "=5", TipoIVA = 21, CuotaIVA = 64050.5, Cedente = "C",
                    FechaAdquisicion = "2008-03-12", DocDeduccion = "7", Sustento = "IVA sí 21% 64.050,50 € (doc 5)", MotivoRevisar = "cuotas de IVA distintas",
                    ObjectIdsSoporte = "5,7", Revisar = "sí",
                },
            });

            var filas = CsvUtil.ReadRows(path);
            Assert.Equal(SalidaActivosCsv.Cabecera, filas[0]);
            Assert.Equal(new[] { "ID_SAREB", "Documentos", "Leidos", "ConRespuesta", "HuboIVA", "DeduccionIVA", "DocIVA", "TipoIVA", "CuotaIVA", "Cedente", "FechaAdquisicion", "DocDeduccion", "Sustento", "MotivoRevisar", "ObjectIdsSoporte", "Revisar" }, filas[0]);
            var fila = filas[1];
            Assert.Equal("'=5", fila[6]);
            Assert.Equal("21", fila[7]);
            Assert.Equal("64050.5", fila[8]);
            Assert.Equal("C", fila[9]);
            Assert.Equal("2008-03-12", fila[10]);
            Assert.Equal("7", fila[11]);
            Assert.Equal("IVA sí 21% 64.050,50 € (doc 5)", fila[12]);
            Assert.Equal("cuotas de IVA distintas", fila[13]);
            Assert.Equal("5,7", fila[14]);
            Assert.Equal("sí", fila[15]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
