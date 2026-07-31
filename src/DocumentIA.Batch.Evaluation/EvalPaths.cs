namespace DocumentIA.Batch.Evaluation;

/// <summary>
/// Localiza la carpeta eval/ del repo independientemente del directorio de trabajo desde el que
/// se invoque la consola (bin de salida al depurar, raiz del repo al ejecutar publicado, etc.).
/// </summary>
public static class EvalPaths
{
    private const int MaxLevelsUp = 8;

    public static string FindEvalDirectory()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var found = SearchUp(start);
            if (found is not null)
            {
                return found;
            }
        }

        throw new EvaluationUsageException(
            "No se encontro la carpeta 'eval' (con manifest.csv) subiendo desde el directorio "
            + $"actual ni desde el directorio del ejecutable. Ejecuta la consola desde el repo o "
            + "cerca de el.");
    }

    private static string? SearchUp(string start)
    {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i <= MaxLevelsUp && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "eval");
            if (File.Exists(Path.Combine(candidate, "manifest.csv")))
            {
                return candidate;
            }
        }

        return null;
    }
}
