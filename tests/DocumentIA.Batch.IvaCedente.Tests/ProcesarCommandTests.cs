using Xunit;
using DocumentIA.Batch.IvaCedente.Commands;
using DocumentIA.Batch.IvaCedente.Io;
using DocumentIA.Batch.IvaCedente.Models;
using DocumentIA.Batch.IvaCedente.Tests.Fakes;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class ProcesarCommandTests : IDisposable
{
    private const string JsonSi = "{\"esTituloPrevio\":true,\"huboIVA\":\"sí\",\"deduccionIVA\":\"sí\",\"cedente\":\"Banco X\",\"fechaAdquisicion\":\"2008-05-12\",\"evidencia\":\"ev\",\"confianza\":0.9}";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "iva-procesar-" + Guid.NewGuid().ToString("N"));
    private readonly string _entrada;
    private readonly string _salida;

    public ProcesarCommandTests()
    {
        Directory.CreateDirectory(_dir);
        _entrada = Path.Combine(_dir, "entrada.csv");
        _salida = Path.Combine(_dir, "salida.csv");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private void EscribirEntrada(params (string Id, string Obj, string Tdn2)[] filas)
    {
        var lineas = new List<string> { "ID_SAREB;Object ID del Documento;TDN2" };
        lineas.AddRange(filas.Select(f => $"{f.Id};{f.Obj};{f.Tdn2}"));
        File.WriteAllLines(_entrada, lineas, new System.Text.UTF8Encoding(true));
    }

    private static IvaConfig Config(int colas = 3) => new() { Colas = colas, PollingIntervalSeconds = 1, MaxReintentos = 0 };

    private static FakeIngestBackend BackendOk() => new()
    {
        OnStatus = _ => FakeIngestBackend.CompletedStatus("OK", "ESCR-10", JsonSi)
    };

    private Task<ResumenProceso> Ejecutar(FakeIngestBackend backend, IvaConfig config, bool reintentar = false, CancellationToken ct = default)
        => ProcesarCommand.EjecutarAsync(_entrada, _salida, config, new Prompts("s", "u"), backend, reintentar,
            TextWriter.Null, ct, (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Ejecutar_EscribeUnaFilaPorEntradaYDevuelveResumen()
    {
        EscribirEntrada(("A1", "O1", "ESCR-10"), ("A2", "O2", "TASA-11"), ("A3", "O3", "ESCR-10"));
        var backend = BackendOk();

        var resumen = await Ejecutar(backend, Config());

        var filas = SalidaDocumentosCsv.Leer(_salida);
        Assert.Equal(new[] { "A1", "A2", "A3" }, filas.Select(f => f.IdSareb));
        Assert.Equal(3, resumen.Total);
        Assert.Equal(0, resumen.Omitidas);
        Assert.Equal(3, resumen.Procesadas);
        Assert.Equal(2, resumen.Ok);
        Assert.Equal(1, resumen.NoAplica);
        Assert.Equal(2, backend.Requests.Count);
    }

    [Fact]
    public async Task Ejecutar_MismoObjectIdEnTresActivos_UnaLlamadaTresFilas()
    {
        EscribirEntrada(("79044", "107736649", "ESCR-10"), ("97061", "107736649", "ESCR-10"), ("97553", "107736649", "ESCR-10"));
        var backend = BackendOk();

        var resumen = await Ejecutar(backend, Config());

        Assert.Single(backend.Requests);
        var filas = SalidaDocumentosCsv.Leer(_salida);
        Assert.Equal(3, filas.Count);
        Assert.All(filas, f => Assert.Equal(EstadosProcesado.Ok, f.Procesado));
        Assert.Single(filas.Select(f => f.InstanceId).Distinct());
        Assert.Equal(new[] { "79044", "97061", "97553" }, filas.Select(f => f.IdSareb));
        Assert.Equal(3, resumen.Ok);
    }

    [Fact]
    public async Task Ejecutar_Relanzado_SoloProcesaPendientes()
    {
        EscribirEntrada(("A1", "A", "ESCR-10"), ("A2", "B", "ESCR-10"), ("A3", "C", "ESCR-10"));
        var backend = BackendOk();
        backend.OnStatus = uri => uri.EndsWith("/B")
            ? FakeIngestBackend.Runtime("Failed")
            : FakeIngestBackend.CompletedStatus("OK", "ESCR-10", JsonSi);
        var primera = await Ejecutar(backend, Config());
        Assert.Equal(1, primera.Errores);

        var segundaBackend = BackendOk();
        var segunda = await Ejecutar(segundaBackend, Config());
        Assert.Empty(segundaBackend.Requests);
        Assert.Equal(3, segunda.Omitidas);

        var terceraBackend = BackendOk();
        var tercera = await Ejecutar(terceraBackend, Config(), reintentar: true);
        Assert.Single(terceraBackend.Requests);
        Assert.Equal(2, tercera.Omitidas);
        Assert.All(SalidaDocumentosCsv.Leer(_salida), f => Assert.Equal(EstadosProcesado.Ok, f.Procesado));
    }

    [Fact]
    public async Task Ejecutar_Cancelado_ConservaLoAnexadoYDejaPendientes()
    {
        EscribirEntrada(("A1", "A", "ESCR-10"), ("A2", "B", "ESCR-10"), ("A3", "C", "ESCR-10"));
        using var cts = new CancellationTokenSource();
        var backend = BackendOk();
        var llamadas = 0;
        backend.OnIngest = request =>
        {
            if (++llamadas == 2)
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }

            return new IngestResponse { InstanceId = "i-" + request.Documento.ObjectIdGDC, StatusQueryUri = "u/" + request.Documento.ObjectIdGDC };
        };

        var resumen = await Ejecutar(backend, Config(colas: 1), ct: cts.Token);

        var filas = SalidaDocumentosCsv.Leer(_salida);
        Assert.Equal(3, filas.Count);
        Assert.Equal(EstadosProcesado.Ok, filas[0].Procesado);
        Assert.Equal(EstadosProcesado.Pendiente, filas[1].Procesado);
        Assert.Equal(EstadosProcesado.Pendiente, filas[2].Procesado);
        Assert.Equal(1, resumen.Procesadas);

        var segundaBackend = BackendOk();
        await Ejecutar(segundaBackend, Config(colas: 1));
        Assert.Equal(2, segundaBackend.Requests.Count);
        Assert.All(SalidaDocumentosCsv.Leer(_salida), f => Assert.Equal(EstadosProcesado.Ok, f.Procesado));
    }
    [Fact]
    public async Task Ejecutar_SalidaBloqueadaPorExcel_ReintentaYNoPierdeLaFila()
    {
        EscribirEntrada(("A1", "A", "ESCR-10"));
        FileStream? bloqueo = null;
        var backend = BackendOk();
        backend.OnStatus = _ =>
        {
            bloqueo ??= new FileStream(_salida, FileMode.Create, FileAccess.Write, FileShare.Read);
            return FakeIngestBackend.CompletedStatus("OK", "ESCR-10", JsonSi);
        };
        var esperas = 0;

        var resumen = await ProcesarCommand.EjecutarAsync(_entrada, _salida, Config(), new Prompts("s", "u"), backend, false,
            TextWriter.Null, CancellationToken.None, (_, _) =>
            {
                if (bloqueo is not null)
                {
                    esperas++;
                    bloqueo.Dispose();
                    bloqueo = null;
                }

                return Task.CompletedTask;
            });

        Assert.Equal(1, esperas);
        Assert.Equal(1, resumen.Ok);
        var filas = SalidaDocumentosCsv.Leer(_salida);
        Assert.Equal(EstadosProcesado.Ok, Assert.Single(filas).Procesado);
    }

    [Fact]
    public async Task Ejecutar_SalidaBloqueadaParaSiempre_AgotaReintentosYFallaSinTraza()
    {
        EscribirEntrada(("A1", "A", "ESCR-10"), ("A2", "B", "ESCR-10"));
        FileStream? bloqueo = null;
        var backend = BackendOk();
        backend.OnStatus = _ =>
        {
            bloqueo ??= new FileStream(_salida, FileMode.Create, FileAccess.Write, FileShare.Read);
            return FakeIngestBackend.CompletedStatus("OK", "ESCR-10", JsonSi);
        };

        var ex = await Assert.ThrowsAsync<IvaCedenteUsageException>(() =>
            ProcesarCommand.EjecutarAsync(_entrada, _salida, Config(colas: 1), new Prompts("s", "u"), backend, false,
                TextWriter.Null, CancellationToken.None, (_, _) => Task.CompletedTask));

        bloqueo?.Dispose();
        Assert.Contains("salida.csv", ex.Message);
        // La pasada se cancela tras agotar el anexado: no se factura el segundo documento.
        Assert.Single(backend.Requests);
    }
    [Theory]
    [InlineData(new[] { "--tipologia", "resumen.documental" }, "resumen.documental")]
    [InlineData(new[] { "--tipologia", "  " }, null)]
    [InlineData(new string[0], null)]
    public void AplicarOpciones_Tipologia(string[] args, string? esperado)
    {
        var config = new IvaConfig();
        ProcesarCommand.AplicarOpciones(CliArgs.Parse(args), config);
        Assert.Equal(esperado, config.TipologiaForzada);
    }

    [Fact]
    public void AplicarOpciones_TipologiaDeConfigSeMantieneSinOpcion()
    {
        var config = new IvaConfig { TipologiaForzada = "resumen.documental" };
        ProcesarCommand.AplicarOpciones(CliArgs.Parse(new[] { "--colas", "2" }), config);
        Assert.Equal("resumen.documental", config.TipologiaForzada);
        Assert.Equal(2, config.Colas);
    }
}
