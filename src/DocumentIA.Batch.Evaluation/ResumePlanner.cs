using DocumentIA.Batch.Evaluation.Models;

namespace DocumentIA.Batch.Evaluation;

/// <summary>
/// Reanudacion de un 'run' (--resume &lt;dir&gt;): decide que documentos quedan por procesar a
/// partir del results.csv de la pasada anterior y consolida las pasadas en el orden del set.
/// Como el CSV se anexa fila a fila durante la ejecucion, un mismo rel_path puede aparecer
/// varias veces (p.ej. Error en una pasada y OK en la siguiente): gana la ultima fila.
/// </summary>
public static class ResumePlanner
{
    public static List<ManifestRow> Pending(IReadOnlyList<ManifestRow> documents, IEnumerable<EvaluationResultRow> previous)
    {
        // Un OK con RateLimit es un documento devuelto sin tipologia por un 429 del modelo:
        // se reintenta igual que un error, para no falsear la medicion con "vacios".
        var last = LastByRelPath(previous);
        return documents
            .Where(d => !(last.TryGetValue(d.RelPath, out var r) && r.IsEvaluated && !r.RateLimit))
            .ToList();
    }

    public static List<EvaluationResultRow> Merge(
        IReadOnlyList<ManifestRow> documents,
        IEnumerable<EvaluationResultRow> previous,
        IEnumerable<EvaluationResultRow> fresh)
    {
        var last = LastByRelPath(previous.Concat(fresh));
        var merged = new List<EvaluationResultRow>(documents.Count);
        foreach (var doc in documents)
        {
            if (last.TryGetValue(doc.RelPath, out var row))
            {
                merged.Add(row);
            }
        }

        return merged;
    }

    private static Dictionary<string, EvaluationResultRow> LastByRelPath(IEnumerable<EvaluationResultRow> rows)
    {
        var last = new Dictionary<string, EvaluationResultRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            last[row.RelPath] = row;
        }

        return last;
    }
}
