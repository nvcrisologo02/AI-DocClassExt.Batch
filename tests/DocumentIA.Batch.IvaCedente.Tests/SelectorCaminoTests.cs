using DocumentIA.Batch.IvaCedente.Engine;
using DocumentIA.Batch.IvaCedente.Models;
using Xunit;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class SelectorCaminoTests
{
    [Theory]
    [InlineData("ESCR-10", Camino.UnaLlamada)]
    [InlineData("cera-14", Camino.UnaLlamada)]
    [InlineData("ESCR-24", Camino.UnaLlamada)]
    [InlineData("SERE-90", Camino.UnaLlamada)]
    [InlineData("TASA-09", Camino.UnaLlamada)]
    [InlineData("ZZZZ-99", Camino.UnaLlamada)]
    [InlineData("", Camino.UnaLlamada)]
    [InlineData("TASA-11", Camino.NoAplica)]
    [InlineData(" tasa-11 ", Camino.NoAplica)]
    [InlineData("SCXX-00", Camino.NoAplica)]
    public void Decidir_SegunConfiguracionPorDefecto(string tdn2, Camino esperado)
        => Assert.Equal(esperado, SelectorCamino.Decidir(tdn2, new IvaConfig()));

    [Fact]
    public void Decidir_ScxxQuitadoDeExcluidos_EsDosLlamadas()
        => Assert.Equal(Camino.DosLlamadas, SelectorCamino.Decidir("SCXX-00", new IvaConfig { CodigosExcluidos = new() { "TASA-11" } }));
}
