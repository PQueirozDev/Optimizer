namespace PQueirozOptimizer.Models;

/// <summary>
/// Leitura do sistema com valores numéricos: os textos são montados só para exibição,
/// assim nenhum cálculo depende de reinterpretar números formatados (ex.: "1.863,0 GB").
/// </summary>
public sealed record SystemSnapshot(
    string OperatingSystem,
    string Build,
    string Architecture,
    string Processor,
    string Graphics,
    double MemoryGb,
    double StorageGb,
    double FreeGb,
    DateTime? BootTime,
    bool IsAdministrator)
{
    public string Memory => $"{MemoryGb:0.#} GB";
    public string Storage => FormatSize(StorageGb);
    public string FreeSpace => FormatSize(FreeGb);
    public double FreePercent => StorageGb <= 0 ? 0 : Math.Clamp(FreeGb / StorageGb * 100, 0, 100);
    public TimeSpan Uptime => BootTime is { } boot && boot <= DateTime.Now ? DateTime.Now - boot : TimeSpan.Zero;

    public string UptimeText => Uptime switch
    {
        { Days: > 0 } u => $"{u.Days}d {u.Hours}h",
        { Hours: > 0 } u => $"{u.Hours}h {u.Minutes}m",
        var u => $"{u.Minutes}m",
    };

    private static string FormatSize(double gb) => gb >= 1000 ? $"{gb / 1024:0.##} TB" : $"{gb:0.#} GB";
}
