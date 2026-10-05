using static PQueirozOptimizer.BiosAdvisor.R;

namespace PQueirozOptimizer.BiosAdvisor.Rules;

/// <summary>Hyper-Threading/SMT e núcleos ativos: os dois o Windows consegue ver.</summary>
public sealed class CommonCpuRules : IAdvisorRule
{
    public IEnumerable<AdvisorRecommendation> Evaluate(AdvisorContext c)
    {
        var cpu = c.Profile.Cpu;
        if (cpu.Cores <= 0 || cpu.Threads <= 0) yield break;

        // SMT: dois threads por núcleo = ligado. Um por núcleo só é "desligado" quando sabemos que o modelo tem SMT.
        // Intel híbridos (12ª–14ª): só os P-cores têm HT, então threads < 2 × núcleos é normal; Core Ultra 200 não tem HT.
        var hybrid = c.Cpu is { Vendor: CpuVendor.Intel, Brand: "Core", Generation: >= 12 };
        var smtOn = hybrid ? cpu.Threads > cpu.Cores : cpu.Threads >= cpu.Cores * 2;
        var modelHasSmt = c.Cpu switch
        {
            { Brand: "Core Ultra" } => false,
            { Vendor: CpuVendor.Intel, Brand: "Core", Generation: >= 10 } => true,
            { Vendor: CpuVendor.Amd, Brand: "Ryzen", Tier: >= 5 } => true,
            _ => (bool?)null,
        };
        var smtName = c.IsAmd ? T("SMT (Simultaneous Multithreading)", "SMT (Simultaneous Multithreading)") : T("Hyper-Threading", "Hyper-Threading");
        if (modelHasSmt != false && (smtOn || modelHasSmt == true))
            yield return new AdvisorRecommendation
            {
                Id = "cpu-smt", SettingId = Settings.Smt, Category = AdvisorCategory.Cpu,
                Name = smtName,
                Description = T("Dois threads por núcleo. Jogos atuais e o Windows aproveitam os threads extras; desligar raramente melhora o FPS e piora o desempenho geral.",
                    "Two threads per core. Modern games and Windows use the extra threads; turning it off rarely improves FPS and hurts overall performance."),
                RecommendedValue = Enabled,
                CurrentValue = T($"{cpu.Cores} núcleos / {cpu.Threads} threads", $"{cpu.Cores} cores / {cpu.Threads} threads"),
                Evidence = smtOn && !hybrid ? Evidence.Detected : Evidence.Inferred,
                Compliance = smtOn ? Compliance.Ok : Compliance.Attention,
                ExpectedBenefit = T("Mais desempenho em tarefas paralelas e menos travadas com programas em segundo plano.", "More performance in parallel work and fewer stutters with background apps."),
                Gain = Level.Medium, Risk = Level.Low, Thermal = ThermalImpact.Low,
                Rollback = Undo("Enabled", "Enabled"),
            };

        var allCores = cpu.EnabledCores <= 0 || cpu.EnabledCores >= cpu.Cores;
        yield return new AdvisorRecommendation
        {
            Id = "cpu-active-cores", SettingId = Settings.ActiveCores, Category = AdvisorCategory.Cpu,
            Name = c.IsAmd ? T("Núcleos ativos (Downcore Control)", "Active cores (Downcore Control)") : T("Active Processor Cores", "Active Processor Cores"),
            Description = T("Quantos núcleos a BIOS libera para o Windows. Desligar núcleos não traz ganho em jogos atuais.",
                "How many cores the BIOS exposes to Windows. Disabling cores brings no gain in current games."),
            RecommendedValue = T("All (todos)", "All"),
            CurrentValue = cpu.EnabledCores > 0 ? T($"{cpu.EnabledCores} de {cpu.Cores} núcleos ativos", $"{cpu.EnabledCores} of {cpu.Cores} cores enabled") : null,
            Evidence = cpu.EnabledCores > 0 ? Evidence.Detected : Evidence.NeedsBiosCheck,
            Compliance = cpu.EnabledCores > 0 ? (allCores ? Compliance.Ok : Compliance.Attention) : Compliance.Unknown,
            ExpectedBenefit = T("Todos os núcleos disponíveis para jogos e programas.", "All cores available to games and apps."),
            Gain = allCores ? Level.Low : Level.High, Risk = Level.Low, Thermal = ThermalImpact.Low,
            Rollback = Undo("All", "All"),
        };
    }
}
