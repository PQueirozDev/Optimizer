using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace PQueirozOptimizer.BiosAdvisor.Detector;

/// <summary>
/// Leitura da placa NVIDIA pelo nvidia-smi que o driver instala em System32. Argumentos fixos, tempo limite e
/// processo encerrado se travar. Campos que o driver não conhece voltam como null em vez de inventar valor.
/// </summary>
public static class NvidiaSmi
{
    public static string ExePath => Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
    public static bool Available => File.Exists(ExePath);

    /// <summary>Executa com os argumentos dados; null se não existir, falhar ou passar do tempo.</summary>
    public static string? Run(TimeSpan timeout, params string[] args)
    {
        if (!Available) return null;
        try
        {
            var psi = new ProcessStartInfo(ExePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            var deadline = DateTime.UtcNow + timeout;
            if (!p.WaitForExit((int)timeout.TotalMilliseconds)) { try { p.Kill(true); } catch (InvalidOperationException) { } return null; }
            // O mesmo prazo vale para terminar de ler a saída (um processo filho pode segurar o pipe aberto)
            var left = deadline - DateTime.UtcNow;
            if (!output.Wait(left > TimeSpan.Zero ? left : TimeSpan.FromMilliseconds(200))) return null;
            return p.ExitCode == 0 ? output.Result : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return null; }
    }

    /// <summary>Consulta CSV sem cabeçalho e sem unidades; uma lista de campos por placa.</summary>
    public static List<string[]> Query(params string[] fields)
    {
        var text = Run(TimeSpan.FromSeconds(6), "--query-gpu=" + string.Join(",", fields), "--format=csv,noheader,nounits");
        return text is null ? new() : ParseCsv(text, fields.Length);
    }

    public static List<string[]> ParseCsv(string text, int fieldCount) => text
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.Split(',').Select(v => v.Trim()).ToArray())
        .Where(v => v.Length == fieldCount)
        .ToList();

    public static int? Int(string? value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : null;
    public static double? Double(string? value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>Total da janela BAR1 (MiB) de cada placa, na ordem do nvidia-smi.</summary>
    public static List<long> Bar1TotalsMb()
    {
        var text = Run(TimeSpan.FromSeconds(6), "-q", "-d", "MEMORY");
        return text is null ? new() : ParseBar1(text);
    }

    public static List<long> ParseBar1(string text)
    {
        var result = new List<long>();
        foreach (Match m in Regex.Matches(text, @"BAR1 Memory Usage\s*\r?\n\s*Total\s*:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase))
            result.Add(long.Parse(m.Groups[1].Value));
        return result;
    }

    /// <summary>Link PCIe da placa (por nome). Cada consulta é separada: um campo desconhecido não derruba os outros.</summary>
    public static Dictionary<string, Detector.GpuLinkReading> ReadLinks()
    {
        var map = new Dictionary<string, GpuLinkReading>(StringComparer.OrdinalIgnoreCase);
        var basic = Query("name", "pcie.link.gen.max", "pcie.link.width.max", "pcie.link.width.current");
        var gpuMax = Query("name", "pcie.link.gen.gpumax");
        var bars = Bar1TotalsMb();
        for (var i = 0; i < basic.Count; i++)
        {
            var row = basic[i];
            map.TryAdd(row[0], new GpuLinkReading(Int(row[1]), i < gpuMax.Count ? Int(gpuMax[i][1]) : null, Int(row[2]), Int(row[3]), i < bars.Count ? bars[i] : null));
        }
        return map;
    }
}

public sealed record GpuLinkReading(int? GenMax, int? GenGpuMax, int? WidthMax, int? WidthCurrent, long? Bar1Mb);
