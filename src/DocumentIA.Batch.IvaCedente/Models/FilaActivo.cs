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
    public string ObjectIdsSoporte { get; set; } = "";
    public string Revisar { get; set; } = "no";
}
