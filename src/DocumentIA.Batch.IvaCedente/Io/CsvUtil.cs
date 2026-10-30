using System.Globalization;
using System.Text;

namespace DocumentIA.Batch.IvaCedente.Io;

/// <summary>
/// Lector/escritor CSV mínimo (delimitador ';', comillas RFC4180 básicas). Los ficheros usan UTF-8
/// con BOM, igual que los exports de Batch.
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

    /// <summary>Número para los CSV: punto decimal y la cadena más corta que hace ida y vuelta; vacío si es nulo.</summary>
    public static string Numero(double? valor)
        => valor?.ToString(CultureInfo.InvariantCulture) ?? "";

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

            // Un campo entrecomillado puede contener saltos de línea: se sigue leyendo mientras
            // queden comillas sin cerrar.
            while (line.Count(ch => ch == '"') % 2 != 0)
            {
                var next = reader.ReadLine();
                if (next is null)
                {
                    break;
                }

                line += "\n" + next;
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

    /// <summary>
    /// Como WriteRows pero atómica: escribe en "path.tmp" y lo mueve sobre el destino, de modo que un
    /// fallo a medias no deja el fichero truncado.
    /// </summary>
    public static void WriteRowsAtomic(string path, IEnumerable<string> header, IEnumerable<IEnumerable<string?>> rows)
    {
        var tmp = path + ".tmp";
        try
        {
            WriteRows(tmp, header, rows);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
