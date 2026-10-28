using Xunit;
namespace DocumentIA.Batch.IvaCedente.Tests;

public class CliArgsTests
{
    [Fact]
    public void Has_FlagSinValor_DevuelveTrue()
    {
        var args = CliArgs.Parse(new[] { "--entrada", "in.csv", "--reintentar-errores", "--salida", "out.csv" });
        Assert.True(args.Has("reintentar-errores"));
        Assert.False(args.Has("colas"));
        Assert.Equal("out.csv", args.Require("salida"));
    }
}
