namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>IDs estáveis das configurações de BIOS. Regras, fabricantes e confirmações do usuário usam estes nomes.</summary>
public static class Settings
{
    public const string IntelTurbo = "intel.turbo", IntelSpeedStep = "intel.speedstep", IntelSpeedShift = "intel.speedshift";
    public const string Smt = "cpu.smt", ActiveCores = "cpu.cores", CStates = "cpu.cstates", ThermalMonitor = "thermal.monitor";
    public const string AmdCpb = "amd.cpb", AmdPbo = "amd.pbo", AmdCppc = "amd.cppc";
    public const string MemoryProfile = "mem.profile", MemoryVoltage = "mem.voltage", MemoryTimings = "mem.timings";
    public const string PowerLimits = "power.limits", PowerEnhancement = "power.enhancement";
    public const string PcieLinkSpeed = "pcie.linkspeed", Above4G = "pcie.above4g", ResizableBar = "pcie.rebar";
    public const string Csm = "boot.csm", SecureBoot = "sec.secureboot", Tpm = "sec.tpm", Virtualization = "virt.vtx", SpreadSpectrum = "latency.spread";
}

/// <summary>Conjunto de caminhos documentados para uma família de BIOS (ex.: ASUS Intel série 400), com a fonte.</summary>
public sealed record BiosPathSet(string Source, Func<MotherboardInfo, PlatformInfo, bool> AppliesTo, IReadOnlyDictionary<string, string[]> Paths, IReadOnlyDictionary<string, LocalizedText>? Notes = null);

/// <summary>
/// Dados de uma fabricante: nome da interface, teclas, nomes das opções e caminhos documentados.
/// Caminho só entra com fonte oficial; sem fonte, a página mostra que a localização não foi confirmada.
/// </summary>
public abstract class VendorProfile
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    /// <summary>Nome da interface ("ASUS UEFI BIOS").</summary>
    public abstract string Interface { get; }
    public abstract string[] ManufacturerAliases { get; }
    /// <summary>Tecla que abre o modo avançado, quando a fabricante tem modo simples e avançado.</summary>
    public virtual string? AdvancedModeKey => null;
    public virtual IReadOnlyList<BiosPathSet> PathSets => Array.Empty<BiosPathSet>();
    /// <summary>Nomes que a fabricante usa para cada configuração (para procurar na BIOS quando não há caminho).</summary>
    public virtual IReadOnlyDictionary<string, string[]> OptionNames => new Dictionary<string, string[]>();

    public bool Matches(MotherboardInfo board) => ManufacturerAliases.Any(a => board.Manufacturer.Contains(a, StringComparison.OrdinalIgnoreCase));

    /// <summary>Página oficial de suporte/BIOS da placa. Null quando não há endereço oficial confiável.</summary>
    public virtual string? SupportUrl(MotherboardInfo board) => null;

    public BiosGuide Guide(string settingId, MotherboardInfo board, PlatformInfo platform, string? target)
    {
        var names = OptionNames.TryGetValue(settingId, out var own) ? own : SettingNames.For(settingId, platform);
        foreach (var set in PathSets)
        {
            if (!set.AppliesTo(board, platform) || !set.Paths.TryGetValue(settingId, out var steps)) continue;
            var note = set.Notes != null && set.Notes.TryGetValue(settingId, out var n) ? n : null;
            var full = AdvancedModeKey is { } key ? new[] { $"Advanced Mode ({key})" }.Concat(steps).ToArray() : steps;
            return new BiosGuide(Interface, full, target, set.Source, names, note);
        }
        return new BiosGuide(Interface, Array.Empty<string>(), target, null, names);
    }
}

/// <summary>Fabricante não reconhecida: só os nomes genéricos das opções, sem caminhos.</summary>
public sealed class GenericVendor : VendorProfile
{
    public override string Id => "generic";
    public override string DisplayName => "BIOS / UEFI";
    public override string Interface => "BIOS / UEFI";
    public override string[] ManufacturerAliases => Array.Empty<string>();
}

/// <summary>Nomes usuais de cada opção, independentes da fabricante.</summary>
public static class SettingNames
{
    public static string[] For(string settingId, PlatformInfo platform)
    {
        var amd = platform.Cpu.Vendor == CpuVendor.Amd;
        return settingId switch
        {
            Settings.IntelTurbo => new[] { "Turbo Mode", "Intel Turbo Boost Technology" },
            Settings.IntelSpeedStep => new[] { "Intel SpeedStep", "EIST" },
            Settings.IntelSpeedShift => new[] { "Intel Speed Shift Technology", "Hardware P-States (HWP)" },
            Settings.Smt => amd ? new[] { "SMT Control", "SMT Mode" } : new[] { "Hyper-Threading", "Intel Hyper-Threading Technology" },
            Settings.ActiveCores => amd ? new[] { "Downcore Control" } : new[] { "Active Processor Cores", "Active Performance Cores" },
            Settings.CStates => amd ? new[] { "Global C-state Control" } : new[] { "CPU C-states", "C-States Control", "Package C State Limit" },
            Settings.ThermalMonitor => new[] { "Thermal Monitor", "Intel Thermal Monitor" },
            Settings.AmdCpb => new[] { "Core Performance Boost" },
            Settings.AmdPbo => new[] { "Precision Boost Overdrive", "PBO" },
            Settings.AmdCppc => new[] { "CPPC", "CPPC Preferred Cores" },
            Settings.MemoryProfile => amd ? new[] { "EXPO", "DOCP", "XMP", "A-XMP" } : new[] { "XMP", "Extreme Memory Profile (X.M.P.)" },
            Settings.MemoryVoltage => new[] { "DRAM Voltage" },
            Settings.MemoryTimings => new[] { "DRAM Timing Control", "Memory Timings" },
            Settings.PowerLimits => new[] { "Long Duration Package Power Limit (PL1)", "Short Duration Package Power Limit (PL2)", "Package Power Time Window (Tau)" },
            Settings.PowerEnhancement => new[] { "MultiCore Enhancement", "Performance Enhancement" },
            Settings.PcieLinkSpeed => new[] { "PCIEx16 Link Speed", "PCIe Slot Configuration" },
            Settings.Above4G => new[] { "Above 4G Decoding" },
            Settings.ResizableBar => new[] { "Re-Size BAR Support", "Resizable BAR" },
            Settings.Csm => new[] { "CSM", "Launch CSM" },
            Settings.SecureBoot => new[] { "Secure Boot" },
            Settings.Tpm => amd ? new[] { "fTPM", "AMD CPU fTPM" } : new[] { "PTT", "Intel Platform Trust Technology" },
            Settings.Virtualization => amd ? new[] { "SVM Mode" } : new[] { "Intel Virtualization Technology (VMX)", "VT-x" },
            Settings.SpreadSpectrum => new[] { "BCLK Spread Spectrum", "Spread Spectrum" },
            _ => Array.Empty<string>(),
        };
    }
}

/// <summary>Registro das fabricantes conhecidas. Para adicionar uma, crie a classe em Vendors/ e inclua aqui.</summary>
public static class VendorCatalog
{
    public static IReadOnlyList<VendorProfile> All { get; } = new VendorProfile[] { new AsusVendor(), new MsiVendor(), new GigabyteVendor(), new AsrockVendor() };

    public static VendorProfile For(MotherboardInfo board) => All.FirstOrDefault(v => v.Matches(board)) ?? new GenericVendor();
}
