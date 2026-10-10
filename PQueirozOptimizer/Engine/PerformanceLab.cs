using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using PQueirozOptimizer.BiosAdvisor.Benchmark;
using PQueirozOptimizer.BiosAdvisor.Monitoring;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer.Engine;

/// <summary>Uma leitura por segundo durante a captura. Null = não medido (nada é estimado para preencher).</summary>
public sealed record PerfSample(double Second, double? Fps, double? FrametimeMs, double? Cpu, double? Gpu, double? Ram, double? GpuTemp, double? GpuClock, double? GpuPower, double? CpuClock, double? PerfLimit);

/// <summary>Condições do teste: o que precisa ser igual para comparar dois resultados.</summary>
public sealed record CaptureConditions(string Cpu, string Gpu, string GpuDriver, string Windows, int Build, string PowerPlan, string Resolution, int RefreshHz, string AppVersion);

/// <summary>
/// Resultado de uma captura. FPS e frametime só existem quando vieram de dados reais de apresentação de quadros
/// (PresentMon ou CSV importado); sem isso a captura guarda só os sensores e diz isso.
/// </summary>
public sealed record PerfResult
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Label { get; init; } = "";
    public string? Process { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public double DurationSeconds { get; init; }
    /// <summary>"PresentMon", "CSV importado" ou "Só sensores".</summary>
    public string Source { get; init; } = "Só sensores";
    public int? Frames { get; init; }
    public double? AverageFps { get; init; }
    public double? MinimumFps { get; init; }
    public double? Low1Fps { get; init; }
    public double? Low01Fps { get; init; }
    public double? AverageFrametimeMs { get; init; }
    public double? P95FrametimeMs { get; init; }
    public double? P99FrametimeMs { get; init; }
    public double? CpuAvg { get; init; }
    public double? GpuAvg { get; init; }
    public double? RamAvg { get; init; }
    public double? GpuTempMax { get; init; }
    public double? GpuClockAvg { get; init; }
    public double? GpuPowerAvg { get; init; }
    public double? CpuClockAvg { get; init; }
    /// <summary>Segundos em que o Windows informou limite de desempenho da CPU (% Performance Limit &gt; 0).</summary>
    public int? LimitedSeconds { get; init; }
    public IReadOnlyList<string> ThrottleReasons { get; init; } = Array.Empty<string>();
    public CaptureConditions? Conditions { get; init; }
    public IReadOnlyList<PerfSample> Samples { get; init; } = Array.Empty<PerfSample>();
    /// <summary>Experimento do Optimization Lab a que pertence (antes/depois), se houver.</summary>
    public string? LabExperiment { get; init; }
    public string? LabPhase { get; init; }
    public bool HasFrames => AverageFps is not null;
}

/// <summary>Cálculo das métricas a partir dos frametimes e das leituras de sensores.</summary>
public static class PerfMetrics
{
    public static PerfResult Build(PerfResult header, IReadOnlyList<double>? frametimesMs, IReadOnlyList<PerfSample> samples, IReadOnlyList<string>? throttleReasons = null)
    {
        static double? Avg(IEnumerable<double?> v) { var l = v.Where(x => x.HasValue).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : l.Average(); }
        static double? Max(IEnumerable<double?> v) { var l = v.Where(x => x.HasValue).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : l.Max(); }
        var result = header with
        {
            Samples = samples,
            CpuAvg = Avg(samples.Select(s => s.Cpu)), GpuAvg = Avg(samples.Select(s => s.Gpu)), RamAvg = Avg(samples.Select(s => s.Ram)),
            GpuTempMax = Max(samples.Select(s => s.GpuTemp)), GpuClockAvg = Avg(samples.Select(s => s.GpuClock)), GpuPowerAvg = Avg(samples.Select(s => s.GpuPower)),
            CpuClockAvg = Avg(samples.Select(s => s.CpuClock)),
            LimitedSeconds = samples.Any(s => s.PerfLimit.HasValue) ? samples.Count(s => s.PerfLimit is > 0.5) : null,
            ThrottleReasons = throttleReasons ?? Array.Empty<string>(),
            DurationSeconds = header.DurationSeconds > 0 ? header.DurationSeconds : samples.Count,
        };
        if (frametimesMs is null || frametimesMs.Count < 100) return result;
        var metrics = FrametimeAnalyzer.Compute(frametimesMs, header.Process);
        var sorted = frametimesMs.OrderBy(x => x).ToArray();
        return result with
        {
            Frames = metrics.Frames, AverageFps = metrics.AverageFps, Low1Fps = metrics.Low1Fps, Low01Fps = metrics.Low01Fps, AverageFrametimeMs = metrics.AverageFrametimeMs,
            P95FrametimeMs = FrametimeAnalyzer.Percentile(sorted, 0.95), P99FrametimeMs = FrametimeAnalyzer.Percentile(sorted, 0.99),
            MinimumFps = MinimumPerSecond(frametimesMs),
            DurationSeconds = Math.Max(result.DurationSeconds, metrics.DurationSeconds),
        };
    }

