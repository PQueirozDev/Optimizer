using static PQueirozOptimizer.BiosAdvisor.R;

namespace PQueirozOptimizer.BiosAdvisor.Rules;

/// <summary>Precision Boost, PBO, CPPC/Preferred Cores e C-States em Ryzen.</summary>
public sealed class AmdCpuRules : IAdvisorRule
{
    public IEnumerable<AdvisorRecommendation> Evaluate(AdvisorContext c)
    {
        if (!c.IsAmd) yield break;
        var peak = c.Profile.Observed?.PeakProcessorPerformancePercent;
        var observed = peak is > 103;

        yield return new AdvisorRecommendation
        {
            Id = "amd-cpb", SettingId = Settings.AmdCpb, Category = AdvisorCategory.Cpu,
            Name = T("Precision Boost (Core Performance Boost)", "Precision Boost (Core Performance Boost)"),
            Description = T("O boost da AMD: deixa os núcleos subirem acima do clock base conforme temperatura e energia.",
                "AMD's boost: lets cores run above base clock depending on temperature and power."),
            RecommendedValue = AutoOrEnabled,
            CurrentValue = observed ? T("Boost em uso (observado pelo Windows)", "Boost in use (observed by Windows)") : null,
            Evidence = observed ? Evidence.Detected : Evidence.NeedsBiosCheck,
            Compliance = observed ? Compliance.Ok : Compliance.Unknown,
            ExpectedBenefit = T("Clock bem maior em jogos; desligado, o processador fica no clock base.", "Much higher clocks in games; when off, the CPU stays at base clock."),
            Gain = Level.High, Risk = Level.Low, Thermal = ThermalImpact.Medium,
            Rollback = UndoToAuto,
        };

        // CPPC: Ryzen 3000 em diante informa ao Windows quais núcleos são os mais rápidos
        if (c.Cpu.Generation >= 3)
            yield return new AdvisorRecommendation
            {
                Id = "amd-cppc", SettingId = Settings.AmdCppc, Category = AdvisorCategory.Latency,
                Name = T("CPPC e Preferred Cores", "CPPC and Preferred Cores"),
                Description = T("Informam ao Windows quais núcleos alcançam o maior clock, para as tarefas leves e os jogos irem para eles.",
                    "Tell Windows which cores reach the highest clocks, so light tasks and games run on them."),
                RecommendedValue = AutoOrEnabled,
                Evidence = Evidence.NeedsBiosCheck, Compliance = Compliance.Unknown,
                ExpectedBenefit = T("Melhor uso dos núcleos mais rápidos e, nos X3D, do núcleo com o cache extra.", "Better use of the fastest cores and, on X3D chips, of the cores with extra cache."),
                Gain = Level.Medium, Risk = Level.Low, Thermal = ThermalImpact.None,
                Rollback = UndoToAuto,
            };

        // PBO: não existe nos 5000 X3D; precisa de chipset com overclock e de desktop
        var x3dLocked = c.Cpu.IsX3D && c.Cpu.Generation == 5;
        if (c.IsDesktop && c.Cpu.Generation >= 3 && !x3dLocked && c.Platform.ChipsetAllowsCpuOverclock != false)
            yield return new AdvisorRecommendation
            {
                Id = "amd-pbo", SettingId = Settings.AmdPbo, Category = AdvisorCategory.Power, MinPreset = AdvisorPreset.Performance,
                Name = T("Precision Boost Overdrive (PBO)", "Precision Boost Overdrive (PBO)"),
                Description = c.Cpu.IsX3D
                    ? T("Nos X3D o PBO só amplia os limites de energia dentro do que a AMD permite. Ative sem mexer em tensões e confira a temperatura no monitor.",
                        "On X3D chips PBO only widens power limits within what AMD allows. Enable it without touching voltages and check the temperature in the monitor.")
                    : T("Aumenta os limites de energia e corrente do boost (PPT/TDC/EDC) conforme a placa. Ganho moderado com mais calor: confira a temperatura e mantenha tensões em Auto.",
                        "Raises boost power and current limits (PPT/TDC/EDC) based on the board. Moderate gain with more heat: check the temperature and keep voltages on Auto."),
                RecommendedValue = T("Enabled (limites da placa), sem offset de tensão", "Enabled (board limits), no voltage offset"),
                Evidence = Evidence.NeedsBiosCheck, Compliance = Compliance.Unknown,
                ExpectedBenefit = T("Clocks mais altos por mais tempo em cargas multi-núcleo e jogos pesados na CPU.", "Higher clocks for longer in multi-core loads and CPU-heavy games."),
                Gain = Level.Medium, Risk = Level.Medium, Thermal = ThermalImpact.High,
                Rollback = Undo("Auto/Disabled", "Auto/Disabled"),
            };

        // Nos Ryzen o boost depende dos núcleos ociosos entrarem em C6: desligar C-States costuma reduzir o boost
        if (c.IsDesktop)
            yield return new AdvisorRecommendation
            {
                Id = "amd-cstates", SettingId = Settings.CStates, Category = AdvisorCategory.Latency, MinPreset = AdvisorPreset.Competitive, Weight = 0,
                Name = T("Global C-state Control", "Global C-state Control"),
                Description = T("Nos Ryzen o boost de um núcleo depende de os outros dormirem (C6). Desligar os C-States costuma reduzir o boost e aumentar temperatura, sem ganho garantido de latência.",
                    "On Ryzen, single-core boost depends on the other cores sleeping (C6). Turning C-States off usually lowers boost and raises temperature, with no guaranteed latency gain."),
                RecommendedValue = T("Auto (manter)", "Auto (keep)"),
                Evidence = Evidence.NeedsBiosCheck, Compliance = Compliance.Info,
                ExpectedBenefit = T("Mantém o boost máximo de um núcleo, importante em jogos.", "Keeps the maximum single-core boost, which matters in games."),
                Gain = Level.Low, Risk = Level.Low, Thermal = ThermalImpact.None,
                Rollback = UndoToAuto,
            };
    }
}
