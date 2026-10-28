using DocumentIA.Batch.IvaCedente.Engine;
using DocumentIA.Batch.IvaCedente.Models;
using Xunit;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class ReanudacionTests
{
    private static FilaEntrada E(string id, string obj) => new(id, obj, "ESCR-10");

    private static FilaDocumento D(string id, string obj, string estado, string evidencia = "")
        => new() { IdSareb = id, ObjectId = obj, Procesado = estado, Evidencia = evidencia };

    private static readonly FilaEntrada[] Entrada = { E("1", "a"), E("2", "b"), E("3", "c"), E("4", "d"), E("5", "e") };

    private static readonly FilaDocumento[] Previas =
    {
        D("1", "a", EstadosProcesado.Ok),
        D("2", "b", EstadosProcesado.NoAplica),
        D("3", "c", EstadosProcesado.Error),
        D("4", "d", EstadosProcesado.Pendiente),
    };

    [Fact]
    public void Pendientes_SinReintentar_SaltaFinalesYErrores()
    {
        var p = Reanudacion.Pendientes(Entrada, Previas, reintentarErrores: false);
        Assert.Equal(new[] { E("4", "d"), E("5", "e") }, p);
    }

    [Fact]
    public void Pendientes_ConReintentar_IncluyeErroresYTimeouts()
    {
        var previas = Previas.Append(D("5", "e", EstadosProcesado.Timeout)).ToArray();
        var p = Reanudacion.Pendientes(Entrada, previas, reintentarErrores: true);
        Assert.Equal(new[] { E("3", "c"), E("4", "d"), E("5", "e") }, p);
        var sin = Reanudacion.Pendientes(Entrada, previas, reintentarErrores: false);
        Assert.Equal(new[] { E("4", "d") }, sin);
    }

    [Fact]
    public void Fusionar_RespetaOrdenDeEntradaYNuevasPisanPrevias()
    {
        var nuevas = new[] { D("3", "c", EstadosProcesado.Ok, "nueva") };
        var f = Reanudacion.Fusionar(Entrada, Previas, nuevas);
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, f.Select(x => x.IdSareb));
        Assert.Equal("nueva", f[2].Evidencia);
        Assert.Equal(EstadosProcesado.Ok, f[2].Procesado);
        Assert.Equal(EstadosProcesado.Pendiente, f[4].Procesado);
        Assert.Equal("e", f[4].ObjectId);
    }

    [Fact]
    public void Fusionar_ConservaAlFinalLasPreviasQueYaNoEstanEnLaEntrada()
    {
        var entrada = new[] { E("2", "b"), E("9", "z") };
        var f = Reanudacion.Fusionar(entrada, Previas, new[] { D("9", "z", EstadosProcesado.Ok) });
        Assert.Equal(new[] { "2", "9", "1", "3", "4" }, f.Select(x => x.IdSareb));
        Assert.Equal(EstadosProcesado.NoAplica, f[0].Procesado);
        Assert.Equal(EstadosProcesado.Error, f[3].Procesado);
    }
}
