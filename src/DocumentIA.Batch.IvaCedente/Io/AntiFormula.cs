namespace DocumentIA.Batch.IvaCedente.Io;

/// <summary>
/// Neutralización frente a la inyección de fórmulas de Excel: un valor que empieza por = + - @ tab CR o
/// apóstrofo se escribe con un apóstrofo delante, y Desneutralizar lo quita al leer.
/// </summary>
public static class AntiFormula
{
    public static string Neutralizar(string? valor)
    {
        valor ??= "";
        return valor.Length > 0 && valor[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\''
            ? "'" + valor
            : valor;
    }

    public static string Desneutralizar(string valor)
        => valor.Length > 0 && valor[0] == '\'' ? valor[1..] : valor;
}
