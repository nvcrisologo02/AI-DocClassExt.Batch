using DocumentIA.Batch.IvaCedente.Models;

namespace DocumentIA.Batch.IvaCedente.Engine;

/// <summary>Calcula qué filas de la entrada quedan por procesar al relanzar y fusiona resultados.</summary>
public static class Reanudacion
{
    public static List<FilaEntrada> Pendientes(IReadOnlyList<FilaEntrada> entrada, IReadOnlyList<FilaDocumento> previas, bool reintentarErrores)
    {
        var resueltas = new HashSet<(string, string)>();
        foreach (var p in previas)
        {
            var resuelta = EstadosProcesado.EsFinal(p.Procesado)
                || (!reintentarErrores && p.Procesado is EstadosProcesado.Error or EstadosProcesado.Timeout);
            if (resuelta)
            {
                resueltas.Add((p.IdSareb, p.ObjectId));
            }
        }

        return entrada.Where(e => !resueltas.Contains((e.IdSareb, e.ObjectId))).ToList();
    }

    public static List<FilaDocumento> Fusionar(IReadOnlyList<FilaEntrada> entrada, IReadOnlyList<FilaDocumento> previas, IReadOnlyList<FilaDocumento> nuevas)
    {
        var porClave = new Dictionary<(string, string), FilaDocumento>();
        foreach (var p in previas)
        {
            porClave[(p.IdSareb, p.ObjectId)] = p;
        }

        foreach (var n in nuevas)
        {
            porClave[(n.IdSareb, n.ObjectId)] = n;
        }

        var resultado = entrada
            .Select(e => porClave.TryGetValue((e.IdSareb, e.ObjectId), out var f)
                ? f
                : new FilaDocumento { IdSareb = e.IdSareb, ObjectId = e.ObjectId, Tdn2 = e.Tdn2, Procesado = EstadosProcesado.Pendiente })
            .ToList();

        // Resultados previos de documentos que la entrada actual ya no incluye: se conservan al final.
        var enEntrada = entrada.Select(e => (e.IdSareb, e.ObjectId)).ToHashSet();
        var vistas = new HashSet<(string, string)>();
        resultado.AddRange(previas.Where(p => !enEntrada.Contains((p.IdSareb, p.ObjectId)) && vistas.Add((p.IdSareb, p.ObjectId))));
        return resultado;
    }
}
