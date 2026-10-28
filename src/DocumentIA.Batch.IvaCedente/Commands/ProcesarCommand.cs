using System.Diagnostics;
using System.Globalization;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.IvaCedente.Engine;
using DocumentIA.Batch.IvaCedente.Io;
using DocumentIA.Batch.IvaCedente.Models;
using DocumentIA.Batch.IvaCedente.Services;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.IvaCedente.Commands;

/// <summary>Resumen de una pasada: cuenta por fila de salida generada en esta pasada, no por documento.</summary>
public sealed record ResumenProceso(int Total, int Omitidas, int Procesadas, int Ok, int NoAplica, int NoEsTitulo, int SinContenido, int NoParseable, int Errores, int Timeouts);

/// <summary>Comando procesar: orquesta el lote con colas limitadas, una llamada por Object ID y reanudación.</summary>
public static class ProcesarCommand
{
    private const int DocumentosPorAvance = 10;

    public static async Task<int> ExecuteAsync(string[] args)
    {
        var cli = CliArgs.Parse(args);
        var entrada = cli.Require("entrada");
        var salida = cli.Require("salida");
        var configPath = cli.GetOrDefault("config", Path.Combine(AppContext.BaseDirectory, "config.json"));
        var reintentarErrores = cli.Has("reintentar-errores");

        var config = IvaConfigLoader.Cargar(configPath);
        if (cli.Has("colas"))
        {
            config.Colas = cli.GetIntOrDefault("colas", config.Colas);
        }

        if (config.Colas < 1)
        {
            throw new IvaCedenteUsageException("El número de colas debe ser al menos 1.");
        }

        var entorno = IvaConfigLoader.EntornoSeleccionado(config);
        var prompts = IvaConfigLoader.CargarPrompts(config, AppContext.BaseDirectory);
        var backend = new IngestBackendAdapter(new DocumentIaBackendClient(), entorno);

        var filasEntrada = EntradaCsv.Leer(entrada);
        var pendientes = Reanudacion.Pendientes(filasEntrada, SalidaDocumentosCsv.Leer(salida), reintentarErrores);
        Console.WriteLine($"Entorno: {entorno.Name}");
        Console.WriteLine($"URL: {entorno.BackendUrl}");
        Console.WriteLine($"Colas: {config.Colas}");
        Console.WriteLine($"Entrada: {entrada}");
        Console.WriteLine($"Salida: {salida}");
        Console.WriteLine($"Filas pendientes: {pendientes.Count} de {filasEntrada.Count}");

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler manejador = (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("Cancelando: se espera a escribir lo ya procesado...");
            cts.Cancel();
        };
        Console.CancelKeyPress += manejador;
        try
        {
            var resumen = await EjecutarAsync(entrada, salida, config, prompts, backend, reintentarErrores, Console.Out, cts.Token);
            if (cts.IsCancellationRequested)
            {
                Console.WriteLine("Proceso cancelado. Relanza el mismo comando para continuar con lo pendiente.");
            }

            return resumen.Errores == 0 && resumen.Timeouts == 0 ? 0 : 2;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.WriteLine("Proceso cancelado. Relanza el mismo comando para continuar con lo pendiente.");
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= manejador;
        }
    }

    public static async Task<ResumenProceso> EjecutarAsync(string entradaPath, string salidaPath, IvaConfig config, Prompts prompts,
        IIngestBackend backend, bool reintentarErrores, TextWriter consola, CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var entrada = EntradaCsv.Leer(entradaPath);
        var previas = SalidaDocumentosCsv.Leer(salidaPath);
        var pendientes = Reanudacion.Pendientes(entrada, previas, reintentarErrores);
        var grupos = pendientes.GroupBy(f => f.ObjectId).Select(g => g.ToList()).ToList();

        var procesador = new ProcesadorDocumento(backend, config, prompts, delay);
        var nuevas = new List<FilaDocumento>();
        var bloqueo = new object();
        var documentosHechos = 0;
        var reloj = Stopwatch.StartNew();
        using var semaforo = new SemaphoreSlim(Math.Max(1, config.Colas));

        async Task ProcesarGrupoAsync(List<FilaEntrada> grupo)
        {
            try
            {
                await semaforo.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var primera = await procesador.ProcesarAsync(grupo[0], ct);
                var filas = new List<FilaDocumento> { primera };
                filas.AddRange(grupo.Skip(1).Select(e => primera.ParaActivo(e.IdSareb, e.Tdn2)));

                lock (bloqueo)
                {
                    foreach (var fila in filas)
                    {
                        SalidaDocumentosCsv.Anexar(salidaPath, fila);
                        nuevas.Add(fila);
                    }

                    documentosHechos++;
                    if (documentosHechos % DocumentosPorAvance == 0)
                    {
                        consola.WriteLine(LineaAvance(documentosHechos, grupos.Count, nuevas, reloj.Elapsed));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Documento en vuelo cancelado: no se anexa y queda pendiente.
            }
            finally
            {
                semaforo.Release();
            }
        }

        await Task.WhenAll(grupos.Select(ProcesarGrupoAsync));

        lock (bloqueo)
        {
            if (documentosHechos % DocumentosPorAvance != 0 || documentosHechos == 0)
            {
                consola.WriteLine(LineaAvance(documentosHechos, grupos.Count, nuevas, reloj.Elapsed));
            }
        }

        SalidaDocumentosCsv.Escribir(salidaPath, Reanudacion.Fusionar(entrada, previas, nuevas));
        return Resumir(entrada.Count, entrada.Count - pendientes.Count, nuevas);
    }

    private static string LineaAvance(int hechos, int totalDocumentos, List<FilaDocumento> nuevas, TimeSpan transcurrido)
    {
        var media = hechos == 0 ? 0 : transcurrido.TotalSeconds / hechos;
        return string.Create(CultureInfo.InvariantCulture,
            $"[{hechos}/{totalDocumentos}] OK={Contar(nuevas, EstadosProcesado.Ok)} noAplica={Contar(nuevas, EstadosProcesado.NoAplica)} noTitulo={Contar(nuevas, EstadosProcesado.NoEsTitulo)} errores={Contar(nuevas, EstadosProcesado.Error) + Contar(nuevas, EstadosProcesado.Timeout)} media={media:F1} s");
    }

    private static int Contar(List<FilaDocumento> filas, string estado) => filas.Count(f => f.Procesado == estado);

    private static ResumenProceso Resumir(int total, int omitidas, List<FilaDocumento> nuevas) => new(
        total, omitidas, nuevas.Count,
        Contar(nuevas, EstadosProcesado.Ok), Contar(nuevas, EstadosProcesado.NoAplica), Contar(nuevas, EstadosProcesado.NoEsTitulo),
        Contar(nuevas, EstadosProcesado.SinContenido), Contar(nuevas, EstadosProcesado.NoParseable),
        Contar(nuevas, EstadosProcesado.Error), Contar(nuevas, EstadosProcesado.Timeout));
}
