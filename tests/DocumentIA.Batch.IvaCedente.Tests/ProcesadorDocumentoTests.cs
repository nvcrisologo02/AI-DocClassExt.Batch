using Xunit;
using DocumentIA.Batch.IvaCedente.Engine;
using DocumentIA.Batch.IvaCedente.Models;
using DocumentIA.Batch.IvaCedente.Tests.Fakes;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class ProcesadorDocumentoTests
{
    private const string JsonSi = "{\"esTituloPrevio\":true,\"huboIVA\":\"sí\",\"deduccionIVA\":\"sí\",\"cedente\":\"Banco X\",\"fechaAdquisicion\":\"2008-05-12\",\"evidencia\":\"con renuncia a la exención\",\"confianza\":0.9}";
    private const string JsonNoTitulo = "{\"esTituloPrevio\":false,\"huboIVA\":\"no consta\",\"deduccionIVA\":\"no aplica\",\"cedente\":null,\"fechaAdquisicion\":null,\"evidencia\":null,\"confianza\":0.95}";

    private static ProcesadorDocumento Crear(FakeIngestBackend backend, IvaConfig? config = null)
        => new(backend, config ?? new IvaConfig(), new Prompts("sistema", "usuario"), (_, _) => Task.CompletedTask);

    private static Task<FilaDocumento> Procesar(ProcesadorDocumento p, string tdn2)
        => p.ProcesarAsync(new FilaEntrada("A1", "OBJ1", tdn2), CancellationToken.None);

    private static IvaConfig ConfigDosLlamadas() => new() { CodigosExcluidos = new List<string> { "TASA-11" } };

    [Fact]
    public async Task NoAplica_NoLlamaAlBackend()
    {
        var backend = new FakeIngestBackend();
        var fila = await Procesar(Crear(backend), "TASA-11");

        Assert.Equal(EstadosProcesado.NoAplica, fila.Procesado);
        Assert.Equal("", fila.HuboIVA);
        Assert.Empty(backend.Requests);
        Assert.NotNull(fila.Fecha);
    }

    [Fact]
    public async Task UnaLlamada_TituloConIva_RellenaLaFila()
    {
        var backend = new FakeIngestBackend { OnStatus = _ => FakeIngestBackend.CompletedStatus("OK", "ESCR-10", JsonSi) };
        var fila = await Procesar(Crear(backend), "ESCR-10");

        Assert.Equal(EstadosProcesado.Ok, fila.Procesado);
        Assert.Equal("sí", fila.EsTituloPrevio);
        Assert.Equal("sí", fila.HuboIVA);
        Assert.Equal("sí", fila.DeduccionIVA);
        Assert.Equal("Banco X", fila.Cedente);
        Assert.Equal("2008-05-12", fila.FechaAdquisicion);
        Assert.Equal("con renuncia a la exención", fila.Evidencia);
        Assert.Equal(0.9, fila.Confianza);
        Assert.Equal("ESCR-10", fila.Tipologia);
        Assert.NotEmpty(fila.InstanceId);
        Assert.NotEmpty(fila.CorrelationId);
        var peticion = Assert.Single(backend.Requests);
        Assert.NotNull(peticion.Instrucciones.Prompt);
    }

    [Fact]
    public async Task UnaLlamada_ModeloDiceQueNoEsTitulo()
    {
        var backend = new FakeIngestBackend { OnStatus = _ => FakeIngestBackend.CompletedStatus("OK", "ESCR-10", JsonNoTitulo) };
        var fila = await Procesar(Crear(backend), "ESCR-10");

        Assert.Equal(EstadosProcesado.Ok, fila.Procesado);
        Assert.Equal("no", fila.EsTituloPrevio);
        Assert.Equal("no es título", fila.HuboIVA);
        Assert.Equal("no aplica", fila.DeduccionIVA);
    }

    [Fact]
    public async Task NoAplica_ScxxExcluidoPorDefecto_NoLlamaAlBackend()
    {
        var backend = new FakeIngestBackend();
        var fila = await Procesar(Crear(backend), "SCXX-00");

        Assert.Equal(EstadosProcesado.NoAplica, fila.Procesado);
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task DosLlamadas_ScxxQueResultaEscritura_HaceClasificacionYPrompt()
    {
        var backend = new FakeIngestBackend();
        var llamadas = 0;
        backend.OnStatus = _ => ++llamadas == 1
            ? FakeIngestBackend.CompletedStatus("OK", "ESCR-29", null)
            : FakeIngestBackend.CompletedStatus("OK", "ESCR-29", JsonSi);
        var fila = await Procesar(Crear(backend, ConfigDosLlamadas()), "SCXX-00");

        Assert.Equal(2, backend.Requests.Count);
        Assert.Null(backend.Requests[0].Instrucciones.Prompt);
        Assert.NotNull(backend.Requests[1].Instrucciones.Prompt);
        Assert.Equal("ESCR-29", fila.Tipologia);
        Assert.Equal(EstadosProcesado.Ok, fila.Procesado);
    }

    [Fact]
    public async Task DosLlamadas_ScxxQueResultaTasacion_NoLanzaElPrompt()
    {
        var backend = new FakeIngestBackend { OnStatus = _ => FakeIngestBackend.CompletedStatus("OK", "TASA-11", null) };
        var fila = await Procesar(Crear(backend, ConfigDosLlamadas()), "SCXX-00");

        Assert.Single(backend.Requests);
        Assert.Equal(EstadosProcesado.NoEsTitulo, fila.Procesado);
        Assert.Equal("TASA-11", fila.Tipologia);
        Assert.Equal("no es título", fila.HuboIVA);
        Assert.Equal("no", fila.EsTituloPrevio);
    }

    [Fact]
    public async Task RespuestaNoParseable_GuardaElTextoBruto()
    {
        var backend = new FakeIngestBackend { OnStatus = _ => FakeIngestBackend.CompletedStatus("OK", "ESCR-10", "bla") };
        var fila = await Procesar(Crear(backend), "ESCR-10");

        Assert.Equal(EstadosProcesado.NoParseable, fila.Procesado);
        Assert.Equal("bla", fila.Evidencia);
    }

    [Fact]
    public async Task EstadoSinContenido_MarcaSinContenido()
    {
        var backend = new FakeIngestBackend { OnStatus = _ => FakeIngestBackend.CompletedStatus("SIN_CONTENIDO", "ESCR-10", null) };
        var fila = await Procesar(Crear(backend), "ESCR-10");

        Assert.Equal(EstadosProcesado.SinContenido, fila.Procesado);
    }

    [Fact]
    public async Task EstadoErrorDelContrato_ReintentaYMarcaError()
    {
        var backend = new FakeIngestBackend { OnStatus = _ => FakeIngestBackend.CompletedStatus("ERROR", "ESCR-10", null) };
        var fila = await Procesar(Crear(backend, new IvaConfig { MaxReintentos = 2 }), "ESCR-10");

        Assert.Equal(3, backend.Requests.Count);
        Assert.Equal(EstadosProcesado.Error, fila.Procesado);
        Assert.NotEmpty(fila.Error);
    }

    [Fact]
    public async Task ExcepcionDelBackend_ReintentaYMarcaError()
    {
        var backend = new FakeIngestBackend { OnIngest = _ => throw new HttpRequestException("caído") };
        var fila = await Procesar(Crear(backend, new IvaConfig { MaxReintentos = 2 }), "ESCR-10");

        Assert.Equal(3, backend.Requests.Count);
        Assert.Equal(EstadosProcesado.Error, fila.Procesado);
        Assert.Contains("caído", fila.Error);
    }

    [Fact]
    public async Task RuntimeFailed_MarcaError()
    {
        var backend = new FakeIngestBackend { OnStatus = _ => FakeIngestBackend.Runtime("Failed") };
        var fila = await Procesar(Crear(backend), "ESCR-10");

        Assert.Equal(EstadosProcesado.Error, fila.Procesado);
    }

    [Fact]
    public async Task RunningSinFin_MarcaTimeout()
    {
        var backend = new FakeIngestBackend { OnStatus = _ => FakeIngestBackend.Runtime("Running") };
        var fila = await Procesar(Crear(backend, new IvaConfig { TimeoutMinutos = 0 }), "ESCR-10");

        Assert.Equal(EstadosProcesado.Timeout, fila.Procesado);
        Assert.Single(backend.Requests);
    }
}
