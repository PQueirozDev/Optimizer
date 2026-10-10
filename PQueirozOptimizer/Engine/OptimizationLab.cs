namespace PQueirozOptimizer.Engine;

public enum LabVerdict { InsufficientData, NoProvenDifference, LikelyGain, LikelyLoss }

/// <summary>Resultado de um experimento: médias, variação, teste estatístico e o que pode ter atrapalhado.</summary>
public sealed record LabOutcome(
    LabVerdict Verdict, string Metric, double MeanBefore, double MeanAfter, double DeltaPercent, double? PValue,
    double CvBefore, double CvAfter, double? Low1DeltaPercent, IReadOnlyList<string> Warnings, string Recommendation);

/// <summary>
/// Optimization Lab (fase 5): compara gravações antes e depois de um único ajuste. Uma diferença só conta como
/// ganho (ou perda) se for estatisticamente consistente (Welch, p &lt; 0,05) <b>e</b> maior que o mínimo prático;
/// diferenças pequenas ficam como "sem diferença comprovada".
/// </summary>
public static class OptimizationLab
{
    public const int MinimumRuns = 3;
    /// <summary>Diferença mínima que importa na prática (abaixo disso é ruído de medição mesmo se "significativa").</summary>
    public const double PracticalPercent = 3;
    public const double HighVariationCv = 5;

    public static LabOutcome Evaluate(IReadOnlyList<PerfResult> before, IReadOnlyList<PerfResult> after)
    {
        var warnings = new List<string>();
        var metric = before.Concat(after).All(r => r.HasFrames) ? "FPS médio" : "Uso de CPU";
        double Value(PerfResult r) => metric == "FPS médio" ? r.AverageFps!.Value : r.CpuAvg ?? 0;
        if (before.Count < MinimumRuns || after.Count < MinimumRuns)
            return new(LabVerdict.InsufficientData, metric, 0, 0, 0, null, 0, 0, null, new[] { $"Faça pelo menos {MinimumRuns} gravações antes e {MinimumRuns} depois." }, "Grave mais vezes antes de decidir.");
        if (metric != "FPS médio")
            warnings.Add("Sem quadros reais (PresentMon) em todas as gravações: o resultado usa só o uso de CPU e não diz nada sobre FPS.");

        var a = before.Select(Value).ToArray();
        var b = after.Select(Value).ToArray();
        var (meanA, sdA) = MeanSd(a);
        var (meanB, sdB) = MeanSd(b);
        var cvA = meanA > 0 ? sdA / meanA * 100 : 0;
        var cvB = meanB > 0 ? sdB / meanB * 100 : 0;
        var delta = meanA != 0 ? (meanB - meanA) / meanA * 100 : 0;
        var p = WelchPValue(a, b);
        double? low1 = before.All(r => r.Low1Fps.HasValue) && after.All(r => r.Low1Fps.HasValue)
            ? (after.Average(r => r.Low1Fps!.Value) - before.Average(r => r.Low1Fps!.Value)) / before.Average(r => r.Low1Fps!.Value) * 100 : null;

        if (cvA > HighVariationCv || cvB > HighVariationCv) warnings.Add($"Variação alta entre gravações (CV {cvA:0.#}% antes e {cvB:0.#}% depois): repita a mesma cena, com a mesma duração.");
        var tempsA = before.Select(r => r.GpuTempMax).OfType<double>().ToList();
        var tempsB = after.Select(r => r.GpuTempMax).OfType<double>().ToList();
        if (tempsA.Count > 0 && tempsB.Count > 0 && Math.Abs(tempsA.Average() - tempsB.Average()) > 5)
            warnings.Add($"Temperatura da GPU diferente entre as fases ({tempsA.Average():0} °C e {tempsB.Average():0} °C): o aquecimento muda o clock.");
        var all = before.Concat(after).ToList();
        if (all.Select(r => r.Process?.ToLowerInvariant()).Distinct().Count() > 1) warnings.Add("Nem todas as gravações são do mesmo processo.");
        if (all.Select(r => r.Conditions?.Resolution + r.Conditions?.RefreshHz).Distinct().Count() > 1) warnings.Add("A resolução ou a taxa de atualização mudou entre as gravações.");
        if (all.Select(r => r.Conditions?.GpuDriver).Distinct().Count() > 1) warnings.Add("O driver de vídeo mudou entre as gravações.");
        var cpuA = before.Select(r => r.CpuAvg).OfType<double>().DefaultIfEmpty().Average();
        var cpuB = after.Select(r => r.CpuAvg).OfType<double>().DefaultIfEmpty().Average();
        if (metric == "FPS médio" && Math.Abs(cpuA - cpuB) > 15) warnings.Add($"Uso de CPU muito diferente ({cpuA:0}% e {cpuB:0}%): pode haver outro programa interferindo.");
        if (all.Any(r => r.LimitedSeconds is > 0)) warnings.Add("O Windows informou limite de desempenho da CPU (térmico ou de energia) em alguma gravação.");

        var significant = p is < 0.05;
        var practical = Math.Abs(delta) >= PracticalPercent;
        var verdict = !significant || !practical ? LabVerdict.NoProvenDifference : delta > 0 == (metric == "FPS médio") ? LabVerdict.LikelyGain : LabVerdict.LikelyLoss;
        var recommendation = verdict switch
        {
            LabVerdict.LikelyGain => warnings.Count == 0 ? "Manter o ajuste: a melhora foi consistente nas gravações." : "Provável melhora, mas confira os avisos antes de considerar comprovado.",
            LabVerdict.LikelyLoss => "Reverter o ajuste: o desempenho piorou de forma consistente.",
            _ => $"Sem diferença comprovada (mínimo prático: {PracticalPercent:0}% com p < 0,05). Se o ajuste não tem outro motivo, reverter é o mais simples.",
        };
        return new(verdict, metric, meanA, meanB, delta, p, cvA, cvB, low1, warnings, recommendation);
    }

