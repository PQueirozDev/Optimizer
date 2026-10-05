using System.Text.RegularExpressions;

namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>
/// ASUS. Caminhos das placas PRIME / PRO / ProArt / TUF GAMING com chipset Intel série 400, conferidos no manual oficial
/// "PRIME/PRO/ProArt/TUF GAMING Intel 400 Series BIOS Manual". ROG/STRIX e outras séries usam outros menus
/// (ex.: Extreme Tweaker) e ficam sem caminho até haver fonte.
/// </summary>
public sealed class AsusVendor : VendorProfile
{
    public override string Id => "asus";
    public override string DisplayName => "ASUS";
    public override string Interface => "ASUS UEFI BIOS";
    public override string[] ManufacturerAliases => new[] { "ASUSTeK", "ASUS" };
    public override string AdvancedModeKey => "F7";

    public const string Intel400Manual = "ASUS PRIME/PRO/ProArt/TUF GAMING Intel 400 Series BIOS Manual";
    public const string Intel400ManualUrl = "https://dlcdnets.asus.com/pub/ASUS/mb/13MANUAL/PRIME_PRO_PROART_TUF_GAMING_Intel_400_Series_BIOS_EM_WEB_EN.pdf";

    private static readonly HashSet<string> Intel400 = new(StringComparer.Ordinal) { "H410", "B460", "H470", "Z490", "Q470", "W480" };

    private static bool IsIntel400Mainstream(MotherboardInfo board, PlatformInfo platform) =>
        platform.Chipset is { } c && Intel400.Contains(c) && Regex.IsMatch(board.Product, @"^(PRIME|PRO|ProArt|TUF)\b", RegexOptions.IgnoreCase);

    private const string CpuPower = "CPU Power Management Configuration";

    public override IReadOnlyList<BiosPathSet> PathSets { get; } = new[]
    {
        new BiosPathSet(Intel400Manual, IsIntel400Mainstream, new Dictionary<string, string[]>
        {
            [Settings.MemoryProfile] = new[] { "Ai Tweaker", "Ai Overclock Tuner" },
            [Settings.MemoryVoltage] = new[] { "Ai Tweaker", "DRAM Voltage" },
            [Settings.PowerEnhancement] = new[] { "Ai Tweaker", "ASUS MultiCore Enhancement" },
            [Settings.PowerLimits] = new[] { "Ai Tweaker", "Internal CPU Power Management", "Long Duration Package Power Limit / Package Power Time Window / Short Duration Package Power Limit" },
            [Settings.SpreadSpectrum] = new[] { "Ai Tweaker", "BCLK Spread Spectrum" },
            [Settings.Smt] = new[] { "Advanced", "CPU Configuration", "Hyper-Threading" },
            [Settings.ActiveCores] = new[] { "Advanced", "CPU Configuration", "Active Processor Cores" },
            [Settings.Virtualization] = new[] { "Advanced", "CPU Configuration", "Intel (VMX) Virtualization Technology" },
            [Settings.IntelSpeedStep] = new[] { "Advanced", "CPU Configuration", CpuPower, "Intel(R) SpeedStep(tm)" },
            [Settings.IntelSpeedShift] = new[] { "Advanced", "CPU Configuration", CpuPower, "Intel(R) Speed Shift Technology" },
            [Settings.IntelTurbo] = new[] { "Advanced", "CPU Configuration", CpuPower, "Turbo Mode" },
            [Settings.CStates] = new[] { "Advanced", "CPU Configuration", CpuPower, "CPU C-states" },
            [Settings.ThermalMonitor] = new[] { "Advanced", "CPU Configuration", CpuPower, "Thermal Monitor" },
            [Settings.Above4G] = new[] { "Advanced", "System Agent (SA) Configuration", "Above 4G Decoding" },
            [Settings.PcieLinkSpeed] = new[] { "Advanced", "System Agent (SA) Configuration", "PEG Port Configuration", "PCIEx16_1 Link Speed" },
            [Settings.Tpm] = new[] { "Advanced", "PCH-FW Configuration", "PTT" },
            [Settings.Csm] = new[] { "Boot", "CSM (Compatibility Support Module)", "Launch CSM" },
            [Settings.SecureBoot] = new[] { "Boot", "Secure Boot", "OS Type" },
        },
        new Dictionary<string, LocalizedText>
        {
            [Settings.MemoryProfile] = new("[XMP I] usa o perfil do pente com ajustes da ASUS; [XMP II] carrega o perfil XMP original do pente. A opção só aparece com pentes que têm XMP.",
                "[XMP I] uses the module profile with ASUS adjustments; [XMP II] loads the module's original XMP profile. The option only appears with XMP modules."),
            [Settings.SpreadSpectrum] = new("Esta opção só aparece com o Ai Overclock Tuner em [XMP I], [XMP II] ou [Manual].",
                "This option only appears when Ai Overclock Tuner is set to [XMP I], [XMP II] or [Manual]."),
            [Settings.PowerEnhancement] = new("Opções: [Auto - Lets BIOS Optimize], [Disabled - Enforce All limits] (limites Intel) e [Enabled - Remove All limits].",
                "Options: [Auto - Lets BIOS Optimize], [Disabled - Enforce All limits] (Intel limits) and [Enabled - Remove All limits]."),
            [Settings.ThermalMonitor] = new("No manual o item aparece no fim de CPU Power Management Configuration, logo após os C-states.",
                "In the manual the item appears at the end of CPU Power Management Configuration, right after the C-states."),
        }),
    };

    public override IReadOnlyDictionary<string, string[]> OptionNames { get; } = new Dictionary<string, string[]>
    {
        [Settings.PowerEnhancement] = new[] { "ASUS MultiCore Enhancement", "ASUS Performance Enhancement" },
    };

    /// <summary>Página de BIOS da placa no site da ASUS (o mesmo endereço do suporte oficial).</summary>
    public override string? SupportUrl(MotherboardInfo board) =>
        string.IsNullOrWhiteSpace(board.Product) ? "https://www.asus.com/support/" : $"https://www.asus.com/supportonly/{Uri.EscapeDataString(board.Product.Trim().ToLowerInvariant())}/helpdesk_bios/";
}
