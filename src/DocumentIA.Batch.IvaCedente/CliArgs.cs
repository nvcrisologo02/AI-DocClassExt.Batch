namespace DocumentIA.Batch.IvaCedente;

/// <summary>Parseo mínimo de argumentos '--nombre valor' y flags sin valor ('--nombre').</summary>
public class CliArgs
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    private CliArgs()
    {
    }

    public static CliArgs Parse(string[] args)
    {
        var result = new CliArgs();
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new IvaCedenteUsageException($"Argumento inesperado: '{token}'. Se esperaba '--opcion valor'.");
            }

            var name = token[2..];
            var isFlag = i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal);
            result._values[name] = isFlag ? string.Empty : args[++i];
        }

        return result;
    }

    /// <summary>Indica si '--name' apareció, con o sin valor.</summary>
    public bool Has(string name) => _values.ContainsKey(name);

    public string Require(string name)
    {
        if (!_values.TryGetValue(name, out var value))
        {
            throw new IvaCedenteUsageException($"Falta el argumento obligatorio '--{name}'.");
        }

        return value;
    }

    public string GetOrDefault(string name, string defaultValue)
        => _values.TryGetValue(name, out var value) ? value : defaultValue;

    public int GetIntOrDefault(string name, int defaultValue)
    {
        if (!_values.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed))
        {
            throw new IvaCedenteUsageException($"El valor de '--{name}' debe ser un entero: '{value}'.");
        }

        return parsed;
    }
}
