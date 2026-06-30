namespace DocumentIA.Batch.Models;

public class TipologiaOption
{
    /// <summary>Código sin versión, ej: nota.simple.1_4  (usado en el request)</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Identificador completo del backend, ej: nota.simple.1_4@1.4</summary>
    public string Identificador { get; set; } = string.Empty;

    /// <summary>Nombre legible del backend, ej: "Nota Simple"</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Texto a mostrar en el ComboBox: nombre y código, ej. "Nota Simple (nota.simple.1_4)"</summary>
    public string Display
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Nombre)) return Code;
            if (string.IsNullOrWhiteSpace(Code)) return Nombre;
            return $"{Nombre} ({Code})";
        }
    }

    public override string ToString() => Display;
}