    /// <summary>FPS mínimo: o pior segundo completo (quadros contados em janelas de 1 s do tempo real dos quadros).</summary>
    public static double? MinimumPerSecond(IReadOnlyList<double> frametimesMs)
    {
        var buckets = new List<int>();
        double elapsed = 0; var count = 0;
        foreach (var ms in frametimesMs)
        {
            elapsed += ms; count++;
            while (elapsed >= 1000) { buckets.Add(count); count = 0; elapsed -= 1000; }
        }
        return buckets.Count == 0 ? null : buckets.Min();
    }

    /// <summary>Linha da comparação: valor antes, depois, diferença absoluta e percentual.</summary>
    public sealed record Delta(string Metric, double Before, double After, double Absolute, double? Percent, bool HigherIsBetter, string Unit);

    public static List<Delta> Compare(PerfResult before, PerfResult after)
    {
        var list = new List<Delta>();
        void Add(string name, double? a, double? b, bool higherBetter, string unit)
        {
            if (a is not { } x || b is not { } y) return;
            list.Add(new(name, x, y, y - x, x != 0 ? (y - x) / x * 100 : null, higherBetter, unit));
        }
        Add("FPS médio", before.AverageFps, after.AverageFps, true, "FPS");
        Add("FPS mínimo", before.MinimumFps, after.MinimumFps, true, "FPS");
        Add("1% low", before.Low1Fps, after.Low1Fps, true, "FPS");
        Add("0,1% low", before.Low01Fps, after.Low01Fps, true, "FPS");
        Add("Frametime médio", before.AverageFrametimeMs, after.AverageFrametimeMs, false, "ms");
        Add("Frametime P95", before.P95FrametimeMs, after.P95FrametimeMs, false, "ms");
        Add("Frametime P99", before.P99FrametimeMs, after.P99FrametimeMs, false, "ms");
        Add("Uso de CPU", before.CpuAvg, after.CpuAvg, false, "%");
        Add("Uso de GPU", before.GpuAvg, after.GpuAvg, true, "%");
        Add("Memória", before.RamAvg, after.RamAvg, false, "%");
        Add("Temperatura da GPU (máx.)", before.GpuTempMax, after.GpuTempMax, false, "°C");
        Add("Clock da GPU", before.GpuClockAvg, after.GpuClockAvg, true, "MHz");
        Add("Potência da GPU", before.GpuPowerAvg, after.GpuPowerAvg, false, "W");
        Add("Clock da CPU (estimado)", before.CpuClockAvg, after.CpuClockAvg, true, "MHz");
        return list;
    }

