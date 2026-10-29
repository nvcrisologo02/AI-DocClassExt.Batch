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
    [Fact]
    public void ConPrompt_ConTipologiaForzada_LaEnviaComoExpectedType()
    {
        var config = new IvaConfig { TipologiaForzada = "resumen.documental" };
        var r = ConstructorPeticiones.ConPrompt(new FilaEntrada("1", "2", "ESCR-10"), config, new Prompts("S", "U"), "c");
        Assert.Equal("resumen.documental", r.Instrucciones.ExpectedType);
    }

    [Fact]
    public void ConPrompt_SinTipologiaForzada_NoEnviaExpectedType()
    {
        var r = ConstructorPeticiones.ConPrompt(new FilaEntrada("1", "2", "ESCR-10"), new IvaConfig(), new Prompts("S", "U"), "c");
        Assert.Equal(string.Empty, r.Instrucciones.ExpectedType);
    }

    [Fact]
    public void Clasificacion_NuncaLlevaExpectedType()
    {
        var config = new IvaConfig { TipologiaForzada = "resumen.documental" };
        var r = ConstructorPeticiones.Clasificacion(new FilaEntrada("1", "2", "SCXX-00"), config, "c");
        Assert.Equal(string.Empty, r.Instrucciones.ExpectedType);
    }
}
