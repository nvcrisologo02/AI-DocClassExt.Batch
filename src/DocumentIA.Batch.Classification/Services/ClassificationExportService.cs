using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using DocumentIA.Batch.Classification.Models;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.Classification.Services;

public class ClassificationExportService
{
    private static readonly string[] Headers =
    {
        "FileName",
        "Status",
        "Typology",
        "TipologiaFamilia",
        "TipologiaVersion",
        "FechaProceso",
        "PaginasIncluidas",
        "Paginas",
        "Tdn1",
        "Tdn2",
        "Matricula",
        "Clasificador",
        "Confidence",
        "FallbackLLM",
        "DuracionTotalMs",
        "DetalleProveedores.Reglas.Tipologia",
        "DetalleProveedores.Reglas.Confianza",
        "DetalleProveedores.Reglas.MotivoDescarte",
        "DetalleProveedores.DI.Tipologia",
        "DetalleProveedores.DI.Confianza",
        "DetalleProveedores.DI.MotivoDescarte",
        "DetalleProveedores.FoundryRescue.Tipologia",
        "DetalleProveedores.FoundryRescue.Confianza",
        "DetalleProveedores.FoundryRescue.MotivoDescarte",
        "ReutilizadaPorDuplicado"
    };

    public void ExportCsv(string filePath, IEnumerable<ClassificationDocumentItem> items)
    {
        var rows = BuildRows(items);
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(';', Headers.Select(Escape)));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(';', row.ToValues().Select(Escape)));
        }

        File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public void ExportExcel(string filePath, IEnumerable<ClassificationDocumentItem> items)
    {
        var rows = BuildRows(items).ToList();

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteTextEntry(archive, "[Content_Types].xml", BuildContentTypesXml());
        WriteTextEntry(archive, "_rels/.rels", BuildRootRelationshipsXml());
        WriteTextEntry(archive, "xl/workbook.xml", BuildWorkbookXml());
        WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsXml());
        WriteWorksheet(archive, rows);
    }

    public void ExportSimplifiedExcel(string filePath, IEnumerable<ClassificationDocumentItem> items)
    {
        var rows = BuildSimplifiedRows(items).ToList();

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteTextEntry(archive, "[Content_Types].xml", BuildContentTypesXml());
        WriteTextEntry(archive, "_rels/.rels", BuildRootRelationshipsXml());
        WriteTextEntry(archive, "xl/workbook.xml", BuildWorkbookXml());
        WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsXml());
        WriteSimplifiedWorksheet(archive, rows);
    }

    private static IEnumerable<ClassificationExportRow> BuildRows(IEnumerable<ClassificationDocumentItem> items)
    {
        var extractor = new BatchOutputAuditExtractor();

        foreach (var item in items)
        {
            var status = ChooseFirst(item.ResultadoEstado, item.Status);
            var typology = item.TipologiaIdentificada;
            var confidenceRaw = item.ConfianzaGlobal;
            var providerDetails = item.DetalleProveedores?.ToList() ?? [];
            var tipologiaFamilia = item.TipologiaFamilia ?? string.Empty;
            var tipologiaVersion = item.TipologiaVersion ?? string.Empty;
            var fechaProceso = item.FechaProceso ?? string.Empty;
            var paginasIncluidas = item.PaginasIncluidas ?? string.Empty;
            var paginas = item.Paginas ?? string.Empty;
            var tdn1 = item.Tdn1 ?? string.Empty;
            var tdn2 = item.Tdn2 ?? string.Empty;
            var matricula = item.Matricula ?? string.Empty;
            var clasificador = item.Clasificador ?? string.Empty;
            var fallbackLlm = item.FallbackLlm ?? string.Empty;
            var duracionTotalMs = item.DuracionTotalMs ?? string.Empty;
            var reutilizadaPorDuplicado = item.ReutilizadaPorDuplicado;

            if (!string.IsNullOrWhiteSpace(item.OutputJsonPath))
            {
                var audit = extractor.Extract(item.OutputJsonPath);

                status = ChooseFirst(status, audit.ResultadoEstadoCalidad);
                typology = ChooseFirst(typology, audit.IdentificacionTipologiaDetectada);
                confidenceRaw = ChooseFirst(confidenceRaw, audit.ResultadoConfianzaGlobal);
            }

            if (!string.IsNullOrWhiteSpace(item.OutputJsonPath)
                && TryReadOutputSnapshot(item.OutputJsonPath, out var snapshot))
            {
                status = ChooseFirst(status, snapshot.Status);
                typology = ChooseFirst(typology, snapshot.Typology);
                tipologiaFamilia = ChooseFirst(tipologiaFamilia, snapshot.TipologiaFamilia);
                tipologiaVersion = ChooseFirst(tipologiaVersion, snapshot.TipologiaVersion);
                fechaProceso = ChooseFirst(fechaProceso, snapshot.FechaProceso);
                paginasIncluidas = ChooseFirst(paginasIncluidas, snapshot.PaginasIncluidas);
                paginas = ChooseFirst(paginas, snapshot.Paginas);
                tdn1 = ChooseFirst(tdn1, snapshot.Tdn1);
                tdn2 = ChooseFirst(tdn2, snapshot.Tdn2);
                matricula = ChooseFirst(matricula, snapshot.Matricula);
                clasificador = ChooseFirst(clasificador, snapshot.Clasificador);
                confidenceRaw = ChooseFirst(confidenceRaw, snapshot.ConfidenceRaw);
                fallbackLlm = ChooseFirst(fallbackLlm, snapshot.FallbackLlm);
                duracionTotalMs = ChooseFirst(duracionTotalMs, snapshot.DuracionTotalMs);

                if (providerDetails.Count == 0 && snapshot.ProviderDetails.Count > 0)
                {
                    providerDetails = snapshot.ProviderDetails;
                }

                if (snapshot.ReutilizadaPorDuplicado.HasValue)
                {
                    reutilizadaPorDuplicado = snapshot.ReutilizadaPorDuplicado.Value;
                }
            }

            var reglas = GetProvider(providerDetails, "Reglas");
            var di = GetProvider(providerDetails, "DI", "DocumentIntelligence");
            var foundry = GetProvider(providerDetails, "FoundryRescue");

            yield return new ClassificationExportRow(
                item.FileName ?? string.Empty,
                status,
                typology,
                tipologiaFamilia,
                tipologiaVersion,
                fechaProceso,
                paginasIncluidas,
                paginas,
                tdn1,
                tdn2,
                matricula,
                clasificador,
                FormatConfidence(confidenceRaw),
                NormalizeBooleanLike(fallbackLlm),
                duracionTotalMs,
                reglas?.Tipologia ?? string.Empty,
                FormatProviderConfidence(reglas?.Confianza),
                reglas?.MotivoDescarte ?? string.Empty,
                di?.Tipologia ?? string.Empty,
                FormatProviderConfidence(di?.Confianza),
                di?.MotivoDescarte ?? string.Empty,
                foundry?.Tipologia ?? string.Empty,
                FormatProviderConfidence(foundry?.Confianza),
                foundry?.MotivoDescarte ?? string.Empty,
                reutilizadaPorDuplicado.ToString().ToLowerInvariant());
        }
    }

    private static void WriteWorksheet(ZipArchive archive, IReadOnlyList<ClassificationExportRow> rows)
    {
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false
        };

        using var writer = XmlWriter.Create(entry.Open(), settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        writer.WriteStartElement("sheetData");

        var rowNumber = 1;
        WriteRow(writer, rowNumber++, Headers);

        foreach (var row in rows)
        {
            WriteRow(writer, rowNumber++, row.ToValues());
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static IEnumerable<ClassificationSimplifiedExportRow> BuildSimplifiedRows(IEnumerable<ClassificationDocumentItem> items)
    {
        var extractor = new BatchOutputAuditExtractor();

        foreach (var item in items)
        {
            var fileName = item.FileName ?? string.Empty;
            var status = item.Status ?? string.Empty;
            var resultado = item.ResultadoEstado ?? string.Empty;
            var tipologia = item.TipologiaIdentificada ?? string.Empty;
            var paginasIncluidas = item.PaginasIncluidas ?? string.Empty;
            var paginas = item.Paginas ?? string.Empty;
            var tdn1 = item.Tdn1 ?? string.Empty;
            var tdn2 = item.Tdn2 ?? string.Empty;
            var matricula = item.Matricula ?? string.Empty;
            var clasificador = item.Clasificador ?? string.Empty;
            var duracionTotalMs = item.DuracionTotalMs ?? string.Empty;
            var resumen = item.Resumen ?? string.Empty;
            var tipologiaVirtual = item.TipologiaNombre ?? string.Empty;
            var esTipologiaVirtual = false;
            var confidenceRaw = item.ConfianzaGlobal ?? string.Empty;

            // Si existe OutputJsonPath, intentar extraer datos adicionales
            if (!string.IsNullOrWhiteSpace(item.OutputJsonPath))
            {
                var audit = extractor.Extract(item.OutputJsonPath);
                tipologia = ChooseFirst(tipologia, audit.IdentificacionTipologiaDetectada);
                confidenceRaw = ChooseFirst(confidenceRaw, audit.ResultadoConfianzaGlobal);

                // Intentar obtener resumen del snapshot
                if (TryReadOutputSnapshot(item.OutputJsonPath, out var snapshot))
                {
                    status = ChooseFirst(status, snapshot.Status);
                    resultado = ChooseFirst(resultado, snapshot.Status);
                    tipologia = ChooseFirst(tipologia, snapshot.Typology);
                    paginasIncluidas = ChooseFirst(paginasIncluidas, snapshot.PaginasIncluidas);
                    paginas = ChooseFirst(paginas, snapshot.Paginas);
                    tdn1 = ChooseFirst(tdn1, snapshot.Tdn1);
                    tdn2 = ChooseFirst(tdn2, snapshot.Tdn2);
                    matricula = ChooseFirst(matricula, snapshot.Matricula);
                    clasificador = ChooseFirst(clasificador, snapshot.Clasificador);
                    confidenceRaw = ChooseFirst(confidenceRaw, snapshot.ConfidenceRaw);
                    duracionTotalMs = ChooseFirst(duracionTotalMs, snapshot.DuracionTotalMs);
                    tipologiaVirtual = ChooseFirst(tipologiaVirtual, snapshot.TipologiaNombre);
                    esTipologiaVirtual = snapshot.EsTipologiaVirtual;

                    if (!string.IsNullOrWhiteSpace(snapshot.Resumen))
                    {
                        resumen = snapshot.Resumen;
                    }
                }
            }

            yield return new ClassificationSimplifiedExportRow(
                fileName,
                status,
                resultado,
                tipologia,
                paginasIncluidas,
                paginas,
                tdn1,
                tdn2,
                matricula,
                clasificador,
                FormatConfidence(confidenceRaw),
                duracionTotalMs,
                resumen,
                tipologiaVirtual,
                esTipologiaVirtual.ToString().ToLowerInvariant());
        }
    }

    private static void WriteSimplifiedWorksheet(ZipArchive archive, IReadOnlyList<ClassificationSimplifiedExportRow> rows)
    {
        var simplifiedHeaders = new[]
        {
            "Filename",
            "Status",
            "Rsultado",
            "Tipologia",
            "PaginasIncluidas",
            "Paginas",
            "Tdn1",
            "Tdn2",
            "Matricula",
            "Clasificador",
            "Confidence",
            "DuracionTotalMs",
            "Resumen",
            "TiplogiaVirtual",
            "EsTipologiaVirtual"
        };
        
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false
        };

        using var writer = XmlWriter.Create(entry.Open(), settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        writer.WriteStartElement("sheetData");

        var rowNumber = 1;
        WriteRow(writer, rowNumber++, simplifiedHeaders);

        foreach (var row in rows)
        {
            WriteRow(writer, rowNumber++, row.ToValues());
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteRow(XmlWriter writer, int rowNumber, IReadOnlyList<string> values)
    {
        writer.WriteStartElement("row");
        writer.WriteAttributeString("r", rowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));

        for (var columnIndex = 0; columnIndex < values.Count; columnIndex++)
        {
            writer.WriteStartElement("c");
            writer.WriteAttributeString("r", $"{GetColumnName(columnIndex + 1)}{rowNumber}");
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is");
            writer.WriteStartElement("t");
            writer.WriteAttributeString("xml", "space", null, "preserve");
            writer.WriteString(values[columnIndex] ?? string.Empty);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static string GetColumnName(int columnNumber)
    {
        var name = string.Empty;

        while (columnNumber > 0)
        {
            columnNumber--;
            name = (char)('A' + columnNumber % 26) + name;
            columnNumber /= 26;
        }

        return name;
    }

    private static void WriteTextEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string Escape(string value)
    {
        var normalized = value ?? string.Empty;
        var mustQuote = normalized.Contains(';') || normalized.Contains('"') || normalized.Contains('\r') || normalized.Contains('\n');

        if (!mustQuote)
        {
            return normalized;
        }

        return $"\"{normalized.Replace("\"", "\"\"")}";
    }

    private static string BuildContentTypesXml() => """
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        </Types>
        """;

    private static string BuildRootRelationshipsXml() => """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string BuildWorkbookXml() => """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Resultados" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """;

    private static string BuildWorkbookRelationshipsXml() => """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        </Relationships>
        """;

    private static string ChooseFirst(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static PropuestaProveedor? GetProvider(IEnumerable<PropuestaProveedor> providers, params string[] names)
    {
        foreach (var name in names)
        {
            var provider = providers.FirstOrDefault(p => string.Equals(p.Proveedor, name, StringComparison.OrdinalIgnoreCase));
            if (provider is not null)
            {
                return provider;
            }
        }

        return null;
    }

    private static string NormalizeBooleanLike(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return bool.TryParse(value, out var parsed)
            ? parsed.ToString().ToLowerInvariant()
            : value;
    }

    private static string FormatProviderConfidence(double? confidence)
    {
        if (!confidence.HasValue)
        {
            return string.Empty;
        }

        return confidence.Value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string FormatConfidence(string value)
    {
        if (!TryParseConfidence(value, out var raw))
        {
            return value ?? string.Empty;
        }

        var pct = Math.Round(raw * 100, 1);
        var pctText = pct.ToString("0.0", CultureInfo.GetCultureInfo("es-ES"));
        var rawText = raw.ToString("0.####", CultureInfo.InvariantCulture);
        return $"{pctText} % (raw: {rawText})";
    }

    private static bool TryParseConfidence(string? value, out double normalized)
    {
        normalized = 0d;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var cleaned = value.Replace("%", string.Empty, StringComparison.Ordinal).Trim();
        if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out normalized)
            && !double.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out normalized)
            && !double.TryParse(cleaned, NumberStyles.Float, CultureInfo.GetCultureInfo("es-ES"), out normalized))
        {
            return false;
        }

        if (normalized > 1d && normalized <= 100d)
        {
            normalized /= 100d;
        }

        return normalized >= 0d;
    }

    private static bool TryReadOutputSnapshot(string outputJsonPath, out OutputSnapshot snapshot)
    {
        snapshot = new OutputSnapshot();

        if (!File.Exists(outputJsonPath))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(outputJsonPath, Encoding.UTF8));
            JsonElement output;
            if (TryGetProperty(doc.RootElement, out var wrappedOutput, "output", "Output"))
            {
                output = wrappedOutput;
            }
            else
            {
                // In desktop batch runs we persist the orchestration output directly (without wrapper).
                output = doc.RootElement;
            }

            TryGetProperty(output, out var identificacion, "Identificacion", "identificacion");
            TryGetProperty(output, out var resultado, "Resultado", "resultado");
            TryGetProperty(output, out var detalle, "DetalleEjecucion", "detalleEjecucion");
            TryGetProperty(detalle, out var clasificacion, "Clasificacion", "clasificacion");
            TryGetProperty(detalle, out var seguimiento, "Seguimiento", "seguimiento");
            TryGetProperty(output, out var datosExtraidos, "DatosExtraidos", "datosExtraidos");

            snapshot.Status = GetString(resultado, "Estado", "estado");
            snapshot.Typology = GetString(identificacion, "Tipologia", "tipologia");
            snapshot.TipologiaFamilia = GetString(identificacion, "TipologiaFamilia", "tipologiaFamilia");
            snapshot.TipologiaVersion = GetString(identificacion, "TipologiaVersion", "tipologiaVersion");
            snapshot.FechaProceso = GetString(identificacion, "FechaProceso", "fechaProceso");
            snapshot.Paginas = GetString(identificacion, "Paginas", "paginas");
            snapshot.Tdn1 = GetString(identificacion, "Tdn1", "tdn1");
            snapshot.Tdn2 = GetString(identificacion, "Tdn2", "tdn2");
            snapshot.Matricula = GetString(identificacion, "Matricula", "matricula");
            snapshot.TipologiaNombre = GetString(identificacion, "TipologiaNombre", "tipologiaNombre");
            snapshot.PaginasIncluidas = GetString(detalle, "PaginasIncluidas", "paginasIncluidas");
            snapshot.Clasificador = GetString(clasificacion, "Clasificador", "clasificador", "Modelo", "modelo");
            snapshot.ConfidenceRaw = GetString(clasificacion, "Confianza", "confianza");
            snapshot.FallbackLlm = GetString(clasificacion, "FallbackLLM", "fallbackLLM");
            snapshot.DuracionTotalMs = GetString(seguimiento, "DuracionTotalMs", "duracionTotalMs");
            snapshot.PropuestaTipologia = ChooseFirst(
                GetString(identificacion, "PropuestaTipologia", "propuestaTipologia"),
                GetString(clasificacion, "PropuestaTipologia", "propuestaTipologia"));
            snapshot.Resumen = ChooseFirst(
                GetString(datosExtraidos, "Resumen", "resumen"),
                GetString(clasificacion, "ResumenCombinado", "resumenCombinado", "Resumen", "resumen"));

            if (TryGetProperty(clasificacion, out var clasificacionParcial, "ClasificacionParcial", "clasificacionParcial")
                && clasificacionParcial.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                snapshot.ClasificacionParcial = clasificacionParcial.GetBoolean();
            }

            snapshot.EsTipologiaVirtual = snapshot.ClasificacionParcial || !string.IsNullOrWhiteSpace(snapshot.PropuestaTipologia);

            if (TryGetProperty(resultado, out var reutilizadaElement, "ReutilizadaPorDuplicado", "reutilizadaPorDuplicado")
                && reutilizadaElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                snapshot.ReutilizadaPorDuplicado = reutilizadaElement.GetBoolean();
            }

            if (TryGetProperty(clasificacion, out var providers, "DetalleProveedores", "detalleProveedores")
                && providers.ValueKind == JsonValueKind.Array)
            {
                foreach (var provider in providers.EnumerateArray())
                {
                    var confidenceText = GetString(provider, "Confianza", "confianza");
                    _ = double.TryParse(confidenceText, NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence);

                    snapshot.ProviderDetails.Add(new PropuestaProveedor
                    {
                        Proveedor = GetString(provider, "Proveedor", "proveedor"),
                        Tipologia = GetString(provider, "Tipologia", "tipologia"),
                        Confianza = confidence,
                        MotivoDescarte = GetString(provider, "MotivoDescarte", "motivoDescarte")
                    });
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement property, params string[] names)
    {
        property = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var name in names)
        {
            foreach (var current in element.EnumerateObject())
            {
                if (string.Equals(current.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    property = current.Value;
                    return true;
                }
            }
        }

        return false;
    }

    private sealed class OutputSnapshot
    {
        public string Status { get; set; } = string.Empty;
        public string Typology { get; set; } = string.Empty;
        public string TipologiaFamilia { get; set; } = string.Empty;
        public string TipologiaVersion { get; set; } = string.Empty;
        public string FechaProceso { get; set; } = string.Empty;
        public string PaginasIncluidas { get; set; } = string.Empty;
        public string Paginas { get; set; } = string.Empty;
        public string Tdn1 { get; set; } = string.Empty;
        public string Tdn2 { get; set; } = string.Empty;
        public string Matricula { get; set; } = string.Empty;
        public string TipologiaNombre { get; set; } = string.Empty;
        public string PropuestaTipologia { get; set; } = string.Empty;
        public bool ClasificacionParcial { get; set; }
        public bool EsTipologiaVirtual { get; set; }
        public string Clasificador { get; set; } = string.Empty;
        public string ConfidenceRaw { get; set; } = string.Empty;
        public string FallbackLlm { get; set; } = string.Empty;
        public string DuracionTotalMs { get; set; } = string.Empty;
        public string Resumen { get; set; } = string.Empty;
        public bool? ReutilizadaPorDuplicado { get; set; }
        public List<PropuestaProveedor> ProviderDetails { get; } = [];
    }
}