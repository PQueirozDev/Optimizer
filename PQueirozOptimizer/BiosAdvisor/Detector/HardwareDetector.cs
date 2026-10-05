using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PQueirozOptimizer.BiosAdvisor.Detector;

/// <summary>
/// Detecta placa-mãe, BIOS, processador, memória e placa de vídeo pelo WMI/CIM, registro, APIs do Windows e nvidia-smi.
/// Não precisa de privilégios extras além dos que o app já tem e não lê números de série.
/// A conversão das linhas do WMI (Parse*) é separada da leitura para poder ser testada.
/// </summary>
public sealed class HardwareDetector
{
    private readonly IWmiSource _wmi;
    public HardwareDetector(IWmiSource? wmi = null) => _wmi = wmi ?? new SystemWmiSource();

    public HardwareProfile Detect(CancellationToken token = default)
    {
        var board = ParseBoard(_wmi.Query("Win32_BaseBoard", new[] { "Manufacturer", "Product", "Version" }));
        token.ThrowIfCancellationRequested();
        var system = _wmi.Query("Win32_ComputerSystem", new[] { "HypervisorPresent", "Manufacturer", "Model", "PCSystemType" }).FirstOrDefault();
        var tpm = _wmi.Query("Win32_Tpm", new[] { "IsEnabled_InitialValue" }, @"root\cimv2\Security\MicrosoftTpm").FirstOrDefault()?.Bool("IsEnabled_InitialValue");
        var bios = ParseBios(_wmi.Query("Win32_BIOS", new[] { "Manufacturer", "SMBIOSBIOSVersion", "ReleaseDate" }), ReadFirmwareMode(), ReadSecureBoot(), tpm);
        token.ThrowIfCancellationRequested();
        var cpu = ParseCpu(_wmi.Query("Win32_Processor", new[] { "Name", "Manufacturer", "Architecture", "NumberOfCores", "NumberOfEnabledCore", "NumberOfLogicalProcessors", "MaxClockSpeed", "VirtualizationFirmwareEnabled" }),
            system?.Bool("HypervisorPresent") ?? false);
        token.ThrowIfCancellationRequested();
        var memory = ParseMemory(
            _wmi.Query("Win32_PhysicalMemory", new[] { "DeviceLocator", "BankLabel", "Manufacturer", "PartNumber", "Capacity", "Speed", "ConfiguredClockSpeed", "SMBIOSMemoryType", "Attributes" }),
            _wmi.Query("Win32_PhysicalMemoryArray", new[] { "MemoryDevices", "Use" }));
        token.ThrowIfCancellationRequested();
        var links = NvidiaSmi.Available ? NvidiaSmi.ReadLinks() : new Dictionary<string, GpuLinkReading>();
        var gpus = ParseGpus(_wmi.Query("Win32_VideoController", new[] { "Name", "AdapterCompatibility", "DriverVersion", "AdapterRAM", "PNPDeviceID" }), ReadVramByName(), links);
        var chassis = _wmi.Query("Win32_SystemEnclosure", new[] { "ChassisTypes" }).FirstOrDefault()?.Raw("ChassisTypes") as ushort[];
        var laptop = IsLaptop(chassis, system?.Int("PCSystemType"));
        var vm = IsVirtualMachine(system?.Text("Manufacturer") ?? "", system?.Text("Model") ?? "");
        token.ThrowIfCancellationRequested();
        return new HardwareProfile(board, bios, cpu, memory, gpus, laptop, vm) { Observed = Observe(token) };
    }

    // ---------- Conversão (testável) ----------
    public static MotherboardInfo ParseBoard(IReadOnlyList<WmiRow> rows)
    {
        var r = rows.FirstOrDefault();
        return new MotherboardInfo(r?.Text("Manufacturer") ?? "", r?.Text("Product") ?? "", r?.Text("Version") ?? "");
    }

    public static BiosInfo ParseBios(IReadOnlyList<WmiRow> rows, FirmwareMode firmware, bool? secureBoot, bool? tpm)
    {
        var r = rows.FirstOrDefault();
        return new BiosInfo(r?.Text("Manufacturer") ?? "", r?.Text("SMBIOSBIOSVersion") ?? "", ParseCimDate(r?.Text("ReleaseDate")), firmware, secureBoot, tpm);
    }

    /// <summary>Data CIM ("20200722000000.000000+000") ou já convertida pelo PowerShell.</summary>
    public static DateTime? ParseCimDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length >= 8 && DateTime.TryParseExact(text[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var any) ? any.Date : null;
    }

    public static CpuInfo ParseCpu(IReadOnlyList<WmiRow> rows, bool hypervisorPresent)
    {
        var r = rows.FirstOrDefault();
        var name = Regex.Replace(r?.Text("Name") ?? "", @"\s+", " ");
        if (name.Length == 0) name = ReadRegistryCpuName();
        var vendor = (r?.Text("Manufacturer") ?? "") switch
        {
            var m when m.Contains("Intel", StringComparison.OrdinalIgnoreCase) => CpuVendor.Intel,
            var m when m.Contains("AMD", StringComparison.OrdinalIgnoreCase) => CpuVendor.Amd,
            _ => name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? CpuVendor.Intel : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? CpuVendor.Amd : CpuVendor.Unknown,
        };
        var arch = r?.Int("Architecture") switch { 9 => "x64", 12 => "ARM64", 0 => "x86", 5 => "ARM", _ => "" };
        // Vários soquetes: soma núcleos e threads
        var cores = rows.Sum(x => x.Int("NumberOfCores") ?? 0);
        var enabled = rows.Sum(x => x.Int("NumberOfEnabledCore") ?? 0);
        var threads = rows.Sum(x => x.Int("NumberOfLogicalProcessors") ?? 0);
        // Clock base: o do nome comercial ("@ 2.90GHz") é o anunciado; o MaxClockSpeed só como alternativa
        var m2 = Regex.Match(name, @"@\s*([\d.]+)\s*GHz", RegexOptions.IgnoreCase);
        var baseMhz = m2.Success && double.TryParse(m2.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ghz) ? (int)Math.Round(ghz * 1000) : r?.Int("MaxClockSpeed") ?? 0;
        return new CpuInfo(name, vendor, arch, cores, enabled, threads, baseMhz, hypervisorPresent, r?.Bool("VirtualizationFirmwareEnabled"));
    }

