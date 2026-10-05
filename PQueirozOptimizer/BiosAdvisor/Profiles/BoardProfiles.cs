using System.Text.RegularExpressions;

namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>
/// Perfil específico de uma placa: fatos conferidos no manual oficial (slots, PCIe, memória, nome do arquivo de BIOS)
/// e processadores já mapeados. Vem do banco bios-db.json; as regras continuam genéricas.
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
    public bool HasCpu(CpuModel cpu) => MappedCpus.Contains(cpu.Number + cpu.Suffix, StringComparer.OrdinalIgnoreCase);
}
