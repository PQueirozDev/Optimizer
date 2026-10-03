using System.Diagnostics;
using System.IO;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Tempos da abertura do app, contados desde o início do processo. Com PQO_STARTUP_PROFILE apontando
/// para um arquivo, as marcas são gravadas nele e o app fecha assim que a Visão geral fica utilizável
/// (usado para medir a inicialização sem interação).
/// </summary>
public static class StartupProfiler
{
    private static readonly DateTime ProcessStart = ReadProcessStart();
    private static readonly List<(string Name, double Ms)> Marks = new();
    public static string? OutputPath { get; } = Environment.GetEnvironmentVariable("PQO_STARTUP_PROFILE");
    public static bool Enabled => OutputPath is not null;

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

    /// <summary>Grava as marcas (só no modo de medição). Devolve true quando o app deve fechar.</summary>
    public static bool Finish()
    {
        if (OutputPath is null) return false;
        lock (Marks)
        {
            try { File.WriteAllLines(OutputPath, Marks.Select(m => $"{m.Name}\t{m.Ms:F0}")); }
            catch (IOException) { }
        }
        return true;
    }
}
