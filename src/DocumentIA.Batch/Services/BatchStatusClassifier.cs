namespace DocumentIA.Batch.Services;

/// <summary>
/// Clasifica estados devueltos por el backend (Resultado.EstadoCalidad o Resultado.Estado)
/// en categorías operativas del batch. Lógica pura, sin dependencias de UI.
/// </summary>
public static class BatchStatusClassifier
{
    private static readonly HashSet<string> RevisionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "REVISION",
        "VALIDACION_CON_ERRORES",
        "EXTRACCION_INCOMPLETA",
        "BAJA_CONFIANZA_CLASIFICACION"
    };

    public static bool IsRevisionQuality(string? value) =>
        !string.IsNullOrWhiteSpace(value) && RevisionStates.Contains(value.Trim());
}
