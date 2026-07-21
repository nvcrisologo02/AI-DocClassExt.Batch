using DocumentIA.Batch.ClassificationLite.Engine;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class AdaptivePollingStrategyTests
{
    [Fact]
    public void GetDelay_SigueLaSecuenciaAdaptativaYSeEstabiliza()
    {
        var strategy = new AdaptivePollingStrategy(steadyIntervalSeconds: 60);

        Assert.Equal(TimeSpan.FromSeconds(3), strategy.GetDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(5), strategy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(20), strategy.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(30), strategy.GetDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(60), strategy.GetDelay(5));
        Assert.Equal(TimeSpan.FromSeconds(60), strategy.GetDelay(50));
    }

    [Fact]
    public void GetDelay_ConIntervaloCorto_NuncaSuperaElIntervalo()
    {
        var strategy = new AdaptivePollingStrategy(steadyIntervalSeconds: 10);

        Assert.Equal(TimeSpan.FromSeconds(3), strategy.GetDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.GetDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.GetDelay(9));
    }

    [Fact]
    public void IsTimedOut_RespetaElLimite()
    {
        var strategy = new AdaptivePollingStrategy(steadyIntervalSeconds: 60, timeoutMinutes: 30);

        Assert.False(strategy.IsTimedOut(TimeSpan.FromMinutes(29)));
        Assert.True(strategy.IsTimedOut(TimeSpan.FromMinutes(30)));
        Assert.True(strategy.IsTimedOut(TimeSpan.FromMinutes(31)));
    }
}
