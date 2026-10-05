namespace PQueirozOptimizer.BiosAdvisor;

// Fabricantes ainda sem manual conferido: só a interface, a tecla do modo avançado, nomes de opções e o suporte oficial.
// Para documentar caminhos, adicione um BiosPathSet com a fonte, como em AsusVendor.

public sealed class MsiVendor : VendorProfile
{
    public override string Id => "msi";
    public override string DisplayName => "MSI";
    public override string Interface => "MSI Click BIOS";
    public override string[] ManufacturerAliases => new[] { "Micro-Star", "MSI" };
    public override string AdvancedModeKey => "F7";
    public override IReadOnlyDictionary<string, string[]> OptionNames { get; } = new Dictionary<string, string[]>
    {
        [Settings.MemoryProfile] = new[] { "Extreme Memory Profile (XMP)", "A-XMP", "EXPO" },
        [Settings.PowerEnhancement] = new[] { "Enhanced Turbo" },
    };
    public override string? SupportUrl(MotherboardInfo board) => "https://www.msi.com/support";
}

public sealed class GigabyteVendor : VendorProfile
{
    public override string Id => "gigabyte";
    public override string DisplayName => "GIGABYTE";
    public override string Interface => "GIGABYTE UEFI DualBIOS";
    public override string[] ManufacturerAliases => new[] { "Gigabyte" };
    public override string AdvancedModeKey => "F2";
    public override IReadOnlyDictionary<string, string[]> OptionNames { get; } = new Dictionary<string, string[]>
    {
        [Settings.MemoryProfile] = new[] { "Extreme Memory Profile (X.M.P.)", "EXPO" },
        [Settings.PowerEnhancement] = new[] { "Enhanced Multi-Core Performance" },
    };
    public override string? SupportUrl(MotherboardInfo board) => "https://www.gigabyte.com/Support";
}

public sealed class AsrockVendor : VendorProfile
{
    public override string Id => "asrock";
    public override string DisplayName => "ASRock";
    public override string Interface => "ASRock UEFI Setup Utility";
    public override string[] ManufacturerAliases => new[] { "ASRock" };
    public override string AdvancedModeKey => "F6";
    public override string? SupportUrl(MotherboardInfo board) => "https://www.asrock.com/support/index.asp";
}
