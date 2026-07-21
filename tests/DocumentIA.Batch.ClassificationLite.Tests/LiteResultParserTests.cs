using System.Text.Json;
using DocumentIA.Batch.ClassificationLite.Engine;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteResultParserTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Parse_OutputPascalCase_ExtraeTodosLosCampos()
    {
        var output = Parse("""
            {
              "Identificacion": { "Tdn1": "T01", "Tdn2": "T01.02", "Paginas": 14, "FechaProceso": "2026-07-20T10:00:00Z" },
              "Resultado": { "Estado": "OK", "ConfianzaGlobal": 0.91 },
              "DetalleEjecucion": {
                "PaginasIncluidas": "1-10",
                "Clasificacion": { "Confianza": 0.85, "Tdn2Detectado": "T01.99" },
                "Seguimiento": { "DuracionTotalMs": 5230 }
              }
            }
            """);

        var result = LiteResultParser.Parse(output);

        Assert.Equal("T01", result.Tdn1);
        Assert.Equal("T01.02", result.Tdn2);
        Assert.Equal(0.91, result.Confidence);
        Assert.Equal(14, result.Pages);
        Assert.Equal("1-10", result.PagesIncluded);
        Assert.Equal("2026-07-20T10:00:00Z", result.ProcessDate);
        Assert.Equal(5230, result.DurationMs);
        Assert.Equal("OK", result.Estado);
    }

    [Fact]
    public void Parse_OutputCamelCase_ConFallbacks()
    {
        var output = Parse("""
            {
              "identificacion": { "tdn1": "T05", "paginas": "7" },
              "resultado": { "estado": "OK" },
              "detalleEjecucion": {
                "paginasIncluidas": "3",
                "clasificacion": { "confianza": "0.72", "tdn2Detectado": "T05.01" },
                "seguimiento": { "duracionTotalMs": "8100" }
              }
            }
            """);

        var result = LiteResultParser.Parse(output);

        Assert.Equal("T05", result.Tdn1);
        Assert.Equal("T05.01", result.Tdn2);
        Assert.Equal(0.72, result.Confidence);
        Assert.Equal(7, result.Pages);
        Assert.Equal("3", result.PagesIncluded);
        Assert.Equal(8100, result.DurationMs);
    }

    [Fact]
    public void Parse_OutputVacio_DevuelveNulos()
    {
        var result = LiteResultParser.Parse(Parse("{}"));

        Assert.Null(result.Tdn1);
        Assert.Null(result.Tdn2);
        Assert.Null(result.Confidence);
        Assert.Null(result.Pages);
        Assert.Null(result.PagesIncluded);
        Assert.Null(result.ProcessDate);
        Assert.Null(result.DurationMs);
        Assert.Null(result.Estado);
    }
}
