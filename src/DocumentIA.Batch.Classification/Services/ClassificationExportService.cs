using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using DocumentIA.Batch.Classification.Models;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.Classification.Services;

public class ClassificationExportService
{
    private static readonly string[] Headers =
    {
        "FileName",
        "Identificacion_TipoDocumento",
        "Identificacion_TipologiaDetectada",
        "Resultado_ConfianzaGlobal"
    };

    public void ExportCsv(string filePath, IEnumerable<ClassificationDocumentItem> items)
    {
        var rows = BuildRows(items);
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(';', Headers.Select(Escape)));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(';', new[] { row.FileName, row.IdentificacionDocumento, row.TipologiaIdentificada, row.ConfianzaGlobal }.Select(Escape)));
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

    private static IEnumerable<ClassificationExportRow> BuildRows(IEnumerable<ClassificationDocumentItem> items)
    {
        var extractor = new BatchOutputAuditExtractor();

        foreach (var item in items)
        {
            var identificacion = item.IdentificacionDocumento;
            var tipologia = item.TipologiaIdentificada;
            var confianza = item.ConfianzaGlobal;

            if (!string.IsNullOrWhiteSpace(item.OutputJsonPath)
                && (string.IsNullOrWhiteSpace(identificacion) || string.IsNullOrWhiteSpace(tipologia) || string.IsNullOrWhiteSpace(confianza)))
            {
                var audit = extractor.Extract(item.OutputJsonPath);
                identificacion = string.IsNullOrWhiteSpace(identificacion) ? audit.IdentificacionTipoDocumento : identificacion;
                tipologia = string.IsNullOrWhiteSpace(tipologia) ? audit.IdentificacionTipologiaDetectada : tipologia;
                confianza = string.IsNullOrWhiteSpace(confianza) ? audit.ResultadoConfianzaGlobal : confianza;
            }

            yield return new ClassificationExportRow(item.FileName ?? string.Empty, identificacion ?? string.Empty, tipologia ?? string.Empty, confianza ?? string.Empty);
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
            WriteRow(writer, rowNumber++, new[] { row.FileName, row.IdentificacionDocumento, row.TipologiaIdentificada, row.ConfianzaGlobal });
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
}