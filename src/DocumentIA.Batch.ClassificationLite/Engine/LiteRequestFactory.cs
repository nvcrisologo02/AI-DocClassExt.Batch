using System.Text.Encodings.Web;
using System.Text.Json;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.ClassificationLite.Engine;

public static class LiteRequestFactory
{
    private static readonly JsonSerializerOptions StorageJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static IngestRequest Build(LiteConfig config, string fileName, byte[] fileBytes, string correlationId)
    {
        var isTdn1Level = string.Equals(config.ClassificationLevel, "TDN1", StringComparison.OrdinalIgnoreCase);
        var effectiveClassificationOnly = config.OnlyClassification || isTdn1Level;
        var isDefaultLevel = string.Equals(config.ClassificationLevel, "DEFAULT", StringComparison.OrdinalIgnoreCase);

        return new IngestRequest
        {
            Instrucciones = new IngestInstrucciones
            {
                ExpectedType = string.Empty,
                ClassificationOnly = effectiveClassificationOnly,
                ExecuteIntegrarWhenClassificationOnly = effectiveClassificationOnly ? false : null,
                MaxPagesForClassificationOnly = effectiveClassificationOnly ? 10 : 0,
                ForzarResumenPorDefecto = null,
                SkipDuplicateCheck = false,
                ForceReprocess = config.ForceReprocess,
                SkipGdcUpload = true,
                Classification = new IngestIaConfig
                {
                    Provider = config.Provider,
                    Model = config.Model,
                    NivelClasificacion = isDefaultLevel ? null : config.ClassificationLevel
                },
                Extraction = new IngestIaConfig
                {
                    Provider = "auto",
                    Model = "auto"
                }
            },
            Documento = new IngestDocumento
            {
                Name = fileName,
                Content = new IngestDocumentoContent
                {
                    Base64 = Convert.ToBase64String(fileBytes)
                }
            },
            Trazabilidad = new IngestTrazabilidad
            {
                CorrelationId = correlationId,
                SubmittedBy = "DocumentIA.Batch.ClassificationLite"
            }
        };
    }

    public static string BuildRequestJsonForStorage(IngestRequest request, long fileSizeBytes)
    {
        var storageCopy = new IngestRequest
        {
            Instrucciones = request.Instrucciones,
            Trazabilidad = request.Trazabilidad,
            Documento = new IngestDocumento
            {
                Name = request.Documento.Name,
                Content = new IngestDocumentoContent
                {
                    Base64 = $"<base64 omitido, {fileSizeBytes} bytes>"
                }
            }
        };

        return JsonSerializer.Serialize(storageCopy, StorageJsonOptions);
    }
}
