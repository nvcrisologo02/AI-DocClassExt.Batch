using DocumentIA.Batch.ClassificationLite.ViewModels;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteDocumentRowTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("OK", "")]
    [InlineData("ok", "")]
    [InlineData("BAJA_CONFIANZA_CLASIFICACION", "Baja confianza")]
    [InlineData("baja_confianza_clasificacion", "Baja confianza")]
    [InlineData("OTRO_RECHAZO", "OTRO_RECHAZO")]
    public void ResultadoDisplay_TraduceElEstadoATextoAmigable(string? estado, string expected)
    {
        var row = new LiteDocumentRow { Estado = estado };

        Assert.Equal(expected, row.ResultadoDisplay);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("OK", false)]
    [InlineData("ok", false)]
    [InlineData("BAJA_CONFIANZA_CLASIFICACION", true)]
    public void IsLowConfidence_EsTrueParaCualquierEstadoDistintoDeOk(string? estado, bool expected)
    {
        var row = new LiteDocumentRow { Estado = estado };

        Assert.Equal(expected, row.IsLowConfidence);
    }

    [Fact]
    public void Estado_AlCambiar_NotificaResultadoDisplayEIsLowConfidence()
    {
        var row = new LiteDocumentRow();
        var notified = new List<string>();
        row.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

        row.Estado = "BAJA_CONFIANZA_CLASIFICACION";

        Assert.Contains(nameof(LiteDocumentRow.Estado), notified);
        Assert.Contains(nameof(LiteDocumentRow.ResultadoDisplay), notified);
        Assert.Contains(nameof(LiteDocumentRow.IsLowConfidence), notified);
    }
}
