using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.ViewModels;

/// <summary>
/// Foto de una ejecucion leida de SQLite en un hilo cualquiera, para aplicarla despues sobre la
/// rejilla en el hilo de interfaz. Existe para que la parte caro del refresco (la consulta de
/// todas las filas mas el recuento agregado) no se ejecute en el hilo de interfaz.
/// </summary>
internal sealed class LiteGridSnapshot
{
    public LiteGridSnapshot(string executionId, List<LiteDocument> documents, LiteCounters counters)
    {
        ExecutionId = executionId;
        Documents = documents;
        Counters = counters;
    }

    public string ExecutionId { get; }

    public List<LiteDocument> Documents { get; }

    public LiteCounters Counters { get; }
}
