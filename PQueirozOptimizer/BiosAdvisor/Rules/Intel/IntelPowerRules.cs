using static PQueirozOptimizer.BiosAdvisor.R;

namespace PQueirozOptimizer.BiosAdvisor.Rules;

/// <summary>
/// Limites de energia (PL1/PL2/Tau) e o "aprimoramento" da fabricante (ASUS MultiCore Enhancement e equivalentes).
/// Só desktop. Nada de remover todos os limites: a capacidade do VRM e do cooler não é conhecida pelo Windows.
/// </summary>
public sealed class IntelPowerRules : IAdvisorRule
{
    public IEnumerable<AdvisorRecommendation> Evaluate(AdvisorContext c)
    {
        if (!c.IsIntel || !c.IsDesktop) yield break;
        var spec = c.Platform.PowerSpec;
        var specText = spec is null ? null : $"PL1 {spec.Pl1Watts} W · PL2 {spec.Pl2Watts} W · Tau {spec.TauSeconds} s";

        // Seguro: padrão da placa/Intel
        yield return new AdvisorRecommendation
        {
            Id = "intel-power-default", SettingId = Settings.PowerLimits, Category = AdvisorCategory.Power, MaxPreset = AdvisorPreset.Safe,
            Name = T("Limites de energia (PL1 / PL2 / Tau)", "Power limits (PL1 / PL2 / Tau)"),
            Description = spec is null
                ? T("PL1 é a potência sustentada, PL2 o pico de curta duração e Tau quanto tempo o pico dura. Em Auto a placa usa o padrão da Intel ou o seu próprio.",
                    "PL1 is sustained power, PL2 the short-term peak and Tau how long the peak lasts. On Auto the board uses Intel's default or its own.")
                : T($"PL1 é a potência sustentada, PL2 o pico de curta duração e Tau quanto tempo o pico dura. Padrão Intel para o {c.Cpu.ShortName}: {specText}.",
                    $"PL1 is sustained power, PL2 the short-term peak and Tau how long the peak lasts. Intel default for the {c.Cpu.ShortName}: {specText}."),
            RecommendedValue = spec is null ? T("Auto (padrão)", "Auto (default)") : T($"Auto ({specText})", $"Auto ({specText})"),
            Evidence = Evidence.NeedsBiosCheck, Compliance = Compliance.Unknown,
            ExpectedBenefit = T("Comportamento previsível do Turbo, dentro da especificação.", "Predictable Turbo behavior, within spec."),
            Gain = Level.Low, Risk = Level.Low, Thermal = ThermalImpact.Low,
            Rollback = UndoToAuto,
        };

        // Desempenho: PL1 maior, PL2/Tau da Intel; exige monitorar temperatura
        var raisePl1 = spec is { Pl1Watts: 65 } ? 125 : (int?)null;
        yield return new AdvisorRecommendation
        {
            Id = "intel-power-raised", SettingId = Settings.PowerLimits, Category = AdvisorCategory.Power, MinPreset = AdvisorPreset.Performance,
            Name = T("Limite sustentado maior (PL1)", "Higher sustained limit (PL1)"),
            Description = raisePl1 is { } w
                ? T($"Com PL1 de {spec!.Pl1Watts} W o {c.Cpu.ShortName} perde clock depois de {spec.TauSeconds} s de carga pesada. Subir o PL1 para até {w} W mantém o Turbo por mais tempo. Faça só com um cooler melhor que o box e confira no monitor: abaixo de ~85 °C sob carga. Placas de entrada (VRM simples) também esquentam mais.",
                    $"With PL1 at {spec!.Pl1Watts} W the {c.Cpu.ShortName} drops clocks after {spec.TauSeconds} s of heavy load. Raising PL1 up to {w} W keeps Turbo longer. Only with a cooler better than the stock one, and check the monitor: below ~85 °C under load. Entry-level boards (basic VRM) also run hotter.")
                : T("Aumentar o PL1 acima do TDP mantém o Turbo por mais tempo em cargas longas. Suba aos poucos e confira a temperatura sob carga (abaixo de ~85 °C). Não use \"remover todos os limites\" sem saber a capacidade do VRM da placa.",
                    "Raising PL1 above TDP keeps Turbo longer in long workloads. Increase gradually and check the temperature under load (below ~85 °C). Avoid \"remove all limits\" without knowing the board's VRM capacity."),
            RecommendedValue = raisePl1 is { } watts
                ? T($"PL1 (Long Duration): {watts} W · PL2 (Short Duration): {spec!.Pl2Watts} W · Tau: {spec.TauSeconds} s", $"PL1 (Long Duration): {watts} W · PL2 (Short Duration): {spec!.Pl2Watts} W · Tau: {spec.TauSeconds} s")
                : T("PL1 acima do TDP, em passos, com teste de temperatura", "PL1 above TDP, in steps, with temperature testing"),
            Evidence = Evidence.NeedsBiosCheck, Compliance = Compliance.Unknown,
            ExpectedBenefit = T("Mais desempenho em cargas longas (compilar, renderizar, jogos pesados na CPU) e 1% lows mais estáveis quando a CPU limita.",
                "More performance in long workloads (compiling, rendering, CPU-heavy games) and steadier 1% lows when CPU-bound."),
            Gain = Level.Medium, Risk = Level.Medium, Thermal = ThermalImpact.High,
            Rollback = UndoToAuto,
        };

        var enhancementNames = c.Db.OptionNamesFor(c.Vendor.Id, Settings.PowerEnhancement);
        var isAsus = c.Vendor is AsusVendor;
        yield return new AdvisorRecommendation
        {
            Id = "vendor-enhancement", BiosTarget = @"^Auto", SettingId = Settings.PowerEnhancement, Category = AdvisorCategory.Power,
            Name = isAsus ? T("ASUS MultiCore Enhancement", "ASUS MultiCore Enhancement") : enhancementNames is { Length: > 0 } ? T(enhancementNames[0], enhancementNames[0]) : T("Aprimoramento da fabricante (MultiCore Enhancement e similares)", "Vendor enhancement (MultiCore Enhancement and similar)"),
            Description = T("Opção da fabricante que muda os limites de Turbo e energia. \"Remover todos os limites\" pode passar muito do consumo previsto: não é recomendado sem refrigeração e VRM adequados.",
                "Vendor option that changes Turbo and power limits. \"Remove all limits\" can go far past the expected power draw: not recommended without suitable cooling and VRM."),
            RecommendedValue = isAsus ? T("Auto - Lets BIOS Optimize", "Auto - Lets BIOS Optimize") : T("Auto (padrão)", "Auto (default)"),
            Evidence = Evidence.NeedsBiosCheck, Compliance = Compliance.Unknown,
            ExpectedBenefit = T("Turbo otimizado pela placa, sem tirar as proteções de energia.", "Board-optimized Turbo without removing power protections."),
            Gain = Level.Medium, Risk = Level.Low, Thermal = ThermalImpact.Medium,
            Rollback = UndoToAuto,
        };
    }
}