    /// <summary>Diferenças de condições que invalidam ou enfraquecem a comparação.</summary>
    public static List<string> ConditionDifferences(PerfResult a, PerfResult b)
    {
        var list = new List<string>();
        if (!string.Equals(a.Process, b.Process, StringComparison.OrdinalIgnoreCase)) list.Add($"Processos diferentes ({a.Process ?? "nenhum"} e {b.Process ?? "nenhum"}).");
        if (a.Conditions is not { } x || b.Conditions is not { } y) return list;
        if (x.Gpu != y.Gpu || x.Cpu != y.Cpu) list.Add("Hardware diferente.");
        if (x.GpuDriver != y.GpuDriver) list.Add($"Driver de vídeo diferente ({x.GpuDriver} e {y.GpuDriver}).");
        if (x.Build != y.Build) list.Add($"Versão do Windows diferente (build {x.Build} e {y.Build}).");
        if (x.Resolution != y.Resolution || x.RefreshHz != y.RefreshHz) list.Add($"Resolução ou taxa de atualização diferente ({x.Resolution} {x.RefreshHz} Hz e {y.Resolution} {y.RefreshHz} Hz).");
        if (x.PowerPlan != y.PowerPlan) list.Add($"Plano de energia diferente ({x.PowerPlan} e {y.PowerPlan}).");
        return list;
    }
}

/// <summary>Histórico de capturas em %LocalAppData%\PQueirozOptimizer\PerformanceLab (um JSON por teste).</summary>
public sealed class PerfLabStore
{
    public static PerfLabStore Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "PerformanceLab"));
    private readonly string _directory;
    public PerfLabStore(string directory) => _directory = directory;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private string PathFor(string id) => Path.Combine(_directory, string.Concat(id.Where(char.IsLetterOrDigit)) + ".json");

    public void Save(PerfResult result)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(result.Id);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(result, Options));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public IReadOnlyList<PerfResult> List()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<PerfResult>();
        var list = new List<PerfResult>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try { if (JsonSerializer.Deserialize<PerfResult>(File.ReadAllText(file)) is { } r) list.Add(r); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return list.OrderByDescending(r => r.StartedAt).ToList();
    }

    public void Delete(string id) { try { File.Delete(PathFor(id)); } catch (IOException) { } }
}

/// <summary>Exportação em CSV (resumo + leituras por segundo) e JSON (resultado completo).</summary>
public static class PerfExport
{
    private static string F(double? v) => v is { } x ? x.ToString("0.###", CultureInfo.InvariantCulture) : "";

    public static string ToCsv(PerfResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("metric,value");
        void Row(string k, string v) => sb.AppendLine($"{k},\"{v.Replace("\"", "\"\"")}\"");
        Row("label", r.Label); Row("process", r.Process ?? ""); Row("started", r.StartedAt.ToString("s")); Row("duration_s", F(r.DurationSeconds)); Row("source", r.Source);
        Row("frames", r.Frames?.ToString() ?? ""); Row("avg_fps", F(r.AverageFps)); Row("min_fps", F(r.MinimumFps)); Row("low1_fps", F(r.Low1Fps)); Row("low01_fps", F(r.Low01Fps));
        Row("frametime_avg_ms", F(r.AverageFrametimeMs)); Row("frametime_p95_ms", F(r.P95FrametimeMs)); Row("frametime_p99_ms", F(r.P99FrametimeMs));
        Row("cpu_avg_pct", F(r.CpuAvg)); Row("gpu_avg_pct", F(r.GpuAvg)); Row("ram_avg_pct", F(r.RamAvg)); Row("gpu_temp_max_c", F(r.GpuTempMax));
        Row("gpu_clock_avg_mhz", F(r.GpuClockAvg)); Row("gpu_power_avg_w", F(r.GpuPowerAvg)); Row("cpu_clock_est_mhz", F(r.CpuClockAvg)); Row("cpu_limited_s", r.LimitedSeconds?.ToString() ?? "");
        if (r.Conditions is { } c) { Row("cpu", c.Cpu); Row("gpu", c.Gpu); Row("gpu_driver", c.GpuDriver); Row("windows", $"{c.Windows} ({c.Build})"); Row("power_plan", c.PowerPlan); Row("display", $"{c.Resolution} {c.RefreshHz} Hz"); Row("app_version", c.AppVersion); }
        sb.AppendLine();
        sb.AppendLine("second,fps,frametime_ms,cpu_pct,gpu_pct,ram_pct,gpu_temp_c,gpu_clock_mhz,gpu_power_w,cpu_clock_est_mhz,cpu_perf_limit_pct");
        foreach (var s in r.Samples)
            sb.AppendLine(string.Join(",", F(s.Second), F(s.Fps), F(s.FrametimeMs), F(s.Cpu), F(s.Gpu), F(s.Ram), F(s.GpuTemp), F(s.GpuClock), F(s.GpuPower), F(s.CpuClock), F(s.PerfLimit)));
        return sb.ToString();
    }

    public static string ToJson(PerfResult r) => JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>
