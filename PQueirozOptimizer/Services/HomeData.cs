using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer.Services;

public sealed record CpuInfo(int Cores, int Threads, double? BaseMhz);
/// <summary>Memória instalada: velocidade configurada (MT/s), tipo (DDR4, DDR5...) e quantidade de módulos.</summary>
public sealed record MemoryInfo(int? SpeedMts, string? Type, int Modules);
public sealed record DriveStat(string Name, string Label, double TotalGb, double FreeGb, string? MediaType, bool IsSystem)
{
    public double UsedPercent => TotalGb <= 0 ? 0 : Math.Clamp((TotalGb - FreeGb) / TotalGb * 100, 0, 100);
    public double FreePercent => 100 - UsedPercent;
}
public sealed record NetworkInfo(string? Name, string Kind, bool Connected);
public sealed record BoardInfo(string? Manufacturer, string? Product, string? BiosVendor, string? BiosVersion, DateTime? BiosDate);
public sealed record GpuAdapter(string Name, string? DriverVersion, DateTime? DriverDate);
public sealed record OsInfo(string? DisplayVersion, DateTime? InstalledAt);
/// <summary>Indicador do card Meu PC. Tone: Success, Warning, Danger ou Muted (sem dados suficientes).</summary>
public sealed record HealthIndicator(string Key, string Title, string Tone, string Status, string Detail);

/// <summary>
/// Leituras do Início e do Meu PC que não vêm do monitor de 1 segundo: núcleos, unidades, rede ativa e os
/// indicadores de saúde. Tudo só lê o sistema; o que não pode ser lido fica "sem dados" (nunca um valor estimado).
/// As leituras mais caras (WMI, registro de eventos) ficam guardadas por 10 minutos.
/// </summary>
public static class HomeData
{
    private static CpuInfo? _cpu;
    private static BoardInfo? _board;
    private static IReadOnlyList<GpuAdapter>? _gpus;
    private static OsInfo? _os;
    private static MemoryInfo? _memory;
    private static Dictionary<char, string>? _mediaByLetter;
    private static (DateTime At, Task<IReadOnlyList<HealthIndicator>> Task)? _health;

    private static IEnumerable<ManagementObject> Query(string scope, string wql)
    {
        try { using var searcher = new ManagementObjectSearcher(scope, wql); return searcher.Get().Cast<ManagementObject>().ToList(); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return Array.Empty<ManagementObject>(); }
    }

