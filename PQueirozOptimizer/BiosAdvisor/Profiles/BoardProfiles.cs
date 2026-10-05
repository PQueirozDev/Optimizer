using System.Text.RegularExpressions;

namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>
/// Perfil específico de uma placa: fatos conferidos no manual oficial (slots, PCIe, memória, nome do arquivo de BIOS)
/// e processadores já mapeados. As regras continuam genéricas; o perfil só acrescenta dados confirmados.
/// </summary>
public sealed record BoardProfile(
    string Name,
    string VendorId,
    Regex Product,
    string Chipset,
    string Source,
    string? SourceUrl,
    string[]? TwoModuleSlots,
    bool TwoModuleSlotsConfirmed,
    string? PrimaryGpuSlot,
    string? PrimaryGpuSlotLink,
    string? BiosFileName,
    IReadOnlyList<string> MappedCpus,
    IReadOnlyList<LocalizedText> Facts)
{
    public bool Matches(MotherboardInfo board) => Product.IsMatch(board.Product ?? "");
    public bool HasCpu(CpuModel cpu) => MappedCpus.Contains(cpu.Number + cpu.Suffix, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Banco de placas. Para adicionar uma placa, inclua um BoardProfile com a fonte oficial.</summary>
public static class BoardProfiles
{
    public static IReadOnlyList<BoardProfile> All { get; } = new[]
    {
        new BoardProfile(
            "ASUS TUF GAMING B460M-PLUS", "asus",
            new Regex(@"^TUF GAMING B460M-PLUS( \(WI-FI\))?$", RegexOptions.IgnoreCase),
            "B460",
            "ASUS TUF GAMING B460M-PLUS User's Manual (E17227) + " + AsusVendor.Intel400Manual,
            "https://dlcdnets.asus.com/pub/ASUS/mb/LGA1200/TUF_GAMING_B460M-PLUS/E17227_TUF_GAMING_B460M-PLUS_UM_V3_WEB.pdf",
            new[] { "A2", "B2" }, false,
            "PCIEX16_1", "PCIe 3.0 x16",
            "TG460MP.CAP",
            new[] { "10700F" },
            new LocalizedText[]
            {
                new("Memória: até 128 GB em 4 slots DDR4, canal duplo. Core i9/i7 da 10ª geração rodam a memória a até 2933 MHz; os demais processadores, a até 2666 MHz.",
                    "Memory: up to 128 GB in 4 DDR4 slots, dual channel. 10th gen Core i9/i7 run memory at up to 2933 MHz; other CPUs at up to 2666 MHz."),
                new("Placa de vídeo: use o slot PCIEX16_1 (PCIe 3.0 x16). O PCIEX16_2 funciona só em x4.",
                    "Graphics card: use the PCIEX16_1 slot (PCIe 3.0 x16). PCIEX16_2 runs at x4 only."),
                new("Atualização pelo ASUS EZ Flash 3 (aba Tool da BIOS); o manual pede o arquivo renomeado como TG460MP.CAP.",
                    "Update with ASUS EZ Flash 3 (BIOS Tool tab); the manual asks for the file renamed to TG460MP.CAP."),
            }),
    };

    public static BoardProfile? For(MotherboardInfo board) => All.FirstOrDefault(p => p.Matches(board));
}
