using DocumentIA.Batch.Classification.Models;
using DocumentIA.Batch.Classification.Services;
using Xunit;

namespace DocumentIA.Batch.Classification.Tests;

public class ClassificationReprocessPolicyTests
{
    [Theory]
    [InlineData("Error", null, true, true)]
    [InlineData("Error", null, false, true)]
    [InlineData("Cancelled", null, true, true)]
    [InlineData("Cancelled", null, false, true)]
    [InlineData("Pendiente", null, true, true)]
    [InlineData("Pendiente", null, false, false)]
    [InlineData("OK", null, true, false)]
    [InlineData("Completado", null, true, false)]
    [InlineData("REVISION", null, true, false)]
    [InlineData("VALIDACION_CON_ERRORES", null, true, false)]
    [InlineData("BAJA_CONFIANZA", null, true, false)]
    [InlineData("En cola", null, true, false)]
    [InlineData("Enviando", null, true, false)]
    [InlineData("Processing", null, true, false)]
    [InlineData("En ejecución", null, true, false)]
    [InlineData("OK", "Failed", true, true)]
    [InlineData("OK", "Terminated", true, true)]
    [InlineData("OK", "Completed", true, false)]
    public void IsReprocessable_MatrizDeEstados(string status, string? runtimeStatus, bool hasBatchRun, bool expected)
    {
        var file = new ClassificationDocumentItem
        {
            Status = status,
            RuntimeStatus = runtimeStatus ?? string.Empty
        };

        Assert.Equal(expected, ClassificationReprocessPolicy.IsReprocessable(file, hasBatchRun));
    }

    [Fact]
    public void ResetForReprocess_LimpiaTraza_YDejaPendiente()
    {
        var file = new ClassificationDocumentItem
        {
            FileName = "doc.pdf",
            FullPath = @"C:\docs\doc.pdf",
            Status = "Error",
            CorrelationId = "corr-1",
            InstanceId = "inst-1",
            RuntimeStatus = "Failed",
            StatusQueryUri = "http://status",
            MensajeError = "boom",
            FechaInicio = DateTime.Now,
            FechaFin = DateTime.Now,
            OutputJsonPath = @"C:\runs\out.json",
            IdentificacionDocumento = "id-doc",
            TipologiaIdentificada = "tip",
            ConfianzaGlobal = "0.9",
            ResultadoEstado = "REVISION",
            IdentificacionGuid = "guid",
            FechaProceso = "2026-07-14",
            Paginas = "3",
            Tdn1 = "t1",
            Tdn2 = "t2",
            Matricula = "mat",
            Clasificador = "clas",
            FallbackLlm = "fb",
            FallbackRazon = "razon",
            JustificacionClasificacion = "just",
            ClassificationOnlyOutput = "co",
            TipologiaFamilia = "fam",
            TipologiaVersion = "v1",
            TipologiaNombre = "nombre",
            TipologiaMgdcMatricula = "mgdc",
            GdcTipoDocumento = "gtd",
            GdcSubtipoDocumento = "gstd",
            GdcSerie = "serie",
            GptDescripcion = "desc",
            Resumen = "resumen",
            RecorteAplicado = "si",
            PaginasIncluidas = "1-3",
            MarkdownGenerado = "md",
            OrigenMarkdown = "pdfpig",
            ModeloLlmUsado = "modelo",
            ActividadActual = "act",
            ActividadesCompletadas = "2",
            ActividadesTotales = "3",
            DuracionTotalMs = "1234",
            TimelineActividades = "timeline",
            Proveedor = "prov",
            MotivoDescarte = "motivo",
            ReutilizadaPorDuplicado = true,
            MensajeReutilizacion = "reutil"
        };
        file.DetalleProveedores.Add(new PropuestaProveedor { Proveedor = "p" });

        ClassificationReprocessPolicy.ResetForReprocess(file);

        Assert.Equal("Pendiente", file.Status);
        Assert.Equal("doc.pdf", file.FileName);
        Assert.Equal(@"C:\docs\doc.pdf", file.FullPath);
        Assert.Equal(string.Empty, file.CorrelationId);
        Assert.Equal(string.Empty, file.InstanceId);
        Assert.Equal(string.Empty, file.RuntimeStatus);
        Assert.Equal(string.Empty, file.StatusQueryUri);
        Assert.Equal(string.Empty, file.MensajeError);
        Assert.Null(file.FechaInicio);
        Assert.Null(file.FechaFin);
        Assert.Equal(string.Empty, file.OutputJsonPath);
        Assert.Equal(string.Empty, file.TipologiaIdentificada);
        Assert.Equal(string.Empty, file.ConfianzaGlobal);
        Assert.Equal(string.Empty, file.Resumen);
        Assert.Equal(string.Empty, file.TimelineActividades);
        Assert.False(file.ReutilizadaPorDuplicado);
        Assert.Empty(file.DetalleProveedores);
    }
}
