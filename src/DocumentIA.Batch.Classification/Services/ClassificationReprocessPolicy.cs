using DocumentIA.Batch.Classification.Models;

namespace DocumentIA.Batch.Classification.Services;

/// <summary>
/// Decide qué documentos de un lote de clasificación pueden reprocesarse
/// (los que no obtuvieron resultado) y limpia su traza para relanzarlos.
/// Lógica pura, sin dependencias de UI.
/// </summary>
public static class ClassificationReprocessPolicy
{
    public static bool IsReprocessable(ClassificationDocumentItem file, bool hasBatchRun)
    {
        if (string.Equals(file.Status, "Error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(file.RuntimeStatus, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.RuntimeStatus, "Terminated", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return hasBatchRun
            && string.Equals(file.Status, "Pendiente", StringComparison.OrdinalIgnoreCase);
    }

    public static void ResetForReprocess(ClassificationDocumentItem file)
    {
        file.CorrelationId = string.Empty;
        file.InstanceId = string.Empty;
        file.RuntimeStatus = string.Empty;
        file.StatusQueryUri = string.Empty;

        file.MensajeError = string.Empty;
        file.FechaInicio = null;
        file.FechaFin = null;

        file.OutputJsonPath = string.Empty;
        file.IdentificacionDocumento = string.Empty;
        file.TipologiaIdentificada = string.Empty;
        file.ConfianzaGlobal = string.Empty;
        file.ResultadoEstado = string.Empty;
        file.IdentificacionGuid = string.Empty;
        file.FechaProceso = string.Empty;
        file.Paginas = string.Empty;

        file.Tdn1 = string.Empty;
        file.Tdn2 = string.Empty;
        file.Matricula = string.Empty;
        file.Clasificador = string.Empty;
        file.FallbackLlm = string.Empty;
        file.FallbackRazon = string.Empty;
        file.JustificacionClasificacion = string.Empty;
        file.ClassificationOnlyOutput = string.Empty;
        file.TipologiaFamilia = string.Empty;
        file.TipologiaVersion = string.Empty;
        file.TipologiaNombre = string.Empty;
        file.TipologiaMgdcMatricula = string.Empty;
        file.GdcTipoDocumento = string.Empty;
        file.GdcSubtipoDocumento = string.Empty;
        file.GdcSerie = string.Empty;
        file.GptDescripcion = string.Empty;

        file.Resumen = string.Empty;
        file.RecorteAplicado = string.Empty;
        file.PaginasIncluidas = string.Empty;
        file.MarkdownGenerado = string.Empty;
        file.OrigenMarkdown = string.Empty;
        file.ModeloLlmUsado = string.Empty;
        file.ActividadActual = string.Empty;
        file.ActividadesCompletadas = string.Empty;
        file.ActividadesTotales = string.Empty;
        file.DuracionTotalMs = string.Empty;
        file.TimelineActividades = string.Empty;

        file.Proveedor = string.Empty;
        file.MotivoDescarte = string.Empty;
        file.ReutilizadaPorDuplicado = false;
        file.MensajeReutilizacion = string.Empty;
        file.DetalleProveedores.Clear();

        file.IsSelected = false;

        file.Status = "Pendiente";
    }
}
