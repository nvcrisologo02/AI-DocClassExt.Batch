using System.Text;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class LiteRequestFactoryTests
{
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("pdf-fake");

    [Fact]
    public void Build_ConDefaults_EsClassificationOnlyTdn1Tdn2()
    {
        var config = new LiteConfig();

        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        Assert.True(request.Instrucciones.ClassificationOnly);
        Assert.False(request.Instrucciones.ExecuteIntegrarWhenClassificationOnly);
        Assert.Equal(10, request.Instrucciones.MaxPagesForClassificationOnly);
        Assert.True(request.Instrucciones.ForzarResumenPorDefecto);
        Assert.False(request.Instrucciones.SkipDuplicateCheck);
        Assert.False(request.Instrucciones.ForceReprocess);
        Assert.True(request.Instrucciones.SkipGdcUpload);
        Assert.Equal("auto", request.Instrucciones.Classification.Provider);
        Assert.Equal("auto", request.Instrucciones.Classification.Model);
        Assert.Equal("TDN1_TDN2", request.Instrucciones.Classification.NivelClasificacion);
        Assert.Equal(string.Empty, request.Instrucciones.ExpectedType);
        Assert.Equal("doc.pdf", request.Documento.Name);
        Assert.Equal(Convert.ToBase64String(Bytes), request.Documento.Content.Base64);
        Assert.Equal("corr-1", request.Trazabilidad.CorrelationId);
        Assert.Equal("DocumentIA.Batch.ClassificationLite", request.Trazabilidad.SubmittedBy);
    }

    [Fact]
    public void Build_NivelDefault_NoEnviaNivel()
    {
        var config = new LiteConfig { ClassificationLevel = "DEFAULT" };

        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        Assert.Null(request.Instrucciones.Classification.NivelClasificacion);
    }

    [Fact]
    public void Build_NivelTdn1_FuerzaClassificationOnly()
    {
        var config = new LiteConfig { ClassificationLevel = "TDN1", OnlyClassification = false };

        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        Assert.True(request.Instrucciones.ClassificationOnly);
    }

    [Fact]
    public void Build_SinOnlyClassificationNiTdn1_NoLimitaPaginas()
    {
        var config = new LiteConfig { OnlyClassification = false };

        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        Assert.False(request.Instrucciones.ClassificationOnly);
        Assert.Null(request.Instrucciones.ExecuteIntegrarWhenClassificationOnly);
        Assert.Equal(0, request.Instrucciones.MaxPagesForClassificationOnly);
    }

    [Fact]
    public void Build_ForceReprocess_SePropaga()
    {
        var config = new LiteConfig { ForceReprocess = true };

        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        Assert.True(request.Instrucciones.ForceReprocess);
    }

    [Fact]
    public void Build_GenerateSummaryFalse_NoFuerzaElResumen()
    {
        var config = new LiteConfig { GenerateSummary = false };

        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        Assert.False(request.Instrucciones.ForzarResumenPorDefecto);
    }

    [Fact]
    public void BuildRequestJsonForStorage_SustituyeBase64PorPlaceholder()
    {
        var config = new LiteConfig();
        var request = LiteRequestFactory.Build(config, "doc.pdf", Bytes, "corr-1");

        var json = LiteRequestFactory.BuildRequestJsonForStorage(request, 123456);

        Assert.Contains("<base64 omitido, 123456 bytes>", json);
        Assert.DoesNotContain(Convert.ToBase64String(Bytes), json);
        // El request original no se muta:
        Assert.Equal(Convert.ToBase64String(Bytes), request.Documento.Content.Base64);
    }
}
