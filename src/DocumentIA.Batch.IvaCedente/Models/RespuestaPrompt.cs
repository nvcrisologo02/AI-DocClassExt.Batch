namespace DocumentIA.Batch.IvaCedente.Models;

/// <summary>Respuesta JSON del modelo al prompt de IVA del cedente.</summary>
public sealed class RespuestaPrompt
{
    /// <summary>Quién transmite el inmueble en el documento, según el modelo. Solo se usa para la guarda de Sareb.</summary>
    public string? Transmitente { get; set; }

    /// <summary>Quién adquiere el inmueble en el documento, según el modelo. Solo se usa para la guarda de Sareb.</summary>
    public string? Adquirente { get; set; }

    public bool EsTituloPrevio { get; set; }
    public string HuboIVA { get; set; } = ValoresIva.NoConsta;
    public string DeduccionIVA { get; set; } = ValoresIva.NoConsta;
    public string? Cedente { get; set; }
    public string? FechaAdquisicion { get; set; }

    /// <summary>Porcentaje de IVA aplicado en la adquisición (21, 18, 16...). Null si no hubo o no consta.</summary>
    public double? TipoIVA { get; set; }

    /// <summary>Cuota de IVA en euros. Null si no hubo o no consta.</summary>
    public double? CuotaIVA { get; set; }

    public string? Evidencia { get; set; }
    public double? Confianza { get; set; }
}
