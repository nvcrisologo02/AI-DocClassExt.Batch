using System.Text.Json;
using DocumentIA.Batch.Services;
using Xunit;

namespace DocumentIA.Batch.Tests;

public class IngestRequestSerializationTests
{
    [Fact]
    public void IngestRequest_ConObjectIdGdcYPrompt_SerializaLosNombresDelContrato()
    {
        var request = new IngestRequest
        {
            Instrucciones = new IngestInstrucciones
            {
                ClassificationOnly = true, SkipDuplicateCheck = true, SkipGdcUpload = true,
                ForzarProcesadoSinLimitePaginas = true,
                Prompt = new IngestPromptConfig { SystemPrompt = "s", UserPromptTemplate = "u {contenido}", ModelKey = "gpt-x" }
            },
            Documento = new IngestDocumento { ObjectIdGDC = "107736649" },
            Trazabilidad = new IngestTrazabilidad { IdActivo = "79044", SubmittedBy = "iva-cedente/u" }
        };
        var json = JsonSerializer.Serialize(request);
        Assert.Contains("\"objectIdGDC\":\"107736649\"", json);
        Assert.Contains("\"forzarProcesadoSinLimitePaginas\":true", json);
        Assert.Contains("\"modelKey\":\"gpt-x\"", json);
        Assert.Contains("\"idActivo\":\"79044\"", json);
        Assert.Contains("\"base64\":\"\"", json);
    }
}
