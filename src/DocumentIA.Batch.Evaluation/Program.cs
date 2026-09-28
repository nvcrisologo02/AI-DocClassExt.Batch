using DocumentIA.Batch.Evaluation.Commands;

namespace DocumentIA.Batch.Evaluation;

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
        var rest = args[1..];

        try
        {
            return command.ToLowerInvariant() switch
            {
                "run" => await RunCommand.ExecuteAsync(rest),
                "report" => ReportCommand.Execute(rest),
                "compare" => CompareCommand.Execute(rest),
                "help" or "--help" or "-h" => PrintUsageAndSucceed(),
                _ => PrintUnknownCommand(command)
            };
        }
        catch (EvaluationUsageException ex)
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
            DocumentIA.Batch.Evaluation — harness headless de evaluacion de clasificacion.

            Uso:
              run --set golden|full|half-a|half-b|cata100|list [--list <csv>] [--corpus-root <ruta>] [--env <ENV>] [--label <texto>] [--parallel <n>] [--max-pages <n>] [--config <ruta>] [--only-tdn1 <fams>] [--only-files <ruta>] [--resume <dir-de-run>]
              report --run <dir-de-run>
              compare --a <dirA> --b <dirB>

            Defaults: --corpus-root H:\Documentia\ParaNacho\Class | --env DEV | --parallel 2 | --max-pages 10
            --set list lee un CSV rel_path;expected_tdn1;expected_tdn2 (--list). --resume reutiliza
            un run anterior: salta los documentos ya OK de su results.csv y reprocesa el resto.
            results.csv se anexa fila a fila, asi que un run interrumpido se reanuda sin perder nada.
            """);
    }
}