/// PresentMon (ferramenta aberta da Intel) escolhido pelo usuário. Como o SCEWIN, é copiado para a pasta protegida
/// antes de ser executado: o app roda elevado e não executa direto de uma pasta gravável.
/// </summary>
public static class PresentMonTool
{
    private static string Directory_ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PQueirozOptimizer", "PresentMon");
    private static string PathFile => Path.Combine(Directory_, "presentmon.txt");

    public static string? ToolPath()
    {
        try
        {
            var saved = File.Exists(PathFile) ? File.ReadAllText(PathFile).Trim() : null;
            return saved != null && File.Exists(saved) && Path.GetFullPath(saved).StartsWith(Directory_ + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? saved : null;
        }
        catch (IOException) { return null; }
    }

    public static void SetToolPath(string exePath)
    {
        var name = Path.GetFileName(exePath);
        if (!name.Contains("PresentMon", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Escolha o executável do PresentMon (PresentMon-2.x-x64.exe).");
        if (new FileInfo(exePath).Length > 64L * 1024 * 1024) throw new InvalidOperationException("Arquivo grande demais para ser o PresentMon.");
        RegistryTweakStore.EnsureProtectedDirectory(Directory_);
        var staged = Path.Combine(Directory_, "PresentMon.exe");
        File.Delete(staged);
        File.Copy(exePath, staged);
        File.WriteAllText(PathFile, staged);
    }

    /// <summary>Argumentos do PresentMon 2.x: um processo, saída em CSV, sem estatísticas no console.</summary>
    public static IReadOnlyList<string> Arguments(string processName, string outputFile, int seconds) => new[]
    {
        "--process_name", processName, "--output_file", outputFile, "--timed", seconds.ToString(CultureInfo.InvariantCulture), "--terminate_after_timed",
        "--stop_existing_session", "--no_console_stats", "--session_name", "QrztweaksPerfLab",
    };
}

/// <summary>
/// Leitura incremental do CSV que o PresentMon vai escrevendo: só linhas completas, só o processo escolhido.
/// Permite o gráfico de FPS ao vivo com dados reais de apresentação.
/// </summary>
public sealed class FrameCsvTail
{
    private readonly string _path;
    private long _position;
    private string _partial = "";
    private int _frametimeColumn = -1, _appColumn = -1;
    private readonly string? _process;
    public List<double> Frametimes { get; } = new();

    public FrameCsvTail(string path, string? process) { _path = path; _process = process is null ? null : Path.GetFileName(process); }

    /// <summary>Lê o que foi acrescentado desde a última chamada. Devolve os frametimes novos.</summary>
    public List<double> ReadNew()
    {
        var fresh = new List<double>();
        if (!File.Exists(_path)) return fresh;
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < _position) { _position = 0; _partial = ""; } // arquivo recriado
        stream.Seek(_position, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = _partial + reader.ReadToEnd();
        _position = stream.Length;
        var lines = text.Split('\n');
        _partial = lines[^1]; // última linha pode estar incompleta
        foreach (var raw in lines[..^1])
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var cells = line.Split(',');
            if (_frametimeColumn < 0)
            {
                _frametimeColumn = Array.FindIndex(cells, c => c is "MsBetweenPresents" or "FrameTime");
                _appColumn = Array.FindIndex(cells, c => c is "Application" or "ProcessName");
                continue;
            }
            if (cells.Length <= _frametimeColumn) continue;
            if (_process is not null && _appColumn >= 0 && cells.Length > _appColumn && !cells[_appColumn].Equals(_process, StringComparison.OrdinalIgnoreCase)) continue;
            if (double.TryParse(cells[_frametimeColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) && ms > 0 && !double.IsInfinity(ms)) fresh.Add(ms);
        }
        Frametimes.AddRange(fresh);
        return fresh;
    }
}

/// <summary>Coleta de uma captura: sensores a cada segundo e, com PresentMon, os quadros reais.</summary>
public sealed class PerfCaptureSession : IDisposable
{
    private readonly SensorReader _sensors;
    private readonly FrameCsvTail? _tail;
    private readonly Process? _presentMon;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<PerfSample> _samples = new();
    private readonly HashSet<string> _throttle = new();
    public string? CsvPath { get; }
    public string? Process { get; }
    public IReadOnlyList<PerfSample> Samples => _samples;
    public bool CapturingFrames => _presentMon is not null;

    private PerfCaptureSession(SensorReader sensors, string? process, string? csv, Process? presentMon)
    {
        _sensors = sensors; Process = process; CsvPath = csv; _presentMon = presentMon;
        if (csv is not null) _tail = new FrameCsvTail(csv, process);
    }

    /// <summary>Inicia a captura. Sem PresentMon (ou sem processo), grava só os sensores.</summary>
    public static PerfCaptureSession Start(BiosAdvisor.HardwareProfile hardware, string? process, int maxSeconds)
    {
        var sensors = new SensorReader(hardware);
        var tool = PresentMonTool.ToolPath();
        if (tool is null || string.IsNullOrWhiteSpace(process)) return new PerfCaptureSession(sensors, process, null, null);
        var folder = Path.Combine(Path.GetTempPath(), "Qrztweaks-PerfLab");
        Directory.CreateDirectory(folder);
        var csv = Path.Combine(folder, $"{Guid.NewGuid():N}.csv");
        var psi = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in PresentMonTool.Arguments(process!, csv, maxSeconds)) psi.ArgumentList.Add(a);
        var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o PresentMon.");
        p.StandardOutput.ReadToEndAsync(); p.StandardError.ReadToEndAsync();
        return new PerfCaptureSession(sensors, process, csv, p);
    }

    /// <summary>Uma leitura (chamar a cada segundo). Devolve a amostra com o FPS daquele segundo, se houver quadros.</summary>
    public PerfSample Tick()
    {
        var s = _sensors.Read();
        double? fps = null, frametime = null;
        if (_tail is not null)
        {
            try
            {
                var fresh = _tail.ReadNew();
                if (fresh.Count > 0) { frametime = fresh.Average(); fps = 1000 / frametime; }
            }
            catch (IOException) { }
        }
        if (s.GpuThrottleReasons is { } reasons) foreach (var r in reasons) _throttle.Add(r.Pt);
        var latest = HardwareMonitorService.Shared.Latest;
        var sample = new PerfSample(Math.Round(_clock.Elapsed.TotalSeconds), fps, frametime, s.CpuUsage, s.GpuUsage, latest?.Ram, s.GpuTemperature, s.GpuClockMhz, s.GpuPowerWatts, s.CpuClockMhz, s.CpuPerformanceLimit);
        _samples.Add(sample);
        return sample;
    }

    public bool PresentMonExited => _presentMon is { HasExited: true };

    /// <summary>Encerra e monta o resultado com as métricas calculadas dos quadros reais.</summary>
    public PerfResult Finish(string label, CaptureConditions conditions)
    {
        StopPresentMon();
        List<double>? frames = null;
        if (_tail is not null)
        {
            try { _tail.ReadNew(); } catch (IOException) { }
            frames = _tail.Frametimes;
        }
        var header = new PerfResult
        {
            Label = label, Process = Process, StartedAt = DateTime.Now - _clock.Elapsed, DurationSeconds = Math.Round(_clock.Elapsed.TotalSeconds),
            Source = frames is { Count: >= 100 } ? "PresentMon" : "Só sensores", Conditions = conditions,
        };
        return PerfMetrics.Build(header, frames is { Count: >= 100 } ? frames : null, _samples, _throttle.ToList());
    }

    private void StopPresentMon()
    {
        if (_presentMon is null || _presentMon.HasExited) return;
        try { _presentMon.Kill(true); _presentMon.WaitForExit(5000); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    public void Dispose()
    {
        StopPresentMon();
        _presentMon?.Dispose();
        _sensors.Dispose();
        try { if (CsvPath is not null) File.Delete(CsvPath); } catch (IOException) { }
    }
}
