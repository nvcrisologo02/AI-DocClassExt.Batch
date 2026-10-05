using System.Text;

namespace DocumentIA.Batch.Evaluation.Io;

/// <summary>
/// Lector/escritor CSV minimo (delimitador configurable, comillas RFC4180 basicas). Los ficheros
/// de eval (manifest.csv, golden.csv, results.csv) usan ';' como separador y UTF-8 con BOM, igual
/// que los exports de Batch.
/// </summary>
public static class CsvUtil
{
    public const char Delimiter = ';';

    public static string[] SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == Delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    public static string EscapeField(string? value)
    {
        value ??= string.Empty;
        if (value.IndexOfAny(new[] { Delimiter, '"', '\n', '\r' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// Anexa una fila al final del fichero; si no existe lo crea con la cabecera. Cada llamada
    /// abre, escribe y cierra, de modo que una fila termina en disco antes de seguir.
    /// </summary>
    public static void AppendRow(string path, IEnumerable<string> header, IEnumerable<string?> row)
    {
        var exists = File.Exists(path) && new FileInfo(path).Length > 0;
        var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: !exists);
        using var writer = new StreamWriter(path, append: true, utf8Bom);
        writer.NewLine = "\r\n";
        if (!exists)
        {
            writer.WriteLine(string.Join(Delimiter, header.Select(EscapeField)));
        }

        writer.WriteLine(string.Join(Delimiter, row.Select(EscapeField)));
    }

    public static IReadOnlyList<string[]> ReadRows(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var rows = new List<string[]>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            rows.Add(SplitLine(line));
        }

        return rows;
    }

    public static void WriteRows(string path, IEnumerable<string> header, IEnumerable<IEnumerable<string?>> rows)
    {
        var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        using var writer = new StreamWriter(path, append: false, utf8Bom);
        writer.NewLine = "\r\n";
        writer.WriteLine(string.Join(Delimiter, header.Select(EscapeField)));
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(Delimiter, row.Select(EscapeField)));
        }
    }
}
