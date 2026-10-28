using System.Text.Json;
using DocumentIA.Batch.IvaCedente.Engine;
using Xunit;

namespace DocumentIA.Batch.IvaCedente.Tests;

public class ParserSalidaTests
{
    [Fact]
    public void Leer_OutputPascalCase_ExtraeEstadoTdn2YResultadoPrompt()
    {
        var output = JsonDocument.Parse("""{"Resultado":{"Estado":"OK"},"Identificacion":{"Tdn2":"ESCR-10"},"DatosExtraidos":{"ResultadoPrompt":"{\"esTituloPrevio\":true}"}}""").RootElement;
        var s = ParserSalida.Leer(output);
        Assert.Equal("OK", s.EstadoContrato);
        Assert.Equal("ESCR-10", s.Tdn2);
        Assert.Contains("esTituloPrevio", s.ResultadoPrompt);
    }

    [Fact]
    public void ParsearRespuesta_ConVallasYTextoAlrededor_Parsea()
    {
        var texto = "Claro, aquí tienes:\n```json\n{\"esTituloPrevio\": true, \"huboIVA\": \"Sí\", \"deduccionIVA\": \"no consta\", \"cedente\": \"Banco X\", \"fechaAdquisicion\": \"2008-05-12\", \"evidencia\": \"sujeta a IVA\", \"confianza\": 0.8}\n```\nEspero que sirva.";
        var r = ParserSalida.ParsearRespuesta(texto)!;
        Assert.True(r.EsTituloPrevio);
        Assert.Equal("sí", r.HuboIVA);
        Assert.Equal("no consta", r.DeduccionIVA);
        Assert.Equal(0.8, r.Confianza);
    }

    [Theory]
    [InlineData("no es JSON")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("{\"esTituloPrevio\": }")]
    public void ParsearRespuesta_SinJsonValido_DevuelveNull(string? texto) => Assert.Null(ParserSalida.ParsearRespuesta(texto));

    [Theory]
    [InlineData("SI", false, "sí")]
    [InlineData("si", false, "sí")]
    [InlineData("true", false, "sí")]
    [InlineData("No", false, "no")]
    [InlineData("desconocido", false, "no consta")]
    [InlineData("no aplica", true, "no aplica")]
    [InlineData("no aplica", false, "no consta")]
    [InlineData(null, false, "no consta")]
    public void NormalizarValor_MapeaALosValoresFijos(string? entrada, bool permiteNoAplica, string esperado)
        => Assert.Equal(esperado, ParserSalida.NormalizarValor(entrada, permiteNoAplica));
}
