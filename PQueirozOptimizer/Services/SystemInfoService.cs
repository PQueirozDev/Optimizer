using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Dados do sistema para a Visão geral, lidos direto do Windows (registro e APIs nativas) em poucos
/// milissegundos. Antes vinham de um PowerShell com WMI, que levava cerca de 3 segundos a cada abertura.
/// </summary>
public sealed class SystemInfoService
{
    public SystemSnapshot Read()
    {
        var (os, build) = OperatingSystemName();
        return new SystemSnapshot(
            os, build, Architecture(),
            Safe(GamingService.ProcessorName) is { Length: > 0 } cpu ? cpu : "Não disponível",
            Safe(GraphicsAdapters) is { Length: > 0 } gpu ? gpu : "Não disponível",
            InstalledMemoryGb(), SystemDrive(d => d.TotalSize), SystemDrive(d => d.AvailableFreeSpace),
            // Mesmo contador do "Tempo ativo" do Gerenciador de Tarefas
            DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64),
            new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator));
    }

    private static string? Safe(Func<string> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ExternalException) { return null; }
    }

    /// <summary>"Microsoft Windows 11 Pro" e o build, como o WMI mostrava.</summary>
    internal static (string Name, string Build) OperatingSystemName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = (key?.GetValue("ProductName") as string)?.Trim();
        var build = key?.GetValue("CurrentBuildNumber") as string ?? Environment.OSVersion.Version.Build.ToString();
        if (string.IsNullOrEmpty(product)) return ("Windows", build);
        // O Windows 11 mantém "Windows 10" no ProductName por compatibilidade: o build decide
        if (int.TryParse(build, out var number) && number >= 22000) product = product.Replace("Windows 10", "Windows 11");
        return (product.StartsWith("Microsoft ", StringComparison.Ordinal) ? product : "Microsoft " + product, build);
    }

    private static string Architecture() => RuntimeInformation.OSArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "64 bits",
        System.Runtime.InteropServices.Architecture.Arm64 => "ARM 64 bits",
        _ => "32 bits",
    };

    // ---------- Placa de vídeo: só os adaptadores presentes (o registro guarda também placas já removidas) ----------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    private const int MirroringDriver = 0x8;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);

    internal static string GraphicsAdapters()
    {
        var names = new List<string>();
        for (uint i = 0; i < 64; i++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            var name = device.DeviceString?.Trim();
            if ((device.StateFlags & MirroringDriver) != 0 || string.IsNullOrEmpty(name) || names.Contains(name)) continue;
            names.Add(name);
        }
        return string.Join(" / ", names);
    }

    // ---------- Memória e disco ----------
    [DllImport("kernel32.dll")] private static extern bool GetPhysicallyInstalledSystemMemory(out ulong kilobytes);
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus { public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual; }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    /// <summary>Memória dos pentes (16 GB); sem essa informação, a visível (15,9 GB, sem a reserva de hardware).</summary>
    private static double InstalledMemoryGb()
    {
        if (GetPhysicallyInstalledSystemMemory(out var kb) && kb > 0) return Math.Round(kb / 1024d / 1024d, 1);
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? Math.Round(status.TotalPhys / 1024d / 1024d / 1024d, 1) : 0;
    }

    private static double SystemDrive(Func<DriveInfo, long> value)
    {
        try { return Math.Round(value(new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\")) / 1024d / 1024d / 1024d, 1); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return 0; }
    }
}
