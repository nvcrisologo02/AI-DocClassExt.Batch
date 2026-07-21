namespace DocumentIA.Batch.Evaluation.Metrics;

/// <summary>Matriz de confusion cuadrada expected x predicted, mismas etiquetas en ambos ejes.</summary>
public sealed class ConfusionMatrix
{
    public ConfusionMatrix(IReadOnlyList<string> labels, int[,] counts)
    {
        Labels = labels;
        Counts = counts;
    }

    public IReadOnlyList<string> Labels { get; }

    public int[,] Counts { get; }

    public int this[int expectedIndex, int predictedIndex] => Counts[expectedIndex, predictedIndex];
}
