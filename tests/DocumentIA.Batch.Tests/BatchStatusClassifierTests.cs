using DocumentIA.Batch.Services;
using Xunit;

namespace DocumentIA.Batch.Tests;

public class BatchStatusClassifierTests
{
    [Theory]
    [InlineData("REVISION")]
    [InlineData("VALIDACION_CON_ERRORES")]
    [InlineData("EXTRACCION_INCOMPLETA")]
    [InlineData("BAJA_CONFIANZA_CLASIFICACION")]
    [InlineData("extraccion_incompleta")]
    [InlineData(" EXTRACCION_INCOMPLETA ")]
    public void IsRevisionQuality_EstadosDeRevision_DevuelveTrue(string estado)
    {
        Assert.True(BatchStatusClassifier.IsRevisionQuality(estado));
    }

    [Theory]
    [InlineData("OK")]
    [InlineData("ERROR")]
    [InlineData("DUPLICADO")]
    [InlineData("BAJA_CONFIANZA")] // valor antiguo erróneo: el backend nunca lo emite
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsRevisionQuality_OtrosEstados_DevuelveFalse(string? estado)
    {
        Assert.False(BatchStatusClassifier.IsRevisionQuality(estado));
    }
}
