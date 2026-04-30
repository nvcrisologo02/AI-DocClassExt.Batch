using System.Globalization;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.Services;

public static class BatchExportRows
{
    public static readonly IReadOnlyList<string> Headers = new[]
    {
        "NombreArchivo",
        "RutaCompleta",
        "TamanoBytes",
        "Estado",
        "RuntimeStatus",
        "EstadoCalidad",
        "ConfianzaGlobal",
        "ConfianzaPorcentaje",
        "DuracionSegundos",
        "Duracion",
        "FechaInicio",
        "FechaFin",
        "CorrelationId",
        "InstanceId",
        "OutputJsonPath",
        "MensajeError",
        "Tipologia",
        "NumeroColas",
        "UmbralConfianza",
        "SubirAGdc",
        "EjecutarConAssetResolver"
    };

    public static IEnumerable<IReadOnlyList<string>> BuildRows(
        IEnumerable<BatchFileItem> files,
        string tipologia,
        int numeroColas,
        int umbralConfianza,
        bool subirAGdc,
        bool ejecutarConAssetResolver)
    {
        foreach (var file in files)
        {
            yield return BuildRow(
                file,
                tipologia,
                numeroColas,
                umbralConfianza,
                subirAGdc,
                ejecutarConAssetResolver).ToArray();
        }
    }

    private static IEnumerable<string> BuildRow(
        BatchFileItem file,
        string tipologia,
        int numeroColas,
        int umbralConfianza,
        bool subirAGdc,
        bool ejecutarConAssetResolver)
    {
        var duration = file.FechaInicio.HasValue && file.FechaFin.HasValue
            ? file.FechaFin.Value - file.FechaInicio.Value
            : (TimeSpan?)null;

        yield return file.FileName;
        yield return file.FullPath;
        yield return file.SizeBytes.ToString(CultureInfo.InvariantCulture);
        yield return file.Estado;
        yield return file.RuntimeStatus;
        yield return file.EstadoCalidad;
        yield return file.ConfianzaGlobal?.ToString("0.########", CultureInfo.InvariantCulture) ?? string.Empty;
        yield return file.ConfidenceDisplay;
        yield return duration?.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
        yield return file.DurationDisplay;
        yield return FormatDate(file.FechaInicio);
        yield return FormatDate(file.FechaFin);
        yield return file.CorrelationId;
        yield return file.InstanceId;
        yield return file.OutputJsonPath;
        yield return file.MensajeError;
        yield return tipologia;
        yield return numeroColas.ToString(CultureInfo.InvariantCulture);
        yield return umbralConfianza.ToString(CultureInfo.InvariantCulture);
        yield return subirAGdc ? "true" : "false";
        yield return ejecutarConAssetResolver ? "true" : "false";
    }

    private static string FormatDate(DateTime? value)
    {
        return value?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