    /// <summary>Núcleos físicos, threads e frequência base (WMI uma vez por execução).</summary>
    public static CpuInfo Cpu()
    {
        if (_cpu is { } cached) return cached;
        int cores = 0, threads = 0; double? mhz = null;
        foreach (var o in Query(@"root\CIMV2", "SELECT NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
        {
            cores += Convert.ToInt32(o["NumberOfCores"] ?? 0);
            threads += Convert.ToInt32(o["NumberOfLogicalProcessors"] ?? 0);
            if (o["MaxClockSpeed"] is { } speed && Convert.ToDouble(speed) > 0) mhz ??= Convert.ToDouble(speed);
        }
        return _cpu = new CpuInfo(cores, threads > 0 ? threads : Environment.ProcessorCount, mhz);
    }

    private static DateTime? WmiDate(object? value)
    {
        var raw = value?.ToString();
        return raw is { Length: >= 8 } && DateTime.TryParseExact(raw[..8], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
    }

    private static string? Clean(object? value) => value?.ToString()?.Trim() is { Length: > 0 } s && !s.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) && !s.Equals("Default string", StringComparison.OrdinalIgnoreCase) ? s : null;

    /// <summary>Placa-mãe e BIOS (WMI uma vez por execução).</summary>
    public static BoardInfo Board()
    {
        if (_board is { } cached) return cached;
        var board = Query(@"root\CIMV2", "SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        var bios = Query(@"root\CIMV2", "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
        return _board = new BoardInfo(Clean(board?["Manufacturer"]), Clean(board?["Product"]), Clean(bios?["Manufacturer"]), Clean(bios?["SMBIOSBIOSVersion"]), WmiDate(bios?["ReleaseDate"]));
    }

    /// <summary>Placas de vídeo com a versão e a data do driver (sem os adaptadores virtuais e o básico da Microsoft).</summary>
    public static IReadOnlyList<GpuAdapter> Gpus()
    {
        if (_gpus is { } cached) return cached;
        return _gpus = Query(@"root\CIMV2", "SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController")
            .Select(o => new GpuAdapter(o["Name"]?.ToString() ?? "", Clean(o["DriverVersion"]), WmiDate(o["DriverDate"])))
            .Where(g => g.Name.Length > 0 && !g.Name.Contains("Basic", StringComparison.OrdinalIgnoreCase) && !g.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Versão de exibição do Windows (ex.: 24H2) e a data de instalação.</summary>
    public static OsInfo Os()
    {
        if (_os is { } cached) return cached;
        string? display = null;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            display = key?.GetValue("DisplayVersion") as string;
        }
        catch (System.Security.SecurityException) { }
        var installed = Query(@"root\CIMV2", "SELECT InstallDate FROM Win32_OperatingSystem").Select(o => WmiDate(o["InstallDate"])).FirstOrDefault();
        return _os = new OsInfo(display, installed);
    }

    /// <summary>Velocidade e tipo da memória (WMI uma vez por execução).</summary>
    public static MemoryInfo Memory()
    {
        if (_memory is { } cached) return cached;
        var modules = Query(@"root\CIMV2", "SELECT ConfiguredClockSpeed, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory").ToList();
        int? speed = modules.Select(m => Convert.ToInt32(m["ConfiguredClockSpeed"] ?? 0)).Where(s => s > 0).DefaultIfEmpty(0).Max() is var c and > 0 ? c
            : modules.Select(m => Convert.ToInt32(m["Speed"] ?? 0)).Where(s => s > 0).DefaultIfEmpty(0).Max() is var s2 and > 0 ? s2 : null;
        var type = modules.Select(m => Convert.ToInt32(m["SMBIOSMemoryType"] ?? 0) switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => null })
            .FirstOrDefault(t => t != null);
        return _memory = new MemoryInfo(speed, type, modules.Count);
    }

    /// <summary>Unidades fixas prontas, com o tipo do disco (SSD/HDD) quando o Windows informa.</summary>
    public static IReadOnlyList<DriveStat> Drives()
    {
        var media = MediaByLetter();
        var system = char.ToUpperInvariant((Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\")[0]);
        var list = new List<DriveStat>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady || d.TotalSize <= 0) continue;
                var letter = char.ToUpperInvariant(d.Name[0]);
                list.Add(new DriveStat(d.Name.TrimEnd('\\'), d.VolumeLabel, d.TotalSize / 1073741824.0, d.AvailableFreeSpace / 1073741824.0, media.GetValueOrDefault(letter), letter == system));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list.OrderByDescending(d => d.IsSystem).ThenBy(d => d.Name).ToList();
    }

    private static Dictionary<char, string> MediaByLetter()
    {
        if (_mediaByLetter is { } cached) return cached;
        var byDisk = Query(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk")
            .ToDictionary(o => o["DeviceId"]?.ToString() ?? "", o => Convert.ToInt32(o["MediaType"] ?? 0) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "" });
        var result = new Dictionary<char, string>();
        foreach (var p in Query(@"root\Microsoft\Windows\Storage", "SELECT DriveLetter, DiskNumber FROM MSFT_Partition"))
        {
            if (p["DriveLetter"] is not char and not ushort and not int || Convert.ToInt32(p["DriveLetter"]) == 0) continue;
            var letter = char.ToUpperInvariant(Convert.ToChar(Convert.ToInt32(p["DriveLetter"])));
            if (byDisk.TryGetValue(p["DiskNumber"]?.ToString() ?? "", out var type) && type.Length > 0) result[letter] = type;
        }
        return _mediaByLetter = result;
    }

    /// <summary>Adaptador com rota para a internet (gateway): nome, tipo e se está conectado.</summary>
    public static NetworkInfo Network()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                if (!nic.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any))) continue;
                var kind = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT ? "Ethernet" : "";
                return new NetworkInfo(nic.Description, kind, true);
            }
        }
        catch (NetworkInformationException) { }
        return new NetworkInfo(null, "", false);
    }

    /// <summary>Descarta as leituras guardadas (botão Atualizar).</summary>
    public static void Invalidate() { _health = null; _mediaByLetter = null; }

    /// <summary>Temperaturas, saúde, estabilidade e drivers, do mais leve ao mais caro. Guardado por 10 minutos.</summary>
    public static Task<IReadOnlyList<HealthIndicator>> HealthAsync(SystemSnapshot snapshot, int healthScore, string healthTone)
    {
        if (_health is { } h && DateTime.Now - h.At < TimeSpan.FromMinutes(10) && !h.Task.IsFaulted) return h.Task;
        var task = Task.Run<IReadOnlyList<HealthIndicator>>(() => new[]
        {
            Temperatures(), Health(healthScore, healthTone), Stability(), Drivers(),
        });
        _health = (DateTime.Now, task);
        return task;
    }

    private static HealthIndicator Temperatures()
    {
        var gpu = HardwareMonitorService.Shared.Latest?.GpuTempC ?? NvmlReader.TemperatureC();
        const string cpuNote = "A temperatura da CPU exige um driver de kernel que o Qrztweaks não instala, por segurança.";
        if (gpu is not { } t) return new("temperatures", "Temperaturas", "Muted", "Sem sensor disponível", "Nenhum sensor de temperatura pôde ser lido sem driver extra. " + cpuNote);
        var (tone, status) = t switch { < 80 => ("Success", "Normal"), < 88 => ("Warning", "Quente"), _ => ("Danger", "Muito quente") };
        return new("temperatures", "Temperaturas", tone, $"{status} · GPU {t:0} °C", $"Placa de vídeo a {t:0} °C (sensor do driver NVIDIA). " + cpuNote);
    }

    private static HealthIndicator Health(int score, string tone) =>
        new("health", "Saúde", tone, string.Format(Translator.Tr("{0} de 100"), score), "Pontuação calculada pelo espaço livre no disco do sistema, pelo tempo desde o último reinício e pela permissão de administrador.");

    /// <summary>Desligamentos inesperados e telas azuis nos últimos 7 dias (registro de eventos do Sistema).</summary>
    private static HealthIndicator Stability()
    {
        const string window = "TimeCreated[timediff(@SystemTime) <= 604800000]";
        int Count(string xpath)
        {
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath));
            var n = 0;
            for (var e = reader.ReadEvent(); e != null; e = reader.ReadEvent()) { using (e) n++; }
            return n;
        }
        try
        {
            var shutdowns = Count($"*[System[(EventID=41 or EventID=6008) and {window}]]");
            var crashes = Count($"*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting' or @Name='BugCheck'] and EventID=1001 and {window}]]");
            var total = shutdowns + crashes;
            var detail = $"Últimos 7 dias no registro de eventos do Windows: {shutdowns} desligamento(s) inesperado(s) e {crashes} tela(s) azul(is).";
            return total switch
            {
                0 => new("stability", "Estabilidade", "Success", "Nenhuma falha em 7 dias", detail),
                <= 2 => new("stability", "Estabilidade", "Warning", total == 1 ? "1 falha em 7 dias" : $"{total} falhas em 7 dias", detail),
                _ => new("stability", "Estabilidade", "Danger", $"{total} falhas em 7 dias", detail),
            };
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new("stability", "Estabilidade", "Muted", "Sem dados", "O registro de eventos do Windows não pôde ser lido agora.");
        }
    }

    private static HealthIndicator Drivers()
    {
        var problems = Query(@"root\CIMV2", "SELECT Name, ConfigManagerErrorCode, Present FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0")
            .Where(o => o["Present"] is not false && Convert.ToInt32(o["ConfigManagerErrorCode"] ?? 0) is not (0 or 22 or 45))
            .Select(o => o["Name"]?.ToString() ?? "Dispositivo sem nome").Distinct().ToList();
        DateTime? driverDate = null;
        foreach (var o in Query(@"root\CIMV2", "SELECT Name, DriverDate FROM Win32_VideoController"))
        {
            var name = o["Name"]?.ToString();
            if (name is null || name.Contains("Basic", StringComparison.OrdinalIgnoreCase) || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
            var raw = o["DriverDate"]?.ToString();
            if (raw is { Length: >= 8 } && DateTime.TryParseExact(raw[..8], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d)) driverDate = d;
            break;
        }
        var old = driverDate is { } date && (DateTime.Now - date).TotalDays > 365;
        if (problems.Count >= 3) return new("drivers", "Drivers", "Danger", $"{problems.Count} dispositivos com erro", "No Gerenciador de Dispositivos: " + string.Join(", ", problems.Take(5)));
        if (problems.Count > 0) return new("drivers", "Drivers", "Warning", problems.Count == 1 ? "1 dispositivo com erro" : $"{problems.Count} dispositivos com erro", "No Gerenciador de Dispositivos: " + string.Join(", ", problems));
        if (old) return new("drivers", "Drivers", "Warning", "Driver de vídeo com mais de 1 ano", $"O driver da placa de vídeo é de {driverDate:dd/MM/yyyy}. Versões novas costumam corrigir desempenho em jogos recentes.");
        return new("drivers", "Drivers", "Success", "Nenhum dispositivo com erro", driverDate is { } dd ? $"Driver de vídeo de {dd:dd/MM/yyyy}." : "Nenhum erro no Gerenciador de Dispositivos.");
    }
}
