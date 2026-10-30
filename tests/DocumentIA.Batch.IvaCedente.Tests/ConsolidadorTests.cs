using DocumentIA.Batch.IvaCedente.Engine;
using DocumentIA.Batch.IvaCedente.Models;
using Xunit;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class ConsolidadorTests
{
    private static FilaDocumento Titulo(string activo, string objectId, string hubo, string deduccion = "no consta",
        double? tipo = null, double? cuota = null, double? confianza = null, string cedente = "", string fecha = "")
        => new()
        {
            IdSareb = activo,
            ObjectId = objectId,
            Procesado = EstadosProcesado.Ok,
            EsTituloPrevio = "sí",
            HuboIVA = hubo,
            DeduccionIVA = deduccion,
            TipoIVA = tipo,
            CuotaIVA = cuota,
            Confianza = confianza,
            Cedente = cedente,
            FechaAdquisicion = fecha,
        };

    private static FilaActivo Uno(params FilaDocumento[] filas) => Assert.Single(Consolidador.Consolidar(filas));

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

    [Theory]
    [InlineData(EstadosProcesado.Error)]
    [InlineData(EstadosProcesado.Timeout)]
    [InlineData(EstadosProcesado.Pendiente)]
    public void Consolidar_FilaIncompletaConIvaSi_DejaElMotivoPeroNoRevisa(string estado)
    {
        var a = Uno(Titulo("1", "A", "sí", "sí"), new FilaDocumento { IdSareb = "1", ObjectId = "B", Procesado = estado });
        Assert.Equal("no", a.Revisar);
        Assert.Equal("filas sin resolver (1)", a.MotivoRevisar);
        Assert.Equal("sí", a.HuboIVA);
    }

    [Theory]
    [InlineData(EstadosProcesado.Error)]
    [InlineData(EstadosProcesado.Pendiente)]
    public void Consolidar_FilaIncompletaSinIvaSi_SigueRevisando(string estado)
    {
        var sinTitulo = Uno(new FilaDocumento { IdSareb = "1", ObjectId = "A", Procesado = estado });
        Assert.Equal(Consolidador.SinTitulo, sinTitulo.HuboIVA);
        Assert.Equal("sí", sinTitulo.Revisar);

        var noConsta = Uno(Titulo("2", "B", "no consta"), new FilaDocumento { IdSareb = "2", ObjectId = "C", Procesado = estado });
        Assert.Equal("sí", noConsta.Revisar);
        Assert.Equal("filas sin resolver (1)", noConsta.MotivoRevisar);

        // Un título sin leer podría decir sí y, por la precedencia sí > no, cambiar el resultado.
        var conNo = Uno(Titulo("3", "D", "no"), new FilaDocumento { IdSareb = "3", ObjectId = "E", Procesado = estado });
        Assert.Equal("sí", conNo.Revisar);
        Assert.Equal("filas sin resolver (1)", conNo.MotivoRevisar);
    }

    [Fact]
    public void Consolidar_ActivoCompletoSinContradiccion_NoMarcaRevisar()
    {
        var a = Assert.Single(Consolidador.Consolidar(new[] { Titulo("1", "A", "sí") }));
        Assert.Equal("no", a.Revisar);
    }

    [Fact]
    public void Consolidar_DocIVA_PrefiereElTituloConCuota()
    {
        var a = Uno(Titulo("1", "A", "sí", confianza: 0.9, cedente: "CA", fecha: "2001-01-01"),
            Titulo("1", "B", "sí", tipo: 21, cuota: 64050, confianza: 0.5, cedente: "CB", fecha: "2008-03-12"));
        Assert.Equal("B", a.DocIVA);
        Assert.Equal(21, a.TipoIVA);
        Assert.Equal(64050, a.CuotaIVA);
        Assert.Equal("CB", a.Cedente);
        Assert.Equal("2008-03-12", a.FechaAdquisicion);
    }

    [Fact]
    public void Consolidar_DocIVA_SinCuotasPrefiereTipoLuegoConfianzaLuegoOrden()
    {
        Assert.Equal("B", Uno(Titulo("1", "A", "sí", confianza: 0.6), Titulo("1", "B", "sí", tipo: 21, confianza: 0.4)).DocIVA);
        Assert.Equal("B", Uno(Titulo("1", "A", "sí", confianza: 0.6), Titulo("1", "B", "sí", confianza: 0.9)).DocIVA);
        Assert.Equal("A", Uno(Titulo("1", "A", "sí"), Titulo("1", "B", "sí")).DocIVA);
    }

    [Fact]
    public void Consolidar_HuboIVANo_DocIVAEsElNoYSinTipoNiCuota()
    {
        var a = Uno(Titulo("1", "A", "no", tipo: 21, cuota: 100, cedente: "CA"));
        Assert.Equal("no", a.HuboIVA);
        Assert.Equal("A", a.DocIVA);
        Assert.Null(a.TipoIVA);
        Assert.Null(a.CuotaIVA);
        Assert.Equal("CA", a.Cedente);
    }

    [Fact]
    public void Consolidar_NoConstaOSinTitulo_DocIVAVacio()
    {
        Assert.Equal("", Uno(Titulo("1", "A", "no consta")).DocIVA);
        var sin = Uno(new FilaDocumento { IdSareb = "1", ObjectId = "A", Procesado = EstadosProcesado.NoAplica });
        Assert.Equal("", sin.DocIVA);
        Assert.Equal("", sin.DocDeduccion);
    }

    [Fact]
    public void Consolidar_DocDeduccion_PrefiereElMismoQueDocIVA()
    {
        var a = Uno(Titulo("1", "A", "sí", "sí", cuota: 100, confianza: 0.5), Titulo("1", "B", "sí", "sí", cuota: 100, confianza: 0.9));
        Assert.Equal("B", a.DocIVA);
        Assert.Equal("B", a.DocDeduccion);
    }

    [Fact]
    public void Consolidar_DocDeduccion_OtroTituloSiDocIVANoLaSostiene()
    {
        var a = Uno(Titulo("1", "A", "sí", "no consta", cuota: 100), Titulo("1", "B", "sí", "sí"));
        Assert.Equal("A", a.DocIVA);
        Assert.Equal("sí", a.DeduccionIVA);
        Assert.Equal("B", a.DocDeduccion);
    }

    [Fact]
    public void Consolidar_CuotasDistintas_MarcaRevisarConMotivo()
    {
        var a = Uno(Titulo("1", "A", "sí", cuota: 100), Titulo("1", "B", "sí", cuota: 200));
        Assert.Equal("sí", a.Revisar);
        Assert.Equal("cuotas de IVA distintas", a.MotivoRevisar);
    }

    [Fact]
    public void Consolidar_CuotasIguales_NoMarcaRevisar()
    {
        var a = Uno(Titulo("1", "A", "sí", cuota: 100), Titulo("1", "B", "sí", cuota: 100));
        Assert.Equal("no", a.Revisar);
        Assert.Equal("", a.MotivoRevisar);
    }

    [Fact]
    public void Consolidar_IvaContradictorio_RecogeLosTitulosQueDiscrepan()
    {
        var a = Uno(Titulo("1", "B", "no"), Titulo("1", "A", "sí", tipo: 21, cuota: 1200), Titulo("1", "C", "no consta"));
        var d = Assert.Single(a.Discrepancias);
        Assert.Equal(new Discrepancia("B", "no", null, null, null), d);
    }

    [Fact]
    public void Consolidar_CuotasDistintas_RecogeLosTitulosConOtraCuota()
    {
        var a = Uno(Titulo("1", "A", "sí", tipo: 21, cuota: 100, confianza: 0.9), Titulo("1", "B", "sí", tipo: 21, cuota: 200), Titulo("1", "C", "sí"));
        Assert.Equal("A", a.DocIVA);
        var d = Assert.Single(a.Discrepancias);
        Assert.Equal(new Discrepancia("B", "sí", 21, 200, null), d);
    }

    [Fact]
    public void Consolidar_DeduccionContradictoria_RecogeLosTitulosQueDiscrepan()
    {
        var a = Uno(Titulo("1", "A", "sí", "sí"), Titulo("1", "B", "sí", "no"), Titulo("1", "C", "sí", "no aplica"));
        var d = Assert.Single(a.Discrepancias);
        Assert.Equal(new Discrepancia("B", null, null, null, "no"), d);
    }

    [Fact]
    public void Consolidar_TituloQueDiscrepaEnAmbas_UnaSolaEntradaOrdenadaPorObjectId()
    {
        var a = Uno(Titulo("1", "Z", "no", "no"), Titulo("1", "A", "sí", "sí", cuota: 100), Titulo("1", "M", "sí", "no", cuota: 300));
        Assert.Equal(new[] { new Discrepancia("M", "sí", null, 300, "no"), new Discrepancia("Z", "no", null, null, "no") }, a.Discrepancias);
    }

    [Fact]
    public void Consolidar_SinMotivosDeDiscrepancia_ListaVacia()
    {
        Assert.Empty(Uno(Titulo("1", "A", "sí", cuota: 100), Titulo("1", "B", "sí", cuota: 100)).Discrepancias);
        Assert.Empty(Uno(Titulo("1", "A", "sí"), new FilaDocumento { IdSareb = "1", ObjectId = "B", Procesado = EstadosProcesado.Error }).Discrepancias);
    }

    [Fact]
    public void Consolidar_MotivoRevisar_AcumulaEnOrden()
    {
        var a = Uno(
            new FilaDocumento { IdSareb = "1", ObjectId = "E1", Procesado = EstadosProcesado.Error },
            new FilaDocumento { IdSareb = "1", ObjectId = "E2", Procesado = EstadosProcesado.Timeout },
            Titulo("1", "A", "sí"), Titulo("1", "B", "no"), Titulo("1", "C", "no consta", "sí"), Titulo("1", "D", "no consta", "no"));
        Assert.Equal("sí", a.Revisar);
        Assert.Equal("filas sin resolver (2); IVA contradictorio; deducción contradictoria", a.MotivoRevisar);
    }

    [Fact]
    public void Consolidar_ObjectIdRepetido_ConservaLaPrimeraDiscrepancia()
    {
        var a = Uno(Titulo("1", "A", "sí", cuota: 100), Titulo("1", "B", "sí", cuota: 200), Titulo("1", "B", "sí", cuota: 300));
        Assert.Equal(new[] { new Discrepancia("B", "sí", null, 200, null) }, a.Discrepancias);
    }

    [Fact]
    public void Consolidar_ObjectIdRepetido_LaSegundaEntraSiLaPrimeraNoDiscrepa()
    {
        var a = Uno(Titulo("1", "A", "sí", cuota: 100), Titulo("1", "B", "sí", cuota: 100), Titulo("1", "B", "sí", cuota: 300));
        Assert.Equal(new[] { new Discrepancia("B", "sí", null, 300, null) }, a.Discrepancias);
    }

    [Fact]
    public void Consolidar_CuotasQueDifierenPorDebajoDelCentimo_NoMarcaRevisar()
    {
        var a = Uno(Titulo("1", "A", "sí", cuota: 1200.001), Titulo("1", "B", "sí", cuota: 1200.004));
        Assert.Equal("no", a.Revisar);
        Assert.Empty(a.Discrepancias);
    }

    [Fact]
    public void Consolidar_CuotasQueDifierenEnUnCentimo_MarcaRevisarYRecogeLaDiscrepancia()
    {
        var a = Uno(Titulo("1", "A", "sí", cuota: 1200.00), Titulo("1", "B", "sí", cuota: 1200.01));
        Assert.Equal("sí", a.Revisar);
        Assert.Equal(Consolidador.MotivoCuotasDistintas, a.MotivoRevisar);
        Assert.Equal(new[] { new Discrepancia("B", "sí", null, 1200.01, null) }, a.Discrepancias);
    }

    [Fact]
    public void Consolidar_CuotaNulaJuntoACuotaInformada_NoCuentaComoDistinta()
    {
        var a = Uno(Titulo("1", "A", "sí", cuota: 100), Titulo("1", "B", "sí"));
        Assert.Equal("no", a.Revisar);
        Assert.Empty(a.Discrepancias);
    }

    [Fact]
    public void Consolidar_Sustento_FraseCompletaSinDiscrepancias()
    {
        var a = Uno(Titulo("1", "12345", "sí", "sí", tipo: 21, cuota: 64050, cedente: "BANCO X", fecha: "2008-03-12"), Titulo("1", "99", "no consta"));
        Assert.Equal("IVA sí 21% 64.050,00 € (doc 12345, cedente BANCO X, 2008-03-12); deducción sí (doc 12345)", a.Sustento);
    }

    [Fact]
    public void Consolidar_Sustento_FraseCompletaConTramoDeDiscrepancias()
    {
        var a = Uno(Titulo("1", "A", "sí", "no", tipo: 21, cuota: 1200), Titulo("1", "B", "sí", "no", tipo: 21, cuota: 1300), Titulo("1", "C", "no", "no"));
        Assert.Equal("IVA sí 21% 1.200,00 € (doc A); deducción no (doc A); discrepan: doc B IVA sí 21% 1.300,00 €; doc C IVA no", a.Sustento);
    }

    [Fact]
    public void Consolidar_CuotasQueSeMuestranIgualesAlCentimo_NoSonDistintas()
    {
        // 2,675 se muestra como 2,67 €, igual que 2,67: comparar y presentar redondean igual.
        var a = Uno(Titulo("1", "A", "sí", cuota: 2.67), Titulo("1", "B", "sí", cuota: 2.675));
        Assert.Equal("no", a.Revisar);
        Assert.Empty(a.Discrepancias);
    }
}
