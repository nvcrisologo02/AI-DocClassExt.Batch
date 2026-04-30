using System.IO;
using System.Text.Json;
using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.Services;

public class BatchRunStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string CreateRunFolder()
    {
        var runsRoot = Path.Combine(AppContext.BaseDirectory, "runs");
        var runFolder = Path.Combine(runsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(runFolder);
        return runFolder;
    }

    public string SaveOutputJson(string runFolder, BatchFileItem file, JsonElement output)
    {
        Directory.CreateDirectory(runFolder);

        var baseName = Path.GetFileNameWithoutExtension(file.FileName);
        var safeName = SanitizeFileName(baseName);
        var suffix = string.IsNullOrWhiteSpace(file.InstanceId) ? file.CorrelationId : file.InstanceId;
        var filePath = Path.Combine(runFolder, $"{safeName}_{suffix}.json");

        File.WriteAllText(filePath, JsonSerializer.Serialize(output, JsonOptions));
        return filePath;
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "documento" : sanitized;
    }
}