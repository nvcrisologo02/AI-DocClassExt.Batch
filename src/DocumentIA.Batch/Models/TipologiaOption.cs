namespace DocumentIA.Batch.Models;

public class TipologiaOption
{
    public string Code { get; set; } = string.Empty;

    public override string ToString()
    {
        return Code;
    }
}
