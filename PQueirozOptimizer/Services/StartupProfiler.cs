using System.Diagnostics;
using System.IO;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Tempos da abertura do app, contados desde o início do processo. Com PQO_STARTUP_PROFILE apontando
/// para um arquivo, as marcas são gravadas nele e o app fecha assim que a Visão geral fica utilizável
/// (usado para medir a inicialização sem interação). PQO_PROFILE_IDLE (segundos) e PQO_PROFILE_NAV=1
/// acrescentam a medição do app parado (CPU, memória, quadros) e da troca de páginas
/// (tools\Measure-UiPerformance.ps1).
/// </summary>
public static class StartupProfiler
{
    private static readonly DateTime ProcessStart = ReadProcessStart();
    private static readonly List<(string Name, double Ms)> Marks = new();
    private static readonly List<(string Name, string Value)> Metrics = new();
    public static string? OutputPath { get; } = Environment.GetEnvironmentVariable("PQO_STARTUP_PROFILE");
    public static bool Enabled => OutputPath is not null;

    /// <summary>Segundos medindo o app parado na Visão geral depois de utilizável (0 = não mede).</summary>
    public static int IdleSeconds { get; } = int.TryParse(Environment.GetEnvironmentVariable("PQO_PROFILE_IDLE"), out var s) ? Math.Clamp(s, 0, 600) : 0;

    /// <summary>Percorre todas as páginas medindo o tempo de cada troca.</summary>
    public static bool NavigationPass { get; } = Environment.GetEnvironmentVariable("PQO_PROFILE_NAV") == "1";

    private static DateTime ReadProcessStart()
    {
        try { using var p = Process.GetCurrentProcess(); return p.StartTime; }
        catch { return DateTime.Now; }
    }

    public static double Elapsed => (DateTime.Now - ProcessStart).TotalMilliseconds;

    public static void Mark(string name)
    {
        lock (Marks) Marks.Add((name, Elapsed));
    }

    public static void Metric(string name, string value)
    {
        lock (Marks) Metrics.Add((name, value));
    }

    /// <summary>Grava as marcas (só no modo de medição). Devolve true quando o app deve fechar.</summary>
    public static bool Finish()
    {
        if (OutputPath is null) return false;
        lock (Marks)
        {
            try { File.WriteAllLines(OutputPath, Marks.Select(m => $"{m.Name}\t{m.Ms:F0}").Concat(Metrics.Select(m => $"{m.Name}\t{m.Value}"))); }
            catch (IOException) { }
        }
        return true;
    }
}
