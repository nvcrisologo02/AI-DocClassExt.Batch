using DocumentIA.Batch.Evaluation.Metrics;
using Xunit;

namespace DocumentIA.Batch.Evaluation.Tests;

public class McNemarTestTests
{
    [Fact]
    public void ExactTwoSidedPValue_B01_1_B10_9_EsAproximadamente0_021()
    {
        var p = McNemarTest.ExactTwoSidedPValue(1, 9);

        Assert.Equal(0.021484375, p, precision: 6);
    }

    [Fact]
    public void ExactTwoSidedPValue_B01_5_B10_5_Es1()
    {
        var p = McNemarTest.ExactTwoSidedPValue(5, 5);

        Assert.Equal(1.0, p, precision: 6);
    }

    [Fact]
    public void ExactTwoSidedPValue_EsSimetricoEnB01yB10()
    {
        Assert.Equal(McNemarTest.ExactTwoSidedPValue(1, 9), McNemarTest.ExactTwoSidedPValue(9, 1), precision: 10);
    }

    [Fact]
    public void ExactTwoSidedPValue_SinDiscordantes_Es1()
    {
        Assert.Equal(1.0, McNemarTest.ExactTwoSidedPValue(0, 0));
    }

    [Fact]
    public void ExactTwoSidedPValue_DiferenciaGrande_EsMuyPequenio()
    {
        var p = McNemarTest.ExactTwoSidedPValue(2, 30);

        Assert.True(p < 0.001, $"p-valor inesperadamente alto: {p}");
    }

    [Fact]
    public void ExactTwoSidedPValue_NuncaSuperaUno()
    {
        for (var b01 = 0; b01 <= 10; b01++)
        {
            for (var b10 = 0; b10 <= 10; b10++)
            {
                Assert.True(McNemarTest.ExactTwoSidedPValue(b01, b10) <= 1.0);
            }
        }
    }
}
