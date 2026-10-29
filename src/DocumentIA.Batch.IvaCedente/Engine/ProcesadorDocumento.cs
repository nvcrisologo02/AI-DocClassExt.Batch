using System.Diagnostics;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.IvaCedente.Models;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.IvaCedente.Engine;

/// <summary>Procesa un documento: una o dos llamadas a DocumentIA según su TDN2, con reintentos y timeout.</summary>
public sealed class ProcesadorDocumento
{
    private const string Si = "sí";
    private const string No = "no";
    private const int MaxFallosConsulta = 5;

    private readonly IIngestBackend _backend;
    private readonly IvaConfig _config;
    private readonly Prompts _prompts;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly AdaptivePollingStrategy _polling;

    public ProcesadorDocumento(IIngestBackend backend, IvaConfig config, Prompts prompts,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _backend = backend;
        _config = config;
        _prompts = prompts;
        _delay = delay ?? Task.Delay;
        _polling = new AdaptivePollingStrategy(config.PollingIntervalSeconds, config.TimeoutMinutos);
    }

    public async Task<FilaDocumento> ProcesarAsync(FilaEntrada fila, CancellationToken ct)
    {
        var resultado = new FilaDocumento { IdSareb = fila.IdSareb, ObjectId = fila.ObjectId, Tdn2 = fila.Tdn2 };
        await ProcesarInternoAsync(fila, resultado, ct);
        resultado.Fecha = DateTime.UtcNow;
        return resultado;
    }

    private async Task ProcesarInternoAsync(FilaEntrada fila, FilaDocumento resultado, CancellationToken ct)
    {
        var camino = SelectorCamino.Decidir(fila.Tdn2, _config);
        if (camino == Camino.NoAplica)
        {
            resultado.Procesado = EstadosProcesado.NoAplica;
            return;
        }

        string? tdn2Clasificado = null;
        if (camino == Camino.DosLlamadas)
        {
            var clasificacion = await EjecutarPasoAsync(resultado, id => ConstructorPeticiones.Clasificacion(fila, _config, id), ct);
            if (clasificacion.Estado != EstadoPaso.Ok)
            {
                AplicarFallo(resultado, clasificacion);
                return;
            }

            if (ParserSalida.EsSinContenido(clasificacion.Salida!.EstadoContrato))
            {
                resultado.Procesado = EstadosProcesado.SinContenido;
                return;
            }

            tdn2Clasificado = clasificacion.Salida.Tdn2;
            resultado.Tipologia = tdn2Clasificado ?? string.Empty;
            if (SelectorCamino.Decidir(tdn2Clasificado ?? string.Empty, _config) != Camino.UnaLlamada)
            {
                resultado.Procesado = EstadosProcesado.NoEsTitulo;
                resultado.EsTituloPrevio = No;
                resultado.HuboIVA = ValoresIva.NoEsTitulo;
                resultado.DeduccionIVA = ValoresIva.NoAplica;
                return;
            }
        }

        var extraccion = await EjecutarPasoAsync(resultado, id => ConstructorPeticiones.ConPrompt(fila, _config, _prompts, id), ct);
        if (extraccion.Estado != EstadoPaso.Ok)
        {
            AplicarFallo(resultado, extraccion);
            return;
        }

        var salida = extraccion.Salida!;
        resultado.Tipologia = !string.IsNullOrWhiteSpace(salida.Tdn2) ? salida.Tdn2! : tdn2Clasificado ?? fila.Tdn2;

        if (ParserSalida.EsSinContenido(salida.EstadoContrato))
        {
            resultado.Procesado = EstadosProcesado.SinContenido;
            return;
        }

        if (!string.IsNullOrWhiteSpace(salida.EstadoContrato)
            && !string.Equals(salida.EstadoContrato, "OK", StringComparison.OrdinalIgnoreCase))
        {
            resultado.Error = $"Estado del contrato: {salida.EstadoContrato}";
        }

        var respuesta = ParserSalida.ParsearRespuesta(salida.ResultadoPrompt);
        if (respuesta is null)
        {
            resultado.Procesado = EstadosProcesado.NoParseable;
            resultado.Evidencia = salida.ResultadoPrompt ?? string.Empty;
            return;
        }

        resultado.Procesado = EstadosProcesado.Ok;
        resultado.EsTituloPrevio = respuesta.EsTituloPrevio ? Si : No;
        resultado.Confianza = respuesta.Confianza;
        resultado.Evidencia = respuesta.Evidencia ?? string.Empty;
        if (respuesta.EsTituloPrevio)
        {
            resultado.HuboIVA = respuesta.HuboIVA;
            resultado.DeduccionIVA = respuesta.DeduccionIVA;
            resultado.Cedente = respuesta.Cedente ?? string.Empty;
            resultado.FechaAdquisicion = respuesta.FechaAdquisicion ?? string.Empty;
            resultado.TipoIVA = respuesta.TipoIVA;
            resultado.CuotaIVA = respuesta.CuotaIVA;
        }
        else
        {
            resultado.HuboIVA = ValoresIva.NoEsTitulo;
            resultado.DeduccionIVA = ValoresIva.NoAplica;
        }
    }

