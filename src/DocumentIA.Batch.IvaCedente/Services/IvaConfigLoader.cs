using System.Text.Json;
using DocumentIA.Batch.Models;
using DocumentIA.Batch.IvaCedente.Models;

namespace DocumentIA.Batch.IvaCedente.Services;

public static class IvaConfigLoader
{
    public const string VariableFunctionKey = "DOCIA_FUNCTION_KEY";

    private static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IvaConfig Cargar(string path)
    {
        if (!File.Exists(path))
            throw new IvaCedenteUsageException($"No se encuentra el fichero de configuración: {path}. Copia config.sample.json a config.json y ajústalo.");
        try
        {
            return JsonSerializer.Deserialize<IvaConfig>(File.ReadAllText(path), Opciones) ?? new IvaConfig();
        }
        catch (JsonException ex)
        {
            throw new IvaCedenteUsageException($"El fichero de configuración {path} no es un JSON válido: {ex.Message}");
        }
    }

    public static EnvironmentConfig EntornoSeleccionado(IvaConfig config)
    {
        var entorno = config.Environments.FirstOrDefault(e => string.Equals(e.Name, config.Entorno, StringComparison.OrdinalIgnoreCase))
            ?? throw new IvaCedenteUsageException($"El entorno '{config.Entorno}' no está definido en Environments.");

        var clave = Environment.GetEnvironmentVariable(VariableFunctionKey);
        if (string.IsNullOrWhiteSpace(clave))
            clave = entorno.FunctionKey;
        if (string.IsNullOrWhiteSpace(clave))
            throw new IvaCedenteUsageException($"Falta la function key del entorno {entorno.Name}: defínela en la variable de entorno {VariableFunctionKey} o en config.json.");

        return new EnvironmentConfig { Name = entorno.Name, BackendUrl = entorno.BackendUrl, FunctionKey = clave };
    }

    public static Prompts CargarPrompts(IvaConfig config, string baseDirectory) =>
        new(Leer(config.PromptSystemPath, baseDirectory), Leer(config.PromptUserPath, baseDirectory));

    private static string Leer(string ruta, string baseDirectory)
    {
        var completa = Path.IsPathRooted(ruta) ? ruta : Path.Combine(baseDirectory, ruta);
        if (!File.Exists(completa))
            throw new IvaCedenteUsageException($"No se encuentra el fichero de prompt: {completa}");
        return File.ReadAllText(completa, System.Text.Encoding.UTF8);
    }
}
