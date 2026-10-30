namespace DocumentIA.Batch.IvaCedente.Models;

/// <summary>Resultado consolidado de un activo: una fila del CSV por activo.</summary>
public sealed class FilaActivo
{
    public string IdSareb { get; set; } = "";
    public int Documentos { get; set; }
    public int Leidos { get; set; }
    public int ConRespuesta { get; set; }
    public string HuboIVA { get; set; } = "";
    public string DeduccionIVA { get; set; } = "";

    /// <summary>Object ID del título elegido para HuboIVA cuando vale sí o no.</summary>
    public string DocIVA { get; set; } = "";

    /// <summary>Tipo y cuota de IVA del título elegido, solo cuando HuboIVA es sí.</summary>
    public double? TipoIVA { get; set; }
    public double? CuotaIVA { get; set; }
    public string Cedente { get; set; } = "";
    public string FechaAdquisicion { get; set; } = "";

    /// <summary>Object ID del título elegido para DeduccionIVA cuando vale sí o no.</summary>
    public string DocDeduccion { get; set; } = "";

    /// <summary>Frase legible con el sustento del IVA y la deducción.</summary>
    public string Sustento { get; set; } = "";

    /// <summary>Motivos de Revisar separados por "; "; puede llevar solo "filas sin resolver (n)" con Revisar en no.</summary>
    public string MotivoRevisar { get; set; } = "";

    /// <summary>Títulos que discrepan del elegido; alimentan la frase de Sustento y no se escriben en el CSV.</summary>
    public List<Discrepancia> Discrepancias { get; set; } = new();
    public string ObjectIdsSoporte { get; set; } = "";
    public string Revisar { get; set; } = "no";
}