    private static void AplicarFallo(FilaDocumento resultado, ResultadoPaso paso)
    {
        resultado.Procesado = paso.Estado == EstadoPaso.Timeout ? EstadosProcesado.Timeout : EstadosProcesado.Error;
        resultado.Error = paso.Mensaje;
    }

    /// <summary>Ingest y polling de una petición; reintenta el paso completo ante excepción, fallo de runtime o estado ERROR.</summary>
    private async Task<ResultadoPaso> EjecutarPasoAsync(FilaDocumento fila, Func<string, IngestRequest> construir, CancellationToken ct)
    {
        var ultimo = new ResultadoPaso(EstadoPaso.Error, null, "Sin intentos.");
        for (var intento = 0; intento <= Math.Max(0, _config.MaxReintentos); intento++)
        {
            ct.ThrowIfCancellationRequested();
            var correlationId = Guid.NewGuid().ToString();
            fila.CorrelationId = correlationId;
            try
            {
                var respuesta = await _backend.IngestAsync(construir(correlationId), ct);
                fila.InstanceId = respuesta.InstanceId;
                ultimo = await EsperarResultadoAsync(respuesta.StatusQueryUri, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ultimo = new ResultadoPaso(EstadoPaso.Error, null, ex.Message);
            }

            if (ultimo.Estado != EstadoPaso.Error) return ultimo;

            if (intento < Math.Max(0, _config.MaxReintentos))
            {
                // Espera creciente antes de volver a enviar el paso a PRO.
                await _delay(TimeSpan.FromSeconds(_config.PollingIntervalSeconds * (intento + 1)), ct);
            }
        }
        return ultimo;
    }

    private async Task<ResultadoPaso> EsperarResultadoAsync(string statusQueryUri, CancellationToken ct)
    {
        var reloj = Stopwatch.StartNew();
        var fallosConsecutivos = 0;
        for (var intento = 0; ; intento++)
        {
            if (_polling.IsTimedOut(reloj.Elapsed))
            {
                return new ResultadoPaso(EstadoPaso.Timeout, null, $"Timeout esperando el resultado ({reloj.Elapsed.TotalMinutes:F0} min).");
            }

            await _delay(_polling.GetDelay(intento), ct);
            DurableStatusResponse estado;
            try
            {
                estado = await _backend.GetStatusAsync(statusQueryUri, ct);
                fallosConsecutivos = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Fallo transitorio de la consulta: se repite la consulta, no el ingest.
                if (++fallosConsecutivos >= MaxFallosConsulta)
                {
                    return new ResultadoPaso(EstadoPaso.Error, null, $"Fallo repetido al consultar el estado: {ex.Message}");
                }

                continue;
            }

            if (string.Equals(estado.RuntimeStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                if (!estado.Output.HasValue)
                {
                    return new ResultadoPaso(EstadoPaso.Error, null, "Orquestación completada sin output.");
                }

                var salida = ParserSalida.Leer(estado.Output.Value);
                return LiteResultParser.IsRetryableBackendFailure(salida.EstadoContrato)
                    ? new ResultadoPaso(EstadoPaso.Error, salida, $"DocumentIA devolvió el estado {salida.EstadoContrato}.")
                    : new ResultadoPaso(EstadoPaso.Ok, salida, string.Empty);
            }

            if (string.Equals(estado.RuntimeStatus, "Failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(estado.RuntimeStatus, "Terminated", StringComparison.OrdinalIgnoreCase))
            {
                return new ResultadoPaso(EstadoPaso.Error, null, $"Orquestación terminada con estado {estado.RuntimeStatus}.");
            }
        }
    }

    private enum EstadoPaso { Ok, Error, Timeout }

    private sealed record ResultadoPaso(EstadoPaso Estado, SalidaDocumentIa? Salida, string Mensaje);
}
