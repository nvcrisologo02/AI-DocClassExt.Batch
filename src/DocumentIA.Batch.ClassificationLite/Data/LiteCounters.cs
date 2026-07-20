namespace DocumentIA.Batch.ClassificationLite.Data;

public class LiteCounters
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int InFlight { get; set; }
    public int Succeeded { get; set; }
    public int Error { get; set; }
    public int DefinitiveError { get; set; }
    public int SkippedHistory { get; set; }
    public int Cancelled { get; set; }
}
