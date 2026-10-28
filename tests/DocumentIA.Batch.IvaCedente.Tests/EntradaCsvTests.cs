using DocumentIA.Batch.IvaCedente.Io;
using DocumentIA.Batch.IvaCedente.Models;
using Xunit;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class EntradaCsvTests
{
    private static string Escribir(string contenido)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        File.WriteAllText(path, contenido, new System.Text.UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void Leer_ConBomEspaciosYLineasVacias_DevuelveFilasLimpias()
    {
        var path = Escribir("\uFEFFID_SAREB;Object ID del Documento;TDN2\r\n 79044 ; 107736649 ;escr-10\r\n79044;107736649;ESCR-10\r\n\r\n");
        var filas = EntradaCsv.Leer(path);
        Assert.Single(filas);
        Assert.Equal(new FilaEntrada("79044", "107736649", "ESCR-10"), filas[0]);
    }

    [Fact]
    public void Leer_MismoObjectIdEnDosActivos_ConservaAmbasFilas()
    {
        var filas = EntradaCsv.Leer(Escribir("ID_SAREB;Object ID del Documento;TDN2\n79044;107736649;ESCR-10\n97061;107736649;ESCR-10\n"));
        Assert.Equal(2, filas.Count);
    }

    [Fact]
    public void Leer_SinCabeceraTdn2_Lanza()
        => Assert.Throws<IvaCedenteUsageException>(() => EntradaCsv.Leer(Escribir("ID_SAREB;Object ID del Documento\n1;2\n")));

    [Fact]
    public void Leer_ObjectIdVacioConOtrosValores_Lanza()
        => Assert.Throws<IvaCedenteUsageException>(() => EntradaCsv.Leer(Escribir("ID_SAREB;Object ID del Documento;TDN2\n1; ;ESCR-10\n")));

    [Fact]
    public void Leer_FilaConMenosCeldasQueLaCabecera_Lanza()
    {
        var ex = Assert.Throws<IvaCedenteUsageException>(() => EntradaCsv.Leer(Escribir("ID_SAREB;Object ID del Documento;TDN2\n1;2\n")));
        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public void Leer_FilaConTodoVacio_SeIgnora()
    {
        var filas = EntradaCsv.Leer(Escribir("ID_SAREB;Object ID del Documento;TDN2\n;;\n1;2;ESCR-10\n"));
        Assert.Single(filas);
    }

    [Fact]
    public void Leer_ComillaSinCerrar_Lanza()
        => Assert.Throws<IvaCedenteUsageException>(() => EntradaCsv.Leer(Escribir("ID_SAREB;Object ID del Documento;TDN2\n1;2;\"ESCR-10\n3;4;ESCR-10\n")));
}
