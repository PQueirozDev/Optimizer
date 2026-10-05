using PQueirozOptimizer.BiosAdvisor.Rules;

namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>
/// Motor de regras: recebe o hardware detectado e devolve recomendações, nota e avisos. Não toca na BIOS,
/// não lê nada do sistema e não conhece a interface — dá para testar com perfis montados à mão.
/// </summary>
public static class BiosAdvisorEngine
{
    /// <summary>Regras na ordem de exibição. Para criar uma regra nova, implemente IAdvisorRule e inclua aqui.</summary>
    public static IReadOnlyList<IAdvisorRule> Rules { get; } = new IAdvisorRule[]
    {
        new MemoryRules(), new IntelCpuRules(), new AmdCpuRules(), new CommonCpuRules(), new IntelPowerRules(), new PcieRules(), new PlatformRules(),
    };

    /// <summary>Categorias que entram na nota (segurança, boot e virtualização são conferências, não desempenho).</summary>
    public static readonly AdvisorCategory[] ScoredCategories =
        { AdvisorCategory.Cpu, AdvisorCategory.Ram, AdvisorCategory.Power, AdvisorCategory.Latency, AdvisorCategory.Pcie, AdvisorCategory.Thermal };

    public static AdvisorReport Analyze(HardwareProfile profile, AdvisorPreset preset, IReadOnlySet<string>? confirmedIds = null)
    {
        var platform = PlatformAnalyzer.Analyze(profile);
        var memory = MemoryAnalyzer.Analyze(profile.Memory, platform);
        var board = BoardProfiles.For(profile.Motherboard);
        var vendor = VendorCatalog.For(profile.Motherboard);
        var context = new AdvisorContext(profile, platform, memory, board, vendor, preset);

        var list = new List<AdvisorRecommendation>();
        foreach (var rule in Rules)
            foreach (var rec in rule.Evaluate(context))
            {
                if (rec.MinPreset > preset || rec.MaxPreset < preset) continue;
                var item = rec;
                // Confirmação do usuário só substitui o que o Windows não consegue ver; nunca uma leitura real
                if (confirmedIds?.Contains(item.Id) == true && item.Evidence == Evidence.NeedsBiosCheck && item.Compliance == Compliance.Unknown)
                    item = item with { Evidence = Evidence.UserConfirmed, Compliance = Compliance.Ok };
                // Numa máquina virtual o Windows vê o hardware virtual: nada vale como evidência da BIOS física
                if (profile.IsVirtualMachine) item = item with { Weight = 0 };
                if (item.SettingId is { } setting && item.Evidence != Evidence.NotApplicable)
                    item = item with { Guide = vendor.Guide(setting, profile.Motherboard, platform, item.RecommendedValue.En) };
                list.Add(item);
            }
        if (list.Select(r => r.Id).Distinct().Count() != list.Count)
            throw new InvalidOperationException("Duas regras geraram o mesmo id de recomendação.");

        var warnings = new List<LocalizedText>();
        if (profile.IsVirtualMachine)
            warnings.Add(new("Este Windows roda numa máquina virtual: a BIOS mostrada é a virtual, não a do computador físico.", "This Windows runs in a virtual machine: the BIOS shown is the virtual one, not the physical PC's."));
        if (profile.IsLaptop)
            warnings.Add(new("Notebook detectado: a fabricante costuma travar a BIOS e ajustes de energia aumentam temperatura e consumo de bateria. Só itens seguros são mostrados.",
                "Laptop detected: manufacturers usually lock the BIOS, and power tweaks raise temperature and battery drain. Only safe items are shown."));
        if (board is null)
            warnings.Add(new("Perfil específico ainda não disponível para esta placa. As recomendações abaixo são genéricas e compatíveis com o hardware detectado.",
                "A specific profile isn't available for this board yet. The recommendations below are generic and compatible with the detected hardware."));
        if (platform.Cpu.Vendor == CpuVendor.Unknown)
            warnings.Add(new("Modelo do processador não reconhecido: as regras específicas de Intel/AMD foram limitadas.", "CPU model not recognized: Intel/AMD-specific rules were limited."));

        string? specificName = board is null ? null : board.HasCpu(platform.Cpu) ? $"{board.Name} + {platform.Cpu.ShortName}" : board.Name;
        return new AdvisorReport(profile, preset, list, Score(list), board != null, specificName, warnings, platform);
    }

    /// <summary>
    /// Nota 0–100 só com o que tem evidência: lido do Windows e confirmado pelo usuário valem 1; deduzido vale meio.
    /// Itens que precisam ser vistos na BIOS ficam fora (a nota não finge saber o que o Windows não mostra).
    /// </summary>
    public static AdvisorScore Score(IReadOnlyList<AdvisorRecommendation> items)
    {
        static double Certainty(Evidence e) => e switch { Evidence.Detected or Evidence.UserConfirmed => 1, Evidence.Inferred => 0.5, _ => 0 };
        var scored = items.Where(r => r.Weight > 0 && ScoredCategories.Contains(r.Category) && r.Evidence != Evidence.NotApplicable).ToList();
        bool Known(AdvisorRecommendation r) => r.Compliance is Compliance.Ok or Compliance.Attention && Certainty(r.Evidence) > 0;
        int? Percent(IEnumerable<AdvisorRecommendation> set)
        {
            double total = 0, ok = 0;
            foreach (var r in set.Where(Known))
            {
                var w = r.Weight * Certainty(r.Evidence);
                total += w;
                if (r.Compliance == Compliance.Ok) ok += w;
            }
            return total <= 0 ? null : (int)Math.Round(ok / total * 100);
        }
        var categories = ScoredCategories.Select(cat =>
        {
            var inCat = scored.Where(r => r.Category == cat).ToList();
            return new CategoryScore(cat, Percent(inCat), inCat.Count(Known), inCat.Count);
        }).ToList();
        return new AdvisorScore(Percent(scored), categories, scored.Count(Known), scored.Count,
            scored.Count(r => Known(r) && r.Evidence == Evidence.Inferred), scored.Count(r => Known(r) && r.Evidence == Evidence.UserConfirmed));
    }
}
