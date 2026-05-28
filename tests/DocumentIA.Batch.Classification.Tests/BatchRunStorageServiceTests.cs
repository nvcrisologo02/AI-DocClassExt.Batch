using System.IO;
using System.Text.Json;
using DocumentIA.Batch.Models;
using DocumentIA.Batch.Services;
using Xunit;

namespace DocumentIA.Batch.Classification.Tests;

public class BatchRunStorageServiceTests
{
    [Fact]
    public void SaveOutputJson_WritesUnicodeWithoutEscaping()
    {
        var service = new BatchRunStorageService();
        var runFolder = Path.Combine(Path.GetTempPath(), "documentia-batch-tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(runFolder);

        try
        {
            using var doc = JsonDocument.Parse("""
            {
              "DatosExtraidos": {
                "Resumen": "Resumen de extracción y validación"
              }
            }
            """);

            var file = new BatchFileItem
            {
                FileName = "demo.pdf",
                CorrelationId = "corr-1",
                InstanceId = "inst-1"
            };

            var outputPath = service.SaveOutputJson(runFolder, file, doc.RootElement);
            var savedJson = File.ReadAllText(outputPath);

            Assert.Contains("extracción", savedJson);
            Assert.Contains("validación", savedJson);
            Assert.DoesNotContain("\\u00", savedJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(runFolder))
            {
                Directory.Delete(runFolder, recursive: true);
            }
        }
    }
}
