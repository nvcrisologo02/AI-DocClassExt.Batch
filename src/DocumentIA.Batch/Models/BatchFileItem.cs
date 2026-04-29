namespace DocumentIA.Batch.Models;

public class BatchFileItem
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Estado { get; set; } = "Pendiente";

    public string DisplaySize
    {
        get
        {
            if (SizeBytes < 1024)
            {
                return $"{SizeBytes} B";
            }

            if (SizeBytes < 1024 * 1024)
            {
                return $"{SizeBytes / 1024d:F1} KB";
            }

            return $"{SizeBytes / (1024d * 1024d):F1} MB";
        }
    }
}
