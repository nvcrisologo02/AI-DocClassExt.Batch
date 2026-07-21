namespace DocumentIA.Batch.Evaluation.Metrics;

/// <summary>Accierto/total mas intervalo de confianza binomial aproximado (Wald, ±1.96·sqrt(p(1-p)/n)).</summary>
public sealed record AccuracyStat(int Correct, int Total)
{
    public double Accuracy => Total == 0 ? 0d : (double)Correct / Total;

    public double CiMargin
    {
        get
        {
            if (Total == 0)
            {
                return 0d;
            }

            var p = Accuracy;
            return 1.96 * Math.Sqrt(p * (1 - p) / Total);
        }
    }

    public double CiLower => Math.Max(0d, Accuracy - CiMargin);

    public double CiUpper => Math.Min(1d, Accuracy + CiMargin);
}
