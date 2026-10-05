using System.Globalization;
using System.IO;

namespace PQueirozOptimizer.BiosAdvisor.Benchmark;

/// <summary>Métricas de um log de frametimes. 0.1% low fica null com menos de 1000 quadros (amostra pequena demais).</summary>
public sealed record FrameMetrics(int Frames, double DurationSeconds, double AverageFps, double Low1Fps, double? Low01Fps, double AverageFrametimeMs, string? Application);

/// <summary>
/// Lê CSV do PresentMon (e de ferramentas que exportam no mesmo formato, como CapFrameX e OCAT).
/// Colunas aceitas: MsBetweenPresents (PresentMon 1.x) ou FrameTime / msBetweenPresents (2.x).
/// Com vários programas no mesmo log, usa o que tem mais quadros. "1% low" aqui é o FPS do percentil 99 do frametime
/// (1000 / P99), com interpolação linear; outras ferramentas usam a média do 1% mais lento e podem dar valores diferentes.
/// </summary>
public static class FrametimeAnalyzer
{
    private static readonly string[] FrametimeColumns = { "MsBetweenPresents", "FrameTime", "msBetweenPresents", "MsBetweenDisplayChange" };
    private static readonly string[] AppColumns = { "Application", "ProcessName" };

    public static FrameMetrics ParseCsv(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var headerIndex = Array.FindIndex(lines, l => FrametimeColumns.Any(c => SplitCsv(l).Contains(c, StringComparer.OrdinalIgnoreCase)));
        if (headerIndex < 0) throw new FormatException("Arquivo sem coluna de frametime (MsBetweenPresents ou FrameTime). Use um CSV do PresentMon ou CapFrameX.");
        var header = SplitCsv(lines[headerIndex]);
        var col = FrametimeColumns.Select(c => Array.FindIndex(header, h => h.Equals(c, StringComparison.OrdinalIgnoreCase))).First(i => i >= 0);
        int Column(params string[] names) => names.Select(c => Array.FindIndex(header, h => h.Equals(c, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(i => i >= 0, -1);
        var appCol = Column(AppColumns);
        var pidCol = Column("ProcessID");
        var swapCol = Column("SwapChainAddress");

        // Um fluxo de quadros = programa + processo + swapchain (o mesmo jogo pode ter mais de uma janela/processo)
        var streams = new Dictionary<string, (string App, List<double> Frames)>(StringComparer.OrdinalIgnoreCase);
        string Cell(string[] cells, int i) => i >= 0 && cells.Length > i ? cells[i] : "";
        foreach (var line in lines.Skip(headerIndex + 1))
        {
            var cells = SplitCsv(line);
            if (cells.Length <= col) continue;
            // Só valores inválidos ficam de fora (0, negativos, NaN); travadas longas são reais e entram na conta
            if (!double.TryParse(cells[col], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || double.IsNaN(ms) || double.IsInfinity(ms) || ms <= 0) continue;
            var app = Cell(cells, appCol);
            var key = $"{app}|{Cell(cells, pidCol)}|{Cell(cells, swapCol)}";
            if (!streams.TryGetValue(key, out var stream)) streams[key] = stream = (app, new List<double>());
            stream.Frames.Add(ms);
        }
        var best = streams.Values.OrderByDescending(s => s.Frames.Count).FirstOrDefault();
        return Compute(best.Frames ?? new List<double>(), string.IsNullOrEmpty(best.App) ? null : best.App);
    }

    public static FrameMetrics Compute(IReadOnlyList<double> frametimesMs, string? application = null)
    {
        if (frametimesMs.Count < 100) throw new FormatException("O log tem poucos quadros (mínimo 100). Grave pelo menos 30 segundos de jogo.");
        var total = frametimesMs.Sum();
        var sorted = frametimesMs.OrderBy(x => x).ToArray();
        var p99 = Percentile(sorted, 0.99);
        double? p999 = sorted.Length >= 1000 ? Percentile(sorted, 0.999) : null;
        return new FrameMetrics(sorted.Length, total / 1000, sorted.Length / (total / 1000), 1000 / p99, p999 is { } x ? 1000 / x : null, total / sorted.Length, application);
    }

    /// <summary>Percentil com interpolação linear entre as posições vizinhas.</summary>
    public static double Percentile(double[] sorted, double q)
    {
        if (sorted.Length == 0) return double.NaN;
        var pos = (sorted.Length - 1) * q;
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    private static string[] SplitCsv(string line) => line.Split(',').Select(c => c.Trim().Trim('"')).ToArray();

    public static FrameMetrics ParseFile(string path) => ParseCsv(File.ReadAllText(path));
}
