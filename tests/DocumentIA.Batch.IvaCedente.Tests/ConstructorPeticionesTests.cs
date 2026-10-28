using DocumentIA.Batch.IvaCedente.Engine;
using DocumentIA.Batch.IvaCedente.Models;
using Xunit;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class ConstructorPeticionesTests
{
    [Fact]
    public void Clasificacion_LlevaObjectIdYSinPromptNiContenido()
    {
        var r = ConstructorPeticiones.Clasificacion(new FilaEntrada("79044", "107736649", "SCXX-00"), new IvaConfig(), "corr-1");
        Assert.Equal("107736649", r.Documento.ObjectIdGDC);
        Assert.Equal(string.Empty, r.Documento.Content.Base64);
        Assert.True(r.Instrucciones.ClassificationOnly);
        Assert.True(r.Instrucciones.SkipDuplicateCheck);
        Assert.True(r.Instrucciones.SkipGdcUpload);
        Assert.False(r.Instrucciones.ExecuteIntegrarWhenClassificationOnly);
        Assert.Equal(10, r.Instrucciones.MaxPagesForClassificationOnly);
        Assert.Null(r.Instrucciones.Prompt);
        Assert.False(r.Instrucciones.ForzarProcesadoSinLimitePaginas);
        Assert.Equal("79044", r.Trazabilidad.IdActivo);
        Assert.Equal("corr-1", r.Trazabilidad.CorrelationId);
        Assert.StartsWith("iva-cedente", r.Trazabilidad.SubmittedBy);
    }

    [Fact]
    public void ConPrompt_LlevaPromptModeloYSinLimiteDePaginas()
    {
        var config = new IvaConfig { ModelKey = "gpt-x" };
        var r = ConstructorPeticiones.ConPrompt(new FilaEntrada("79044", "107736649", "ESCR-10"), config, new Prompts("S", "U {contenido}"), "c");
        Assert.Equal("S", r.Instrucciones.Prompt!.SystemPrompt);
        Assert.Equal("U {contenido}", r.Instrucciones.Prompt.UserPromptTemplate);
        Assert.Equal("gpt-x", r.Instrucciones.Prompt.ModelKey);
        Assert.True(r.Instrucciones.ForzarProcesadoSinLimitePaginas);
        Assert.True(r.Instrucciones.ClassificationOnly);
    }
}