    public static (double Mean, double Sd) MeanSd(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        var sd = values.Count > 1 ? Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1)) : 0;
        return (mean, sd);
    }

    /// <summary>Valor-p bicaudal do teste t de Welch. Null quando as duas amostras não variam (sem como estimar).</summary>
    public static double? WelchPValue(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        var (ma, sa) = MeanSd(a); var (mb, sb) = MeanSd(b);
        var va = sa * sa / a.Count; var vb = sb * sb / b.Count;
        if (va + vb <= 0) return ma == mb ? 1 : 0;
        var t = (ma - mb) / Math.Sqrt(va + vb);
        var df = (va + vb) * (va + vb) / (va * va / (a.Count - 1) + vb * vb / (b.Count - 1));
        // p = I_{df/(df+t²)}(df/2, 1/2)
        return RegularizedIncompleteBeta(df / (df + t * t), df / 2, 0.5);
    }

    /// <summary>Beta incompleta regularizada I_x(a,b) por fração contínua (Numerical Recipes, betacf).</summary>
    public static double RegularizedIncompleteBeta(double x, double a, double b)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        var lnBeta = LogGamma(a + b) - LogGamma(a) - LogGamma(b);
        var front = Math.Exp(lnBeta + a * Math.Log(x) + b * Math.Log(1 - x));
        return x < (a + 1) / (a + b + 2) ? front * BetaFraction(x, a, b) / a : 1 - front * BetaFraction(1 - x, b, a) / b;
    }

    private static double BetaFraction(double x, double a, double b)
    {
        const double eps = 1e-12, fpmin = 1e-300;
        double qab = a + b, qap = a + 1, qam = a - 1, c = 1, d = 1 - qab * x / qap;
        if (Math.Abs(d) < fpmin) d = fpmin;
        d = 1 / d; var h = d;
        for (var m = 1; m <= 300; m++)
        {
            var m2 = 2 * m;
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d; if (Math.Abs(d) < fpmin) d = fpmin;
            c = 1 + aa / c; if (Math.Abs(c) < fpmin) c = fpmin;
            d = 1 / d; h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d; if (Math.Abs(d) < fpmin) d = fpmin;
            c = 1 + aa / c; if (Math.Abs(c) < fpmin) c = fpmin;
            d = 1 / d;
            var del = d * c; h *= del;
            if (Math.Abs(del - 1) < eps) break;
        }
        return h;
    }

    /// <summary>ln Γ(x) pela aproximação de Lanczos.</summary>
    public static double LogGamma(double x)
    {
        double[] g = { 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
        if (x < 0.5) return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1 - x);
        x -= 1;
        var sum = 0.99999999999980993;
        for (var i = 0; i < g.Length; i++) sum += g[i] / (x + i + 1);
        var t = x + g.Length - 0.5;
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(sum);
    }
}
