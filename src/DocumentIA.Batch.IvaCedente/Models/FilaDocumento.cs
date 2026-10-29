namespace DocumentIA.Batch.IvaCedente.Models;

/// <summary>Resultado de un documento: una fila del CSV de salida.</summary>
public sealed class FilaDocumento
{
    public string IdSareb { get; set; } = "";
    public string ObjectId { get; set; } = "";
    public string Tdn2 { get; set; } = "";
    public string Procesado { get; set; } = EstadosProcesado.Pendiente;
    public string EsTituloPrevio { get; set; } = "";
    public string HuboIVA { get; set; } = "";
    public string DeduccionIVA { get; set; } = "";
    public string Cedente { get; set; } = "";
    public string FechaAdquisicion { get; set; } = "";
    public double? TipoIVA { get; set; }
    public double? CuotaIVA { get; set; }
    public string Evidencia { get; set; } = "";
    public string Tipologia { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string Error { get; set; } = "";
    public double? Confianza { get; set; }

    /// <summary>Instante de procesado, en UTC.</summary>
    public DateTime? Fecha { get; set; }

    /// <summary>Copia de la fila con otro IdSareb y Tdn2 (el mismo documento visto desde otro activo).</summary>
    public FilaDocumento ParaActivo(string idSareb, string tdn2)
    {
        var copia = (FilaDocumento)MemberwiseClone();
        copia.IdSareb = idSareb;
        copia.Tdn2 = tdn2;
        return copia;
    }
}
