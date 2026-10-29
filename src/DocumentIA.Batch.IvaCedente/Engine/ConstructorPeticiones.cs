using DocumentIA.Batch.IvaCedente.Models;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.IvaCedente.Engine;

/// <summary>Construye las peticiones de ingest por Object ID de GDC.</summary>
public static class ConstructorPeticiones
{
    public const string Programa = "iva-cedente";

    /// <summary>Petición de solo clasificación, sin prompt ni contenido.</summary>
    public static IngestRequest Clasificacion(FilaEntrada fila, IvaConfig config, string correlationId)
        => new()
        {
            Instrucciones = new IngestInstrucciones
            {
                ClassificationOnly = true,
                ExecuteIntegrarWhenClassificationOnly = false,
                MaxPagesForClassificationOnly = config.MaxPaginasClasificacion,
                SkipDuplicateCheck = true,
                ForceReprocess = false,
                SkipGdcUpload = true,
                ForzarProcesadoSinLimitePaginas = false
            },
            Documento = new IngestDocumento { Name = string.Empty, ObjectIdGDC = fila.ObjectId },
            Trazabilidad = new IngestTrazabilidad
            {
                CorrelationId = correlationId,
                SubmittedBy = SolicitanteProvider.ObtenerSolicitante(Programa),
                IdActivo = fila.IdSareb
            }
        };

    /// <summary>Petición con prompt de IVA, sin límite de páginas según la configuración. Con tipología forzada no es de solo clasificación.</summary>
    public static IngestRequest ConPrompt(FilaEntrada fila, IvaConfig config, Prompts prompts, string correlationId)
    {
        var request = Clasificacion(fila, config, correlationId);
        request.Instrucciones.ForzarProcesadoSinLimitePaginas = config.ForzarSinLimitePaginas;
        if (!string.IsNullOrWhiteSpace(config.TipologiaForzada))
        {
            // El trigger de DocumentIA rechaza ExpectedType junto con ClassificationOnly (400), así que la tipología
            // forzada va por el camino completo: sin clasificación, sin extracción (resumen.documental no la tiene),
            // sin subida a GDC (SkipGdcUpload) y con el prompt libre.
            request.Instrucciones.ExpectedType = config.TipologiaForzada.Trim();
            request.Instrucciones.ClassificationOnly = false;
        }

        request.Instrucciones.Prompt = new IngestPromptConfig
        {
            SystemPrompt = prompts.System,
            UserPromptTemplate = prompts.User,
            ModelKey = config.ModelKey
        };
        return request;
    }
}
