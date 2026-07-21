namespace DocumentIA.Batch.Evaluation;

/// <summary>Parseo minimo de argumentos '--nombre valor' (sin flags booleanas sueltas).</summary>
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
                throw new EvaluationUsageException($"Argumento inesperado: '{token}'. Se esperaba '--opcion valor'.");
            }

            var name = token[2..];
            if (i + 1 >= args.Length)
            {
                throw new EvaluationUsageException($"Falta el valor de '--{name}'.");
            }

            result._values[name] = args[++i];
        }

        return result;
    }

    public string Require(string name)
    {
        if (!_values.TryGetValue(name, out var value))
        {
            throw new EvaluationUsageException($"Falta el argumento obligatorio '--{name}'.");
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
            throw new EvaluationUsageException($"El valor de '--{name}' debe ser un entero: '{value}'.");
        }

        return parsed;
    }
}
