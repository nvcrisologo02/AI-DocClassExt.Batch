using DocumentIA.Batch.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace DocumentIA.Batch.Services;

/// <summary>
/// Servicio para exportar el historial de ejecuciones a CSV y Excel.
/// Sin dependencias de Office ni ClosedXML — usa ZipArchive + XmlWriter.
/// </summary>
public class HistorialExportService
{
    /// <summary>
    /// Exporta los archivos de un run a CSV.
    /// </summary>
    public void ExportCsv(string filePath, BatchRunRecord run, IEnumerable<BatchRunFileRecord> files)
    {
        var sb = new StringBuilder();

        // Encabezado CSV
        sb.AppendLine("Fecha Run,Tipología,Archivo,Tamaño,Estado,Estado Calidad,Confianza Global,Duración,Fecha Inicio,Fecha Fin,InstanceId,CorrelationId,Mensaje Error,Ruta JSON");

        // Filas
        foreach (var file in files)
        {
            var row = new[]
            {
                EscapeCsv(run.CreatedAtDisplay),
                EscapeCsv(run.Tipologia),
                EscapeCsv(file.FileName),
                EscapeCsv(file.SizeDisplay),
                EscapeCsv(file.Estado),
                EscapeCsv(file.EstadoCalidad ?? string.Empty),
                EscapeCsv(file.ConfidenceDisplay),
                EscapeCsv(file.DurationDisplay),
                EscapeCsv(file.FechaInicio?.ToString("O") ?? string.Empty),
                EscapeCsv(file.FechaFin?.ToString("O") ?? string.Empty),
                EscapeCsv(file.InstanceId ?? string.Empty),
                EscapeCsv(file.CorrelationId ?? string.Empty),
                EscapeCsv(file.MensajeError ?? string.Empty),
                EscapeCsv(file.OutputJsonPath ?? string.Empty)
            };

            sb.AppendLine(string.Join(",", row));
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Exporta los archivos de un run a Excel (.xlsx).
    /// Crea un XLSX válido sin Office ni ClosedXML usando ZipArchive + XmlWriter.
    /// </summary>
    public void ExportExcel(string filePath, BatchRunRecord run, IEnumerable<BatchRunFileRecord> files)
    {
        var filesList = files.ToList();

        using var zip = ZipFile.Open(filePath, ZipArchiveMode.Create);

        // Crear estructura mínima del XLSX
        CreateExcelStructure(zip);

        // Crear workbook.xml.rels
        CreateWorkbookRels(zip);

        // Crear sheet1.xml con los datos
        CreateSheet1(zip, run, filesList);

        // Crear workbook.xml
        CreateWorkbook(zip);

        // Crear [Content_Types].xml
        CreateContentTypes(zip);
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static void CreateExcelStructure(ZipArchive zip)
    {
        // _rels/.rels
        var relsEntry = zip.CreateEntry("_rels/.rels");
        using (var writer = new StreamWriter(relsEntry.Open()))
        {
            writer.Write(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");
        }
    }

    private static void CreateWorkbookRels(ZipArchive zip)
    {
        var relsEntry = zip.CreateEntry("xl/_rels/workbook.xml.rels");
        using (var writer = new StreamWriter(relsEntry.Open()))
        {
            writer.Write(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "</Relationships>");
        }
    }

    private static void CreateSheet1(ZipArchive zip, BatchRunRecord run, IReadOnlyList<BatchRunFileRecord> files)
    {
        var sheetEntry = zip.CreateEntry("xl/worksheets/sheet1.xml");
        using (var writer = new StreamWriter(sheetEntry.Open()))
        {
            writer.Write(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<sheetData>");

            // Cabecera
            var headers = new[]
            {
                "Fecha Run", "Tipología", "Archivo", "Tamaño", "Estado",
                "Estado Calidad", "Confianza Global", "Duración", "Fecha Inicio",
                "Fecha Fin", "InstanceId", "CorrelationId", "Mensaje Error", "Ruta JSON"
            };

            writer.Write("<row r=\"1\">");
            for (int col = 0; col < headers.Length; col++)
            {
                var cellRef = GetCellRef(1, col + 1);
                writer.Write($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{XmlEscape(headers[col])}</t></is></c>");
            }
            writer.Write("</row>");

            // Filas de datos
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var rowNum = i + 2;

                var values = new[]
                {
                    run.CreatedAtDisplay,
                    run.Tipologia,
                    file.FileName,
                    file.SizeDisplay,
                    file.Estado,
                    file.EstadoCalidad ?? string.Empty,
                    file.ConfidenceDisplay,
                    file.DurationDisplay,
                    file.FechaInicio?.ToString("O") ?? string.Empty,
                    file.FechaFin?.ToString("O") ?? string.Empty,
                    file.InstanceId ?? string.Empty,
                    file.CorrelationId ?? string.Empty,
                    file.MensajeError ?? string.Empty,
                    file.OutputJsonPath ?? string.Empty
                };

                writer.Write($"<row r=\"{rowNum}\">");
                for (int col = 0; col < values.Length; col++)
                {
                    var cellRef = GetCellRef(rowNum, col + 1);
                    writer.Write($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{XmlEscape(values[col])}</t></is></c>");
                }
                writer.Write("</row>");
            }

            writer.Write(
                "</sheetData>" +
                "<cols>" +
                "<col min=\"1\" max=\"1\" width=\"22\" customWidth=\"1\"/>" +
                "<col min=\"2\" max=\"2\" width=\"20\" customWidth=\"1\"/>" +
                "<col min=\"3\" max=\"3\" width=\"32\" customWidth=\"1\"/>" +
                "<col min=\"4\" max=\"4\" width=\"14\" customWidth=\"1\"/>" +
                "<col min=\"5\" max=\"14\" width=\"20\" customWidth=\"1\"/>" +
                "</cols>" +
                "</worksheet>");
        }
    }

    private static string GetCellRef(int row, int col)
    {
        var colStr = string.Empty;
        var c = col;
        while (c > 0)
        {
            c--;
            colStr = (char)('A' + (c % 26)) + colStr;
            c /= 26;
        }
        return $"{colStr}{row}";
    }

    private static void CreateWorkbook(ZipArchive zip)
    {
        var workbookEntry = zip.CreateEntry("xl/workbook.xml");
        using (var writer = new StreamWriter(workbookEntry.Open()))
        {
            writer.Write(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets>" +
                "<sheet name=\"Historial\" sheetId=\"1\" r:id=\"rId1\"/>" +
                "</sheets>" +
                "</workbook>");
        }
    }

    private static void CreateContentTypes(ZipArchive zip)
    {
        var ctEntry = zip.CreateEntry("[Content_Types].xml");
        using (var writer = new StreamWriter(ctEntry.Open()))
        {
            writer.Write(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "</Types>");
        }
    }

    private static string XmlEscape(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
