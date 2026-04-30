using System.Text.Json;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.Services;

public static class BatchExportRows
{
    private static readonly string[] PrefixHeaders =
    {
        "Identificacion.Documento",
        "Identificacion.Guid",
        "Identificacion.Tipologia",
        "Identificacion.TipologiaFamilia",
        "Identificacion.TipologiaVersion",
        "Identificacion.FechaProceso",
        "Integridad.CRC32",
        "Integridad.SHA256",
        "Integridad.MD5",
        "Integridad.IdActivo",
        "Integridad.IdActivoEntrada",
        "Integridad.IdActivoCambiado"
    };

    private static readonly string[] SuffixHeaders =
    {
        "DetalleEjecucion.Extraccion.Modelo",
        "DetalleEjecucion.Extraccion.CamposConDuda",
        "DetalleEjecucion.AssetResolver.ActivosAAII",
        "DetalleEjecucion.AssetResolver.ActivosAACC",
        "DetalleEjecucion.AssetResolver.Mensaje",
        "DetalleEjecucion.Prompt",
        "Resultado.Estado",
        "Resultado.MensajeError",
        "Resultado.ConfianzaGlobal",
        "Resultado.EstadoCalidad",
        "Resultado.ConfianzaClasificacion",
        "Resultado.ConfianzaExtraccion",
        "Resultado.ConfianzaValidacion",
        "Resultado.ReutilizadaPorDuplicado",
        "Resultado.MensajeReutilizacion"
    };

    public static BatchExportTable BuildTable(
        IEnumerable<BatchFileItem> files,
        string tipologia,
        int numeroColas,
        int umbralConfianza,
        bool subirAGdc,
        bool ejecutarConAssetResolver)
    {
        var documents = files
            .Select(file => new BatchExportDocument(file, BatchOutputJsonReader.LoadDocument(file.OutputJsonPath)))
            .ToList();

        var datosExtraidosFields = DiscoverDatosExtraidosFields(documents);
        var headers = BuildHeaders(datosExtraidosFields);
        var rows = documents
            .Select(document => BuildRow(document, headers))
            .ToList();

        foreach (var document in documents)
        {
            document.Output?.Dispose();
        }

        return new BatchExportTable(headers, rows);
    }

    private static IReadOnlyList<string> BuildHeaders(IReadOnlyList<string> datosExtraidosFields)
    {
        var headers = new List<string>();
        headers.AddRange(PrefixHeaders);

        foreach (var field in datosExtraidosFields)
        {
            headers.Add($"DatosExtraidos.{field}");
            headers.Add($"DetalleEjecucion.Extraccion.ConfianzaPorCampo.{field}");
        }

        headers.AddRange(SuffixHeaders);
        return headers;
    }

    private static IReadOnlyList<string> DiscoverDatosExtraidosFields(IReadOnlyList<BatchExportDocument> documents)
    {
        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            if (document.Output is null)
            {
                continue;
            }

            foreach (var field in BatchOutputJsonReader.GetObjectPropertyNames(document.Output.RootElement, "DatosExtraidos"))
            {
                if (seen.Add(field))
                {
                    fields.Add(field);
                }
            }
        }

        return fields;
    }

    private static IReadOnlyList<string> BuildRow(BatchExportDocument document, IReadOnlyList<string> headers)
    {
        if (document.Output is null)
        {
            return headers.Select(_ => string.Empty).ToArray();
        }

        var root = document.Output.RootElement;
        return headers
            .Select(header => BatchOutputJsonReader.GetPathValue(root, header))
            .ToArray();
    }

    private sealed record BatchExportDocument(BatchFileItem File, JsonDocument? Output);
}

public sealed record BatchExportTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);
