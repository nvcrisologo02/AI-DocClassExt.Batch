using System;

namespace DocumentIA.Batch.ClassificationLite.Engine;

/// <summary>
/// Polling adaptativo por documento: sondeos rapidos al inicio (detecta docs rapidos)
/// con backoff hasta el intervalo configurado (no castiga el status endpoint en docs lentos).
/// </summary>
public class AdaptivePollingStrategy
{
    private static readonly int[] InitialDelaysSeconds = { 3, 5, 10, 20, 30 };

    private readonly int _steadyIntervalSeconds;
    private readonly TimeSpan _timeout;

    public AdaptivePollingStrategy(int steadyIntervalSeconds, int timeoutMinutes = 30)
    {
        _steadyIntervalSeconds = Math.Max(1, steadyIntervalSeconds);
        _timeout = TimeSpan.FromMinutes(timeoutMinutes);
    }

    public TimeSpan GetDelay(int attempt)
    {
        var seconds = attempt >= 0 && attempt < InitialDelaysSeconds.Length
            ? Math.Min(InitialDelaysSeconds[attempt], _steadyIntervalSeconds)
            : _steadyIntervalSeconds;
        return TimeSpan.FromSeconds(seconds);
    }

    public bool IsTimedOut(TimeSpan elapsed) => elapsed >= _timeout;
}