    public static MemoryInfo ParseMemory(IReadOnlyList<WmiRow> modules, IReadOnlyList<WmiRow> arrays)
    {
        var list = modules.Select(m =>
        {
            var type = m.Int("SMBIOSMemoryType") switch { 24 => MemoryType.Ddr3, 26 => MemoryType.Ddr4, 34 => MemoryType.Ddr5, _ => MemoryType.Unknown };
            // Rank fica nos 4 bits baixos de Attributes; 0 = desconhecido
            var rank = (m.Int("Attributes") ?? 0) & 0xF;
            return new MemoryModule(m.Text("DeviceLocator"), m.Text("BankLabel"), m.Text("Manufacturer"), m.Text("PartNumber"),
                m.Long("Capacity") ?? 0, m.Int("Speed") ?? 0, m.Int("ConfiguredClockSpeed") ?? 0, rank == 0 ? null : rank, type);
        }).Where(m => m.CapacityBytes > 0).ToList();
        // Use = 3: memória do sistema (ignora caches e memória de vídeo)
        var slots = arrays.Where(a => a.Int("Use") is null or 3).Sum(a => a.Int("MemoryDevices") ?? 0);
        return new MemoryInfo(list, slots > 0 ? slots : null);
    }

    public static List<GpuInfo> ParseGpus(IReadOnlyList<WmiRow> rows, IReadOnlyDictionary<string, long> vramByName, IReadOnlyDictionary<string, GpuLinkReading> links)
    {
        var result = new List<GpuInfo>();
        foreach (var r in rows)
        {
            var name = r.Text("Name");
            var pnp = r.Text("PNPDeviceID");
            // Adaptadores virtuais/remotos não são placas de vídeo
            if (name.Length == 0 || !pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) continue;
            // AdapterRAM é de 32 bits (no máximo 4 GB): o registro guarda o valor real
            long? vram = vramByName.TryGetValue(name, out var v) ? v : r.Long("AdapterRAM") is { } ram && ram > 0 ? ram : null;
            GpuLinkInfo? link = links.TryGetValue(name, out var l) ? new GpuLinkInfo(l.GenMax, l.GenGpuMax, l.WidthMax, l.WidthCurrent, l.Bar1Mb) : null;
            result.Add(new GpuInfo(name, r.Text("AdapterCompatibility"), vram, r.Text("DriverVersion"), pnp, link));
        }
        return result;
    }

    public static bool IsLaptop(ushort[]? chassisTypes, int? pcSystemType) =>
        pcSystemType == 2 || (chassisTypes ?? Array.Empty<ushort>()).Any(t => t is 8 or 9 or 10 or 11 or 14 or 30 or 31 or 32);

    public static bool IsVirtualMachine(string manufacturer, string model) =>
        Regex.IsMatch(model, @"Virtual Machine|VMware|VirtualBox|KVM|HVM domU|QEMU", RegexOptions.IgnoreCase)
        || Regex.IsMatch(manufacturer, @"QEMU|Xen|innotek|VMware|Parallels", RegexOptions.IgnoreCase);

    // ---------- Leituras do Windows ----------
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFirmwareType(out uint firmwareType);

    private static FirmwareMode ReadFirmwareMode()
    {
        try { return GetFirmwareType(out var t) ? t switch { 1 => FirmwareMode.Legacy, 2 => FirmwareMode.Uefi, _ => FirmwareMode.Unknown } : FirmwareMode.Unknown; }
        catch (EntryPointNotFoundException) { return FirmwareMode.Unknown; }
    }

    private static bool? ReadSecureBoot()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return key?.GetValue("UEFISecureBootEnabled") is int v ? v == 1 : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return null; }
    }

    private static string ReadRegistryCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return ""; }
    }

    /// <summary>VRAM real (64 bits) por nome do adaptador, do registro da classe de vídeo.</summary>
    private static Dictionary<string, long> ReadVramByName()
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return map;
            foreach (var sub in cls.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$")))
            {
                using var k = cls.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") is not string desc) continue;
                var size = k.GetValue("HardwareInformation.qwMemorySize") switch { long l => l, byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0), _ => 0L };
                if (size > 0) map[desc.Trim()] = size;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { }
        return map;
    }

    /// <summary>Amostra os contadores de desempenho do processador por ~1,5 s.</summary>
    private static RuntimeObservations? Observe(CancellationToken token)
    {
        using var performance = PdhCounter.TryCreate(PdhCounter.ProcessorPerformance);
        using var limit = PdhCounter.TryCreate(PdhCounter.PerformanceLimit);
        if (performance is null && limit is null) return null;
        double? peak = null, minLimit = null;
        performance?.Read(); limit?.Read();
        for (var i = 0; i < 6 && !token.IsCancellationRequested; i++)
        {
            Thread.Sleep(250);
            if (performance?.Read() is { } p) peak = Math.Max(peak ?? 0, p);
            if (limit?.Read() is { } l) minLimit = Math.Min(minLimit ?? 100, l);
        }
        return new RuntimeObservations(peak, minLimit);
    }
}
