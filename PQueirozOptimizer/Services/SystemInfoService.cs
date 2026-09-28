using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer.Services;

public sealed class SystemInfoService
{
    public SystemSnapshot Read()
    {
        // Memória instalada vem dos pentes (16 GB); a "visível" desconta a reserva de hardware (15,9 GB)
        var json = RunPowerShell(
            "$os=Get-CimInstance Win32_OperatingSystem;" +
            "$cpu=(Get-CimInstance Win32_Processor|Select-Object -First 1);" +
            "$gpu=(Get-CimInstance Win32_VideoController|ForEach-Object Name) -join ' / ';" +
            "$d=Get-CimInstance Win32_LogicalDisk|Where-Object DeviceID -eq $env:SystemDrive;" +
            "$ram=(Get-CimInstance Win32_PhysicalMemory|Measure-Object Capacity -Sum).Sum;" +
            "[pscustomobject]@{OS=$os.Caption;Build=$os.BuildNumber;Arch=$os.OSArchitecture;" +
            "CPU=$cpu.Name;GPU=$gpu;Memory=[math]::Round($os.TotalVisibleMemorySize/1MB,1);Installed=[math]::Round($ram/1GB,1);" +
            "Storage=[math]::Round($d.Size/1GB,1);Free=[math]::Round($d.FreeSpace/1GB,1);" +
            "Boot=$os.LastBootUpTime.ToString('o')}|ConvertTo-Json -Compress");

        var info = JsonSerializer.Deserialize<HardwareInfo>(json) ?? new HardwareInfo();
        return new SystemSnapshot(
            info.OS ?? "Windows", info.Build ?? "Não disponível",
            info.Arch ?? (Environment.Is64BitOperatingSystem ? "x64" : "x86"),
            info.CPU?.Trim() ?? "Não disponível", string.IsNullOrWhiteSpace(info.GPU) ? "Não disponível" : info.GPU,
            info.Installed > 0 ? info.Installed : info.Memory, info.Storage, info.Free,
            DateTime.TryParse(info.Boot, out var boot) ? boot : null,
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
        public double Installed { get; set; }
        public double Storage { get; set; }
        public double Free { get; set; }
        public string? Boot { get; set; }
    }
}
