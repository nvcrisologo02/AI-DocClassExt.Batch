using DocumentIA.Batch.IvaCedente.Engine;
using DocumentIA.Batch.IvaCedente.Models;
using Xunit;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class ConsolidadorTests
{
    private static FilaDocumento Titulo(string activo, string objectId, string hubo, string deduccion = "no consta")
        => new()
        {
            IdSareb = activo,
            ObjectId = objectId,
            Procesado = EstadosProcesado.Ok,
            EsTituloPrevio = "sí",
            HuboIVA = hubo,
            DeduccionIVA = deduccion,
        };

    [Theory]
    [InlineData(new[] { "no consta", "sí", "no" }, "sí")]
    [InlineData(new[] { "no consta", "no" }, "no")]
    [InlineData(new[] { "no consta", "no consta" }, "no consta")]
    [InlineData(new string[0], "no consta")]
    public void Resolver_AplicaLaPrecedencia(string[] valores, string esperado)
        => Assert.Equal(esperado, Consolidador.Resolver(valores));

    [Fact]
    public void Consolidar_ActivoConContradiccion_MarcaRevisarYSoporte()
    {
        var filas = new[] { Titulo("1", "A", "sí"), Titulo("1", "B", "no"), Titulo("1", "C", "no consta") };
        var a = Assert.Single(Consolidador.Consolidar(filas));
        Assert.Equal("sí", a.HuboIVA);
        Assert.Equal("sí", a.Revisar);
        Assert.Equal("A", a.ObjectIdsSoporte);
        Assert.Equal(3, a.ConRespuesta);
        Assert.Equal(3, a.Leidos);
        Assert.Equal(3, a.Documentos);
    }

    [Fact]
    public void Consolidar_ActivoSinTitulos_EsSinTitulo()
    {
        var filas = new[]
        {
            new FilaDocumento { IdSareb = "1", ObjectId = "A", Procesado = EstadosProcesado.NoAplica },
            new FilaDocumento { IdSareb = "1", ObjectId = "B", Procesado = EstadosProcesado.Ok, EsTituloPrevio = "no" },
        };
        var a = Assert.Single(Consolidador.Consolidar(filas));
        Assert.Equal(2, a.Documentos);
        Assert.Equal(1, a.Leidos);
        Assert.Equal(0, a.ConRespuesta);
        Assert.Equal("sin título", a.HuboIVA);
        Assert.Equal("sin título", a.DeduccionIVA);
        Assert.Equal("no", a.Revisar);
        Assert.Equal("", a.ObjectIdsSoporte);
    }

    [Fact]
    public void Consolidar_DeduccionIgnoraNoAplica()
    {
        var filas = new[] { Titulo("1", "A", "sí", "no aplica"), Titulo("1", "B", "sí", "sí") };
        var a = Assert.Single(Consolidador.Consolidar(filas));
        Assert.Equal("sí", a.DeduccionIVA);
        Assert.Equal("no", a.Revisar);
        Assert.Equal("A,B", a.ObjectIdsSoporte);
    }

    [Fact]
    public void Consolidar_TodasNoAplica_DeduccionNoAplica()
    {
        var a = Assert.Single(Consolidador.Consolidar(new[] { Titulo("1", "A", "no", "no aplica") }));
        Assert.Equal("no aplica", a.DeduccionIVA);
    }

    [Fact]
    public void Consolidar_NoParseableCuentaComoLeido_YSoporteSinDuplicados()
    {
        var filas = new[]
        {
            new FilaDocumento { IdSareb = "1", ObjectId = "A", Procesado = EstadosProcesado.NoParseable },
            Titulo("1", "B", "no"),
            Titulo("1", "B", "no"),
        };
        var a = Assert.Single(Consolidador.Consolidar(filas));
        Assert.Equal(3, a.Leidos);
        Assert.Equal(2, a.ConRespuesta);
        Assert.Equal("B", a.ObjectIdsSoporte);
    }

    [Fact]
    public void Consolidar_ConservaElOrdenDePrimeraAparicion()
    {
        var filas = new[] { Titulo("9", "A", "no"), Titulo("3", "B", "no"), Titulo("9", "C", "no") };
        var r = Consolidador.Consolidar(filas);
        Assert.Equal(new[] { "9", "3" }, r.Select(x => x.IdSareb));
        Assert.Equal(2, r[0].Documentos);
    }
}
