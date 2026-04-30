using DocumentIA.Batch.Models;

namespace DocumentIA.Batch.Services;

public static class RetryPolicy
{
    public static bool IsRetryable(BatchFileItem file)
    {
        if (file.Estado.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Estado, "Cancelado", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.Estado, "Revision", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(file.RuntimeStatus, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.RuntimeStatus, "Terminated", StringComparison.OrdinalIgnoreCase);
    }

    public static void ResetForRetry(BatchFileItem file)
    {
        file.Estado = "Pendiente";
        file.InstanceId = string.Empty;
        file.CorrelationId = string.Empty;
        file.RuntimeStatus = string.Empty;
        file.ActividadActual = string.Empty;
        file.ProgresoActividades = string.Empty;
        file.DetalleActividad = string.Empty;
        file.EstadoCalidad = string.Empty;
        file.ConfianzaGlobal = null;
        file.MensajeError = string.Empty;
        file.FechaInicio = null;
        file.FechaFin = null;
        file.OutputJsonPath = string.Empty;
    }
}