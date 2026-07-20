using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.Services;

public static class LiteExportService
{
    public static readonly string[] Headers =
    {
        "FileName", "Status", "PagesIncluded", "Pages", "TDN1", "TDN2", "Confidence", "ProcessDate", "TotalDurationMs"
    };

    public static string[] ToRow(LiteDocument document) => new[]
    {
        document.FileName,
        document.Status,
        document.PagesIncluded ?? string.Empty,
        document.Pages?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        document.Tdn1 ?? string.Empty,
        document.Tdn2 ?? string.Empty,
        document.Confidence?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
        document.ProcessDate ?? string.Empty,
        document.DurationMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
    };

    public static void ExportCsv(IEnumerable<LiteDocument> documents, string path)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(string.Join(';', Headers));

        foreach (var document in documents)
        {
            writer.WriteLine(string.Join(';', ToRow(document).Select(EscapeCsv)));
        }
    }

    public static void ExportExcel(IEnumerable<LiteDocument> documents, string path)
    {
        var rows = new List<string[]> { Headers };
        rows.AddRange(documents.Select(ToRow));

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);

        WriteEntry(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

        WriteEntry(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="Resultados" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);

        WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);

        WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildSheetXml(rows));
    }

    private static string BuildSheetXml(IReadOnlyList<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        builder.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            builder.Append($"<row r=\"{rowIndex + 1}\">");
            var cells = rows[rowIndex];
            for (var columnIndex = 0; columnIndex < cells.Length; columnIndex++)
            {
                var reference = $"{ColumnName(columnIndex)}{rowIndex + 1}";
                builder.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
                builder.Append(SecurityElement.Escape(RemoveInvalidXmlChars(cells[columnIndex])) ?? string.Empty);
                builder.Append("</t></is></c>");
            }

            builder.Append("</row>");
        }

        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    /// <summary>
    /// Elimina los caracteres de control no validos en XML 1.0 (Excel rechaza el fichero si aparecen).
    /// Conserva tabulador, salto de linea y retorno de carro, que si son validos.
    /// </summary>
    private static string RemoveInvalidXmlChars(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (XmlConvert.IsXmlChar(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        var current = index;
        do
        {
            name = (char)('A' + (current % 26)) + name;
            current = (current / 26) - 1;
        }
        while (current >= 0);

        return name;
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string EscapeCsv(string value)
    {
        // Los valores que empiezan por = + - @ podrian ser interpretados como formulas por Excel al
        // abrir el CSV. Se neutralizan con un apostrofo inicial, salvo que sean numeros validos
        // (por ejemplo duraciones negativas), que se generan internamente y nunca son formulas.
        var needsFormulaGuard = value.Length > 0
            && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@')
            && !double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

        var effectiveValue = needsFormulaGuard ? "'" + value : value;

        if (!needsFormulaGuard && !effectiveValue.Contains(';') && !effectiveValue.Contains('"') && !effectiveValue.Contains('\n') && !effectiveValue.Contains('\r'))
        {
            return effectiveValue;
        }

        return '"' + effectiveValue.Replace("\"", "\"\"") + '"';
    }
}
