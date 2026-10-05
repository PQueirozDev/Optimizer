using PQueirozOptimizer.BiosAdvisor.Monitoring;

namespace PQueirozOptimizer.BiosAdvisor.Benchmark;

public enum BenchmarkSlot { Baseline, After }

/// <summary>
/// Um teste salvo (antes ou depois). Cada métrica é opcional: só existe se foi realmente medida
/// (FPS do log importado; temperaturas e uso da gravação de sensores).
/// </summary>
public sealed record BenchmarkRun
{
    public BenchmarkSlot Slot { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public string? SourceFile { get; init; }
    public string? Application { get; init; }
    public int? Frames { get; init; }
    public double? AverageFps { get; init; }
    public double? Low1Fps { get; init; }
    public double? Low01Fps { get; init; }
    public double? AverageFrametimeMs { get; init; }
    public double? CpuTempMax { get; init; }
    public double? GpuTempMax { get; init; }
    public double? CpuUsageAvg { get; init; }
    public double? GpuUsageAvg { get; init; }
    public double? CpuClockAvg { get; init; }
    public int? SensorSeconds { get; init; }
    /// <summary>Versão da BIOS no momento do teste (para lembrar o que mudou).</summary>
    public string? BiosVersion { get; init; }

    public BenchmarkRun WithFrames(FrameMetrics m, string file) => this with
    {
        SourceFile = file, Application = m.Application, Frames = m.Frames, AverageFps = m.AverageFps, Low1Fps = m.Low1Fps, Low01Fps = m.Low01Fps, AverageFrametimeMs = m.AverageFrametimeMs,
    };

    public BenchmarkRun WithSensors(IReadOnlyList<SensorSnapshot> samples)
    {
        static double? Max(IEnumerable<double?> v) => v.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(double.NaN).Max() is var m && !double.IsNaN(m) ? m : null;
        static double? Avg(IEnumerable<double?> v) => v.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(double.NaN).Average() is var a && !double.IsNaN(a) ? a : null;
        return this with
        {
            CpuTempMax = Max(samples.Select(s => s.CpuTemperature)), GpuTempMax = Max(samples.Select(s => s.GpuTemperature)),
            CpuUsageAvg = Avg(samples.Select(s => s.CpuUsage)), GpuUsageAvg = Avg(samples.Select(s => s.GpuUsage)),
            CpuClockAvg = Avg(samples.Select(s => s.CpuClockMhz)), SensorSeconds = samples.Count,
        };
    }
}

/// <summary>Uma linha da comparação. Delta em % para FPS; em unidades absolutas para temperatura e uso.</summary>
public sealed record BenchmarkDelta(LocalizedText Metric, double Before, double After, double Change, bool IsPercent, bool HigherIsBetter, string Unit);

public static class BenchmarkComparer
{
    /// <summary>Compara só as métricas presentes nos dois testes.</summary>
    public static List<BenchmarkDelta> Compare(BenchmarkRun before, BenchmarkRun after)
    {
        var list = new List<BenchmarkDelta>();
        void Percent(LocalizedText name, double? a, double? b, bool higherBetter, string unit)
        {
            if (a is { } x && b is { } y && x > 0) list.Add(new(name, x, y, (y - x) / x * 100, true, higherBetter, unit));
        }
        void Absolute(LocalizedText name, double? a, double? b, bool higherBetter, string unit)
        {
            if (a is { } x && b is { } y) list.Add(new(name, x, y, y - x, false, higherBetter, unit));
        }
        Percent(new("FPS médio", "Average FPS"), before.AverageFps, after.AverageFps, true, "FPS");
        Percent(new("1% low", "1% low"), before.Low1Fps, after.Low1Fps, true, "FPS");
        Percent(new("0.1% low", "0.1% low"), before.Low01Fps, after.Low01Fps, true, "FPS");
        Percent(new("Frametime médio", "Average frametime"), before.AverageFrametimeMs, after.AverageFrametimeMs, false, "ms");
        Absolute(new("CPU máx.", "CPU max"), before.CpuTempMax, after.CpuTempMax, false, "°C");
        Absolute(new("GPU máx.", "GPU max"), before.GpuTempMax, after.GpuTempMax, false, "°C");
        Absolute(new("Uso médio da CPU", "Average CPU usage"), before.CpuUsageAvg, after.CpuUsageAvg, false, "%");
        Absolute(new("Uso médio da GPU", "Average GPU usage"), before.GpuUsageAvg, after.GpuUsageAvg, true, "%");
        return list;
    }
}
