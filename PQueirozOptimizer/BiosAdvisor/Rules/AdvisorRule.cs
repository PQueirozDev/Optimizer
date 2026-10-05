namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>Tudo o que uma regra precisa saber. Montado uma vez por análise.</summary>
public sealed record AdvisorContext(HardwareProfile Profile, PlatformInfo Platform, MemoryAnalysis Memory, BoardProfile? Board, VendorProfile Vendor, AdvisorPreset Preset)
{
    public CpuModel Cpu => Platform.Cpu;
    public bool IsIntel => Cpu.Vendor == CpuVendor.Intel || Profile.Cpu.Vendor == CpuVendor.Intel;
    public bool IsAmd => Cpu.Vendor == CpuVendor.Amd || Profile.Cpu.Vendor == CpuVendor.Amd;
    /// <summary>Desktop físico: ajustes de energia e C-States não valem para notebooks nem máquinas virtuais.</summary>
    public bool IsDesktop => !Profile.IsLaptop && !Profile.IsVirtualMachine;
}

/// <summary>Uma regra avalia o contexto e devolve zero ou mais recomendações. Regras não conhecem a interface.</summary>
public interface IAdvisorRule
{
    IEnumerable<AdvisorRecommendation> Evaluate(AdvisorContext context);
}

/// <summary>Atalhos para escrever as regras sem repetir a mesma estrutura.</summary>
internal static class R
{
    public static LocalizedText T(string pt, string en) => new(pt, en);

    public static readonly LocalizedText Enabled = T("Enabled (Ativado)", "Enabled");
    public static readonly LocalizedText AutoOrEnabled = T("Auto ou Enabled", "Auto or Enabled");

    /// <summary>Como desfazer: voltar o valor e, se o PC não ligar, o caminho de recuperação.</summary>
    public static LocalizedText Undo(string previousPt, string previousEn) => T(
        $"Volte a opção para {previousPt} (ou para o valor que estava antes) e salve com F10. Se o PC não ligar depois de uma mudança, carregue os padrões (Load Optimized Defaults, F5 em placas ASUS) ou limpe a CMOS.",
        $"Set the option back to {previousEn} (or the value it had before) and save with F10. If the PC fails to boot after a change, load the defaults (Load Optimized Defaults, F5 on ASUS boards) or clear the CMOS.");

    public static readonly LocalizedText UndoToAuto = Undo("Auto", "Auto");
}
