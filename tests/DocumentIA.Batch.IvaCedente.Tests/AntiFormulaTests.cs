using DocumentIA.Batch.IvaCedente.Io;
using Xunit;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class AntiFormulaTests
{
    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@SUMA", "'@SUMA")]
    [InlineData("\ttab", "'\ttab")]
    [InlineData("\rcr", "'\rcr")]
    [InlineData("'ya", "''ya")]
    [InlineData("normal", "normal")]
    [InlineData("", "")]
    public void Neutralizar_YDesneutralizar_HacenViajeDeIda(string original, string esperado)
    {
        Assert.Equal(esperado, AntiFormula.Neutralizar(original));
        Assert.Equal(original, AntiFormula.Desneutralizar(esperado));
    }

    [Fact]
    public void Neutralizar_Nulo_DevuelveVacio() => Assert.Equal("", AntiFormula.Neutralizar(null));
}
