namespace DocumentIA.Batch.Evaluation.Metrics;

/// <summary>
/// Test de McNemar exacto: sobre los pares discordantes (b01 = A acierta/B falla, b10 = A
/// falla/B acierta) el p-valor de dos colas es el de un test binomial exacto con p=0.5 sobre
/// n=b01+b10. Se usa la forma exacta para cualquier n (estable numericamente via la recurrencia
/// de la pmf, sin desbordar); la alternativa chi-cuadrado con correccion de continuidad que
/// sugiere el enunciado para n&gt;25 es una aproximacion de esta misma exacta, así que no aporta
/// aquí y se omite a proposito.
/// </summary>
public static class McNemarTest
{
    public static double ExactTwoSidedPValue(int b01, int b10)
    {
        if (b01 < 0 || b10 < 0)
        {
            throw new ArgumentOutOfRangeException(b01 < 0 ? nameof(b01) : nameof(b10), "No puede ser negativo.");
        }

        var n = b01 + b10;
        if (n == 0)
        {
            // Sin pares discordantes: no hay evidencia de diferencia.
            return 1.0;
        }

        var k = Math.Min(b01, b10);
        var cumulative = CumulativeBinomialAtMost(n, k);
        return Math.Min(1.0, 2.0 * cumulative);
    }

    /// <summary>P(X &lt;= k) para X ~ Binomial(n, 0.5), via recurrencia de la pmf (estable, sin factoriales).</summary>
    private static double CumulativeBinomialAtMost(int n, int k)
    {
        var pmf = Math.Pow(0.5, n);
        var cumulative = pmf;
        for (var i = 1; i <= k; i++)
        {
            pmf *= (double)(n - i + 1) / i;
            cumulative += pmf;
        }

        return cumulative;
    }
}
