namespace DocumentIA.Batch.IvaCedente.Models;

/// <summary>Respuesta JSON del modelo al prompt de IVA del cedente.</summary>
public sealed class RespuestaPrompt
{
    public bool EsTituloPrevio { get; set; }
    public string HuboIVA { get; set; } = ValoresIva.NoConsta;
    public string DeduccionIVA { get; set; } = ValoresIva.NoConsta;
    public string? Cedente { get; set; }
    public string? FechaAdquisicion { get; set; }
    public string? Evidencia { get; set; }
    public double? Confianza { get; set; }
}
