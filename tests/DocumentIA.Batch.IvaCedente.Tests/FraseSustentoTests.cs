using DocumentIA.Batch.IvaCedente.Engine;
using DocumentIA.Batch.IvaCedente.Models;
using Xunit;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class FraseSustentoTests
{
    [Fact]
    public void Componer_IvaSiCompleto()
    {
        var a = new FilaActivo
        {
            HuboIVA = "sí", TipoIVA = 21, CuotaIVA = 64050, DocIVA = "12345", Cedente = "BANCO X", FechaAdquisicion = "2008-03-12",
            DeduccionIVA = "sí", DocDeduccion = "12345",
        };
        Assert.Equal("IVA sí 21% 64.050,00 € (doc 12345, cedente BANCO X, 2008-03-12); deducción sí (doc 12345)", FraseSustento.Componer(a));
    }

    [Fact]
    public void Componer_TipoConDecimales_YSinCuota()
    {
        var a = new FilaActivo { HuboIVA = "sí", TipoIVA = 7.5, DocIVA = "12345", DeduccionIVA = "no consta" };
        Assert.Equal("IVA sí 7,5% (doc 12345); deducción no consta", FraseSustento.Componer(a));
    }

    [Fact]
    public void Componer_IvaNo()
    {
        var a = new FilaActivo { HuboIVA = "no", DocIVA = "9", Cedente = "C", DeduccionIVA = "no aplica" };
        Assert.Equal("IVA no (doc 9, cedente C); deducción no aplica", FraseSustento.Componer(a));
    }

    [Fact]
    public void Componer_IvaNoConsta()
        => Assert.Equal("IVA no consta; deducción no consta", FraseSustento.Componer(new FilaActivo { HuboIVA = "no consta", DeduccionIVA = "no consta" }));

    [Fact]
    public void Componer_SinTitulo()
        => Assert.Equal("sin título previo", FraseSustento.Componer(new FilaActivo { HuboIVA = "sin título", DeduccionIVA = "sin título" }));

    [Fact]
    public void Componer_DeduccionNoConOtroDoc()
    {
        var a = new FilaActivo { HuboIVA = "sí", DocIVA = "A", DeduccionIVA = "no", DocDeduccion = "B" };
        Assert.EndsWith("deducción no (doc B)", FraseSustento.Componer(a));
    }
}
