using DocumentIA.Batch.IvaCedente.Commands;

namespace DocumentIA.Batch.IvaCedente;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0];

        try
        {
            // El comando consolidar se cablea en una tarea posterior.
            return command.ToLowerInvariant() switch
            {
                "procesar" => await ProcesarCommand.ExecuteAsync(args[1..]),
                "help" or "--help" or "-h" => PrintUsageAndSucceed(),
                _ => PrintUnknownCommand(command)
            };
        }
        catch (IvaCedenteUsageException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int PrintUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Comando desconocido: '{command}'.");
        PrintUsage();
        return 1;
    }

    private static int PrintUsageAndSucceed()
    {
        PrintUsage();
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            DocumentIA.Batch.IvaCedente — consola headless que detecta IVA y su deducción en el título previo de cada activo.

            Uso:
              procesar --entrada <csv> --salida <csv> [--config <ruta>] [--colas <n>] [--reintentar-errores]
              consolidar --entrada <csv por documento> --salida <csv por activo>
              help
            """);
    }
}
