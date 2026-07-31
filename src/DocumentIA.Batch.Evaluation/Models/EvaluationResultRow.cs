namespace DocumentIA.Batch.Evaluation.Models;

public static class EvaluationEstado
{
    public const string Ok = "OK";
    public const string Error = "Error";
    public const string Timeout = "Timeout";
}

/// <summary>Una fila de eval/runs/&lt;run&gt;/results.csv: expected (del manifest) + predicted (del backend).</summary>
public sealed class EvaluationResultRow
{
    public string RelPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ExpectedTdn1 { get; set; } = string.Empty;
    public string ExpectedTdn2 { get; set; } = string.Empty;
    public string? PredictedTdn1 { get; set; }
    public string? PredictedTdn2 { get; set; }
    public double? Confianza { get; set; }
    public string? Proveedor { get; set; }
    public bool Fallback { get; set; }
    public long DuracionMs { get; set; }

    /// <summary>OK | Error | Timeout. Solo las filas OK cuentan para accuracy.</summary>
    public string Estado { get; set; } = EvaluationEstado.Ok;
    public string? Error { get; set; }

    /// <summary>Resultado.Estado del contrato de salida (p.ej. "OK", "PENDIENTE_REINTENTO",
    /// "REVISION", "ERROR"). Distinto de <see cref="Estado"/>, que es el estado interno del
    /// harness (OK/Error/Timeout). Vacio si el contrato no informa el campo.</summary>
    public string EstadoContrato { get; set; } = string.Empty;

    /// <summary>DetalleEjecucion.Clasificacion.RateLimitExcedido del contrato de salida. Permite
    /// distinguir "sin tipologia por rate limit" de "sin tipologia por falta de texto".</summary>
    public bool RateLimit { get; set; }

    /// <summary>DetalleEjecucion.OrigenMarkdown del contrato de salida (p.ej.
    /// "LayoutPreClasificacion", "MarkdownPersistidoBD"). Vacio si el contrato no lo informa.</summary>
    public string OrigenMarkdown { get; set; } = string.Empty;

    /// <summary>CorrelationId generado por el runner al construir la request de ingesta (no
    /// proviene del contrato de salida). Se conserva aunque el documento termine en error o
    /// timeout, siempre que el runner llegara a generarlo.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    public bool IsEvaluated => string.Equals(Estado, EvaluationEstado.Ok, StringComparison.OrdinalIgnoreCase);

    public bool Tdn1Correct => IsEvaluated && string.Equals(ExpectedTdn1, PredictedTdn1, StringComparison.OrdinalIgnoreCase);

    public bool Tdn2Correct => IsEvaluated && string.Equals(ExpectedTdn2, PredictedTdn2, StringComparison.OrdinalIgnoreCase);
}
