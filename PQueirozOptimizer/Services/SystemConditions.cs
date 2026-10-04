using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Condições de um ajuste (marcadas no script com Requer = "..." ou "# requer: ..."): o ajuste só aparece
/// na revisão quando o PC atende a todas. Assim um ajuste de AMD não aparece num PC com NVIDIA, nem um de
/// Windows 11 no Windows 10. Valores lidos uma vez por execução do app.
/// </summary>
public static class SystemConditions
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus { public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);

    private static readonly Lazy<string> GpuProviders = new(ReadGpuProviders);

    public static int WindowsBuild => Environment.OSVersion.Version.Build;

    /// <summary>Sem bateria do sistema (BatteryFlag 128): desktop.</summary>
    public static bool IsDesktop => !GetSystemPowerStatus(out var status) || status.BatteryFlag == 128;

    /// <summary>Atende a todas as condições, separadas por vírgula (ex.: "win11, desktop"). Condição desconhecida não bloqueia.</summary>
    public static bool Satisfies(string? requirements)
    {
        if (string.IsNullOrWhiteSpace(requirements)) return true;
        foreach (var raw in requirements.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ok = raw.ToLowerInvariant() switch
            {
                "win10" => WindowsBuild < 22000,
                "win11" => WindowsBuild >= 22000,
                "win11-24h2" => WindowsBuild >= 26100,
                "desktop" => IsDesktop,
                "notebook" => !IsDesktop,
                "amd" => System.Text.RegularExpressions.Regex.IsMatch(GpuProviders.Value, @"\b(AMD|ATI|Advanced Micro Devices)\b"),
                "nvidia" => GpuProviders.Value.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase),
                "intel" => GpuProviders.Value.Contains("Intel", StringComparison.OrdinalIgnoreCase),
                _ => true,
            };
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>Fabricantes dos drivers de vídeo instalados (classe Display), juntos num texto.</summary>
    private static string ReadGpuProviders()
    {
        try
        {
            using var display = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (display is null) return "";
            var names = new List<string>();
            foreach (var sub in display.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var key = display.OpenSubKey(sub);
                if (key?.GetValue("ProviderName") is string provider) names.Add(provider);
                if (key?.GetValue("DriverDesc") is string description) names.Add(description);
            }
            return string.Join(" | ", names);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return ""; }
    }
}
