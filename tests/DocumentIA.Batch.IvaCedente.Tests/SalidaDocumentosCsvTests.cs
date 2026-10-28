using DocumentIA.Batch.IvaCedente.Io;
using DocumentIA.Batch.IvaCedente.Models;
using Xunit;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class SalidaDocumentosCsvTests
{
    private static string RutaTemporal() => Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");

    [Fact]
    public void Anexar_YLeer_IdaYVueltaConEvidenciaMultilinea()
    {
        var path = RutaTemporal();
        var fila = new FilaDocumento
        {
            IdSareb = "79044",
            ObjectId = "107736649",
            Tdn2 = "ESCR-10",
            Procesado = EstadosProcesado.Ok,
            EsTituloPrevio = "sí",
            HuboIVA = ValoresIva.Si,
            DeduccionIVA = ValoresIva.NoConsta,
            Cedente = "Banco; S.A.",
            FechaAdquisicion = "01/02/2020",
            Evidencia = "precio; \"con IVA\"\nlínea 2",
            Confianza = 0.85,
            Tipologia = "tipo",
            InstanceId = "i1",
            CorrelationId = "c1",
            Error = "",
            Fecha = new DateTime(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc),
        };
        SalidaDocumentosCsv.Anexar(path, fila);
        SalidaDocumentosCsv.Anexar(path, fila.ParaActivo("97061", "ESCR-10"));

        var leidas = SalidaDocumentosCsv.Leer(path);

        Assert.Equal(2, leidas.Count);
        var l = leidas[0];
        Assert.Equal(fila.Evidencia, l.Evidencia);
        Assert.Equal(0.85, l.Confianza);
        Assert.Equal(fila.Fecha, l.Fecha);
        Assert.Equal(DateTimeKind.Utc, l.Fecha!.Value.Kind);
        Assert.Equal("Banco; S.A.", l.Cedente);
        Assert.Equal("97061", leidas[1].IdSareb);
        Assert.Equal(string.Join(';', SalidaDocumentosCsv.Cabecera), File.ReadLines(path).First().TrimStart('﻿'));
    }

    [Fact]
    public void Leer_FicheroInexistente_DevuelveVacio()
        => Assert.Empty(SalidaDocumentosCsv.Leer(RutaTemporal()));

    [Fact]
    public void Escribir_ReemplazaElContenido_YConfianzaNulaQuedaVacia()
    {
        var path = RutaTemporal();
        SalidaDocumentosCsv.Anexar(path, new FilaDocumento { IdSareb = "1", ObjectId = "1" });
        SalidaDocumentosCsv.Escribir(path, new[] { new FilaDocumento { IdSareb = "2", ObjectId = "2" } });
        var leidas = SalidaDocumentosCsv.Leer(path);
        Assert.Single(leidas);
        Assert.Equal("2", leidas[0].IdSareb);
        Assert.Null(leidas[0].Confianza);
        Assert.Null(leidas[0].Fecha);
    }

    [Fact]
    public void Anexar_YLeer_NeutralizaFormulasYConservaElValor()
    {
        var path = RutaTemporal();
        var valores = new[] { "=HYPERLINK(\"x\")", "+34", "-1", "@SUM(A1)", "'ya citado" };
        foreach (var v in valores)
        {
            SalidaDocumentosCsv.Anexar(path, new FilaDocumento { IdSareb = "1", ObjectId = "1", Evidencia = v, Cedente = v, FechaAdquisicion = v });
        }

        var texto = File.ReadAllText(path);
        Assert.Contains(";'+34;", texto);
        Assert.Contains(";'-1;", texto);
        Assert.Contains(";'@SUM(A1);", texto);
        Assert.Contains(";''ya citado;", texto);
        Assert.Contains("'=HYPERLINK(\"\"x\"\")", texto);

        var leidas = SalidaDocumentosCsv.Leer(path);
        Assert.Equal(valores.Length, leidas.Count);
        for (var i = 0; i < valores.Length; i++)
        {
            Assert.Equal(valores[i], leidas[i].Evidencia);
            Assert.Equal(valores[i], leidas[i].Cedente);
            Assert.Equal(valores[i], leidas[i].FechaAdquisicion);
        }
    }

    [Fact]
    public void Anexar_EvidenciaLarga_SeTrunca()
    {
        var path = RutaTemporal();
        SalidaDocumentosCsv.Anexar(path, new FilaDocumento { IdSareb = "1", ObjectId = "1", Evidencia = new string('a', 5000) });
        var ev = SalidaDocumentosCsv.Leer(path)[0].Evidencia;
        Assert.Equal(4000 + " [truncado]".Length, ev.Length);
        Assert.EndsWith(" [truncado]", ev);
        SalidaDocumentosCsv.Anexar(path, new FilaDocumento { IdSareb = "1", ObjectId = "2", Evidencia = new string('b', 4000) });
        Assert.Equal(4000, SalidaDocumentosCsv.Leer(path)[1].Evidencia.Length);
    }
}
