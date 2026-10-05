namespace PQueirozOptimizer.BiosAdvisor;

public enum CpuVendor { Unknown, Intel, Amd }
public enum FirmwareMode { Unknown, Uefi, Legacy }
public enum MemoryType { Unknown, Ddr3, Ddr4, Ddr5 }

/// <summary>Placa-mãe como o SMBIOS informa. O número de série nunca é lido.</summary>
public sealed record MotherboardInfo(string Manufacturer, string Product, string Version);

/// <summary>Firmware: versão, data, modo de boot (GetFirmwareType) e Secure Boot (null = o Windows não informou).</summary>
public sealed record BiosInfo(string Manufacturer, string Version, DateTime? ReleaseDate, FirmwareMode Firmware, bool? SecureBootEnabled, bool? TpmEnabled);

/// <summary>
/// Processador. <see cref="BaseClockMhz"/> vem do nome comercial ("@ 2.90GHz") quando existe; o MaxClockSpeed do WMI
/// é só a alternativa. <see cref="VirtualizationFirmwareEnabled"/> vem como false quando um hipervisor já está rodando.
/// </summary>
public sealed record CpuInfo(string Name, CpuVendor Vendor, string Architecture, int Cores, int EnabledCores, int Threads, int BaseClockMhz,
    bool HypervisorPresent, bool? VirtualizationFirmwareEnabled);

/// <summary>Um pente de memória. RatedMhz é o "Speed" do SMBIOS: a velocidade máxima informada, não prova de perfil XMP.</summary>
public sealed record MemoryModule(string Locator, string Bank, string Manufacturer, string PartNumber, long CapacityBytes, int RatedMhz, int ConfiguredMhz, int? Rank, MemoryType Type);

/// <summary>Memória instalada; TotalSlots vem do Win32_PhysicalMemoryArray (pode faltar ou incluir soldados).</summary>
public sealed record MemoryInfo(IReadOnlyList<MemoryModule> Modules, int? TotalSlots)
{
    public long TotalBytes => Modules.Sum(m => m.CapacityBytes);
    public MemoryType Type => Modules.Select(m => m.Type).FirstOrDefault(t => t != MemoryType.Unknown);
    /// <summary>Menor velocidade configurada informada; 0 quando o Windows não informa (nunca usa a nominal no lugar).</summary>
    public int ConfiguredMhz => Modules.Where(m => m.ConfiguredMhz > 0).Select(m => m.ConfiguredMhz).DefaultIfEmpty(0).Min();
    public static MemoryInfo Empty { get; } = new(Array.Empty<MemoryModule>(), null);
}

/// <summary>Leituras da placa NVIDIA pelo nvidia-smi (null = campo não disponível neste driver).</summary>
public sealed record GpuLinkInfo(int? PcieGenMax, int? PcieGenGpuMax, int? PcieWidthMax, int? PcieWidthCurrent, long? Bar1TotalMb);

public sealed record GpuInfo(string Name, string Vendor, long? VramBytes, string DriverVersion, string PnpId, GpuLinkInfo? Link)
{
    public bool IsNvidia => Vendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || PnpId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase);
    public bool IsAmd => Vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase) || Vendor.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase) || PnpId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase);
    public bool IsIntel => Vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase) || PnpId.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase);
    /// <summary>Placa dedicada (não o vídeo integrado do processador nem adaptadores virtuais).</summary>
    public bool IsDiscrete => IsNvidia || (IsAmd && !Name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) && !Name.Equals("AMD Radeon Graphics", StringComparison.OrdinalIgnoreCase))
        || (IsIntel && Name.Contains("Arc", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Tudo o que o detector conseguiu ler. Campos ausentes ficam nulos/vazios; nada é inventado.</summary>
public sealed record HardwareProfile(MotherboardInfo Motherboard, BiosInfo Bios, CpuInfo Cpu, MemoryInfo Memory, IReadOnlyList<GpuInfo> Gpus, bool IsLaptop, bool IsVirtualMachine)
{
    /// <summary>Leituras feitas durante a detecção (null quando o contador não existe).</summary>
    public RuntimeObservations? Observed { get; init; }

    public GpuInfo? PrimaryGpu => Gpus.FirstOrDefault(g => g.IsDiscrete) ?? Gpus.FirstOrDefault();

    /// <summary>Identifica placa + versão da BIOS: confirmações do usuário deixam de valer quando a BIOS muda.</summary>
    public string Fingerprint => $"{Motherboard.Manufacturer}|{Motherboard.Product}|{Bios.Version}".Trim().ToUpperInvariant();
}

/// <summary>
/// Contadores do Windows lidos durante a detecção: "% Processor Performance" acima de 100 indica que o Turbo
/// funcionou em algum momento; abaixo de 100 não prova que esteja desligado (o PC pode estar ocioso).
/// </summary>
public sealed record RuntimeObservations(double? PeakProcessorPerformancePercent, double? MinPerformanceLimitPercent);
