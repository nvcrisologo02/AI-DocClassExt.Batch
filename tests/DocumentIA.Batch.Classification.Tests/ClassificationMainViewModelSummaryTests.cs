using System.Reflection;
using System.Text.Json;
using DocumentIA.Batch.Classification.Models;
using DocumentIA.Batch.Classification.ViewModels;
using Xunit;

namespace DocumentIA.Batch.Classification.Tests;

public class ClassificationMainViewModelSummaryTests
{
    [Fact]
    public void ApplySummaryFromOutput_Prioritizes_DatosExtraidos_Resumen()
    {
        var json = """
        {
          "DatosExtraidos": {
            "Resumen": "Resumen desde datos extraidos"
          },
          "DetalleEjecucion": {
            "Clasificacion": {
              "ResumenCombinado": "Resumen combinado"
            }
          }
        }
        """;

        var item = new ClassificationDocumentItem();
        InvokeApplySummaryFromOutput(item, json);

        Assert.Equal("Resumen desde datos extraidos", item.Resumen);
    }

    [Fact]
    public void ApplySummaryFromOutput_FallsBack_To_ResumenCombinado_When_DatosExtraidos_IsMissing()
    {
        var json = """
        {
          "DetalleEjecucion": {
            "Clasificacion": {
              "ResumenCombinado": "Resumen desde clasificacion"
            }
          }
        }
        """;

        var item = new ClassificationDocumentItem();
        InvokeApplySummaryFromOutput(item, json);

        Assert.Equal("Resumen desde clasificacion", item.Resumen);
    }

    private static void InvokeApplySummaryFromOutput(ClassificationDocumentItem item, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var method = typeof(ClassificationMainViewModel).GetMethod(
            "ApplySummaryFromOutput",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        method!.Invoke(null, [item, doc.RootElement]);
    }
}
