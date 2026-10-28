using DocumentIA.Batch.IvaCedente.Engine;
using DocumentIA.Batch.IvaCedente.Io;
using DocumentIA.Batch.IvaCedente.Models;

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

        var documentos = SalidaDocumentosCsv.Leer(entrada);
        var activos = Consolidador.Consolidar(documentos);
        var incompletas = documentos.Count(f => f.Procesado is EstadosProcesado.Pendiente or EstadosProcesado.Error or EstadosProcesado.Timeout);
        SalidaActivosCsv.Escribir(salida, activos);
        Console.WriteLine($"Activos consolidados: {activos.Count}. Para revisar (Revisar = sí): {activos.Count(a => a.Revisar == "sí")}.");
        if (incompletas > 0)
        {
            Console.WriteLine($"Aviso: {incompletas} filas siguen en pendiente, error o timeout; sus activos llevan Revisar = sí. Relanza procesar (con --reintentar-errores si procede) antes de dar el resultado por bueno.");
        }

        return 0;
    }
}
