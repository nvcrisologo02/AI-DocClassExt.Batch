using DocumentIA.Batch.IvaCedente.Engine;
using DocumentIA.Batch.IvaCedente.Io;

namespace DocumentIA.Batch.IvaCedente.Commands;

/// <summary>Comando consolidar: CSV por documento a CSV por activo.</summary>
public static class ConsolidarCommand
{
    public static int Execute(string[] args)
    {
        var cli = CliArgs.Parse(args);
        var entrada = cli.Require("entrada");
        var salida = cli.Require("salida");
        if (!File.Exists(entrada))
        {
            throw new IvaCedenteUsageException($"No existe el fichero de entrada '{entrada}'.");
        }

        var activos = Consolidador.Consolidar(SalidaDocumentosCsv.Leer(entrada));
        SalidaActivosCsv.Escribir(salida, activos);
        Console.WriteLine($"Activos consolidados: {activos.Count}. Para revisar (Revisar = sí): {activos.Count(a => a.Revisar == "sí")}.");
        return 0;
    }
}
