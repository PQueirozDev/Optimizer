using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer.Services;

public sealed class SystemInfoService
{
    public SystemSnapshot Read()
    {
        var json = RunPowerShell(
            "$os=Get-CimInstance Win32_OperatingSystem;" +
            "$cpu=(Get-CimInstance Win32_Processor|Select-Object -First 1);" +
            "$gpu=(Get-CimInstance Win32_VideoController|ForEach-Object Name) -join ' / ';" +
            "$d=Get-CimInstance Win32_LogicalDisk|Where-Object DeviceID -eq $env:SystemDrive;" +
            "[pscustomobject]@{OS=$os.Caption;Build=$os.BuildNumber;Arch=$os.OSArchitecture;" +
            "CPU=$cpu.Name;GPU=$gpu;Memory=[math]::Round($os.TotalVisibleMemorySize/1MB,1);" +
            "Storage=[math]::Round($d.Size/1GB,1);Free=[math]::Round($d.FreeSpace/1GB,1);" +
            "Boot=$os.LastBootUpTime.ToString('o')}|ConvertTo-Json -Compress");

        var info = JsonSerializer.Deserialize<HardwareInfo>(json) ?? new HardwareInfo();
        var uptime = DateTime.TryParse(info.Boot, out var boot)
            ? DateTime.Now - boot : TimeSpan.Zero;
        return new SystemSnapshot(
            info.OS ?? "Windows", info.Build ?? "Não disponível",
            info.Arch ?? (Environment.Is64BitOperatingSystem ? "x64" : "x86"),
            info.CPU ?? "Não disponível", info.GPU ?? "Não disponível",
            $"{info.Memory:N1} GB", $"{info.Storage:N1} GB", $"{info.Free:N1} GB",
            $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m",
            new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator));
    }

    private static string RunPowerShell(string command)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.Encoding]::UTF8;" + command))}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível consultar o hardware.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("A consulta ao sistema excedeu 30 segundos."); }
        if (process.ExitCode != 0) throw new InvalidOperationException(error.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult().Trim();
    }

    private sealed class HardwareInfo
    {
        public string? OS { get; set; }
        public string? Build { get; set; }
        public string? Arch { get; set; }
        public string? CPU { get; set; }
        public string? GPU { get; set; }
        public double Memory { get; set; }
        public double Storage { get; set; }
        public double Free { get; set; }
        public string? Boot { get; set; }
    }
}
