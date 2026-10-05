namespace PQueirozOptimizer.BiosAdvisor;

// Fabricantes ainda sem manual conferido: só a interface, a tecla do modo avançado e o suporte oficial.
// Nomes de opções e caminhos documentados entram no banco bios-db.json (optionNames / pathSets), com a fonte.

public sealed class MsiVendor : VendorProfile
{
    public override string Id => "msi";
    public override string DisplayName => "MSI";
    public override string Interface => "MSI Click BIOS";
    public override string[] ManufacturerAliases => new[] { "Micro-Star", "MSI" };
    public override string AdvancedModeKey => "F7";
    public override string? SupportUrl(MotherboardInfo board) => "https://www.msi.com/support";
}

public sealed class GigabyteVendor : VendorProfile
{
    public override string Id => "gigabyte";
    public override string DisplayName => "GIGABYTE";
    public override string Interface => "GIGABYTE UEFI DualBIOS";
    public override string[] ManufacturerAliases => new[] { "Gigabyte" };
    public override string AdvancedModeKey => "F2";
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
