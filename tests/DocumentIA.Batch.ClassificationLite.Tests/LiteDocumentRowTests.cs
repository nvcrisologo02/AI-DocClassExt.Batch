using DocumentIA.Batch.ClassificationLite.Models;
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

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("OK", false)]
    [InlineData("ok", false)]
    [InlineData("BAJA_CONFIANZA_CLASIFICACION", true)]
    [InlineData("OTRO_ESTADO", true)]
    public void IsReviewableEstado_ClasificaCorrectamente(string? estado, bool esperado)
    {
        Assert.Equal(esperado, LiteDocumentRow.IsReviewableEstado(estado));
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

    /// <summary>
    /// El refresco periodico de la rejilla vuelca todos los documentos sobre sus filas una vez
    /// por segundo, y la inmensa mayoria no ha cambiado nada. Notificar igualmente cada
    /// propiedad multiplica el trabajo del hilo de interfaz por el numero de documentos.
    /// </summary>
    [Fact]
    public void UpdateFrom_ConLosMismosValores_NoNotificaNingunaPropiedad()
    {
        var document = NuevoDocumento();
        var row = LiteDocumentRow.From(document);
        var notified = new List<string>();
        row.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

        row.UpdateFrom(document);

        Assert.Empty(notified);
    }

    [Fact]
    public void UpdateFrom_ConUnValorDistinto_NotificaSoloEsaPropiedad()
    {
        var document = NuevoDocumento();
        var row = LiteDocumentRow.From(document);
        var notified = new List<string>();
        row.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

        document.Tdn1 = "T07";
        row.UpdateFrom(document);

        Assert.Equal(new[] { nameof(LiteDocumentRow.Tdn1) }, notified);
        Assert.Equal("T07", row.Tdn1);
    }

    private static LiteDocument NuevoDocumento() => new()
    {
        Id = 1,
        FileName = "alfa.pdf",
        Status = LiteDocumentStatus.Succeeded,
        Tdn1 = "T01",
        Tdn2 = "T01.02",
        Confidence = 0.9,
        Pages = 3,
        PagesIncluded = "1-3",
        ProcessDate = "2026-08-06",
        DurationMs = 120,
        Estado = "OK"
    };
}
