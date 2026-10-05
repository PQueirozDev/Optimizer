namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>
/// ASUS. Os caminhos conferidos (ex.: placas PRIME/PRO/ProArt/TUF GAMING Intel série 400, pelo manual oficial) ficam
/// no banco bios-db.json; aqui só o que não muda: interface, tecla do modo avançado (F7) e página oficial de BIOS.
/// </summary>
public sealed class AsusVendor : VendorProfile
{
    public override string Id => "asus";
    public override string DisplayName => "ASUS";
    public override string Interface => "ASUS UEFI BIOS";
    public override string[] ManufacturerAliases => new[] { "ASUSTeK", "ASUS" };
    public override string AdvancedModeKey => "F7";

    /// <summary>Página de BIOS da placa no site da ASUS (o mesmo endereço do suporte oficial).</summary>
    public override string? SupportUrl(MotherboardInfo board) =>
        string.IsNullOrWhiteSpace(board.Product) ? "https://www.asus.com/support/" : $"https://www.asus.com/supportonly/{Uri.EscapeDataString(board.Product.Trim().ToLowerInvariant())}/helpdesk_bios/";
}
