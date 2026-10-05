using System.Text.RegularExpressions;

namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>Limites de energia padrão da Intel (PL1/PL2/Tau) para um processador.</summary>
public sealed record IntelPowerSpec(int Pl1Watts, int Pl2Watts, int TauSeconds);

/// <summary>O que a combinação processador + chipset permite. Campos nulos = não sabemos com segurança.</summary>
public sealed record PlatformInfo(
    CpuModel Cpu,
    string? Chipset,
    bool? ChipsetAllowsCpuOverclock,
    bool? ChipsetAllowsMemoryOverclock,
    int? MemoryCapMhz,
    IntelPowerSpec? PowerSpec,
    bool? SupportsResizableBar,
    int? PlatformPcieGen);

/// <summary>
/// Tabelas da plataforma. Só entram combinações verificadas em documentação oficial (Intel ARK / datasheets,
/// manuais das placas); o resto fica nulo e as regras tratam como "não sabemos".
/// </summary>
public static class PlatformAnalyzer
{
    private static readonly string[] IntelChipsets =
    {
        "H310", "B360", "B365", "H370", "Z370", "Z390", "Q370",
        "H410", "B460", "H470", "Z490", "Q470", "W480",
        "H510", "B560", "H570", "Z590", "Q570", "W580",
        "H610", "B660", "H670", "Z690", "Q670", "W680",
        "B760", "H770", "Z790",
        "H810", "B860", "Z890",
    };

    private static readonly string[] AmdChipsets =
    {
        "A320", "B350", "X370", "B450", "X470", "A520", "B550", "X570", "A620", "B650E", "B650", "X670E", "X670", "B840", "B850", "X870E", "X870",
    };

    // Intel não-Z que liberam overclock de memória (11ª geração em diante)
    private static readonly HashSet<string> IntelMemoryOcNonZ = new(StringComparer.Ordinal) { "B560", "H570", "B660", "H670", "B760", "H770", "B860" };
    // Intel série 300/400 sem Z: a memória fica presa na velocidade oficial do processador
    private static readonly HashSet<string> IntelMemoryLocked = new(StringComparer.Ordinal) { "H310", "B360", "B365", "H370", "Q370", "H410", "B460", "H470", "Q470" };

    // Datasheet da 10ª geração (Comet Lake-S): PL1 = TDP, PL2 e Tau por modelo
    private static readonly Dictionary<string, IntelPowerSpec> IntelPower = new(StringComparer.OrdinalIgnoreCase)
    {
        ["10900K"] = new(125, 250, 56), ["10900KF"] = new(125, 250, 56), ["10900"] = new(65, 224, 28), ["10900F"] = new(65, 224, 28),
        ["10700K"] = new(125, 229, 56), ["10700KF"] = new(125, 229, 56), ["10700"] = new(65, 224, 28), ["10700F"] = new(65, 224, 28),
        ["10600K"] = new(125, 182, 56), ["10600KF"] = new(125, 182, 56), ["10600"] = new(65, 134, 28),
        ["10500"] = new(65, 134, 28), ["10400"] = new(65, 134, 28), ["10400F"] = new(65, 134, 28),
        ["10100"] = new(65, 90, 28), ["10100F"] = new(65, 90, 28),
    };

    /// <summary>Chipset pelo nome da placa ("TUF GAMING B460M-PLUS" → B460), conferindo com a marca do processador.</summary>
    public static string? ParseChipset(string product, CpuVendor vendor)
    {
        var list = vendor == CpuVendor.Amd ? AmdChipsets : vendor == CpuVendor.Intel ? IntelChipsets : IntelChipsets.Concat(AmdChipsets).ToArray();
        foreach (var chipset in list)
            if (Regex.IsMatch(product ?? "", $@"(?<![A-Z0-9]){chipset}(?![0-9])", RegexOptions.IgnoreCase)) return chipset;
        return null;
    }

    /// <summary>Velocidade oficial de memória do processador (Intel ARK), usada só onde o chipset trava a memória.</summary>
    public static int? IntelOfficialMemoryMhz(CpuModel cpu) => cpu is { Vendor: CpuVendor.Intel, Brand: "Core" } ? cpu.Generation switch
    {
        10 => cpu.Tier >= 7 ? 2933 : 2666,
        8 or 9 => cpu.Tier >= 5 ? 2666 : 2400,
        _ => null,
    } : null;

    public static PlatformInfo Analyze(HardwareProfile profile)
    {
        var cpu = CpuModel.Parse(profile.Cpu.Name);
        var vendor = cpu.Vendor != CpuVendor.Unknown ? cpu.Vendor : profile.Cpu.Vendor;
        var chipset = ParseChipset(profile.Motherboard.Product, vendor);
        bool? cpuOc = null, memOc = null;
        int? memCap = null, pcieGen = null;
        bool? rebar = null;
        if (chipset != null && vendor == CpuVendor.Intel)
        {
            cpuOc = chipset.StartsWith('Z') || chipset.StartsWith('W');
            memOc = chipset.StartsWith('Z') || IntelMemoryOcNonZ.Contains(chipset) ? true : IntelMemoryLocked.Contains(chipset) ? false : null;
            if (memOc == false) memCap = IntelOfficialMemoryMhz(cpu);
        }
        else if (chipset != null && vendor == CpuVendor.Amd)
        {
            // Série A: sem overclock de processador; memória liberada em todas as séries AM4/AM5
            cpuOc = !chipset.StartsWith('A');
            memOc = true;
        }
        if (vendor == CpuVendor.Intel && cpu.Brand == "Core")
        {
            // PCIe do processador: 3.0 até a 10ª geração, 4.0 na 11ª, 5.0 (slot x16) da 12ª em diante
            pcieGen = cpu.Generation switch { <= 10 and >= 6 => 3, 11 => 4, >= 12 => 5, _ => null };
            // Resizable BAR: Intel a partir da 10ª geração (com BIOS atualizada)
            rebar = cpu.Generation >= 10 ? true : cpu.Generation is not null ? false : null;
        }
        else if (vendor == CpuVendor.Amd && cpu.Brand == "Ryzen")
        {
            rebar = cpu.Generation >= 3 ? true : cpu.Generation is not null ? false : null;
            pcieGen = cpu.Generation switch { >= 7 => 5, >= 3 => chipset is "A520" or "B450" or "X470" or "A320" or "B350" or "X370" ? 3 : 4, >= 1 => 3, _ => null };
        }
        IntelPowerSpec? power = vendor == CpuVendor.Intel && IntelPower.TryGetValue(cpu.Number + cpu.Suffix, out var spec) ? spec : null;
        return new PlatformInfo(cpu, chipset, cpuOc, memOc, memCap, power, rebar, pcieGen);
    }
}
