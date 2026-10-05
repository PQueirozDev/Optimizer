using static PQueirozOptimizer.BiosAdvisor.R;

namespace PQueirozOptimizer.BiosAdvisor.Rules;

/// <summary>Turbo Boost, SpeedStep, Speed Shift, C-States e Thermal Monitor em processadores Intel.</summary>
public sealed class IntelCpuRules : IAdvisorRule
{
    public IEnumerable<AdvisorRecommendation> Evaluate(AdvisorContext c)
    {
        if (!c.IsIntel) yield break;
        var hasTurbo = c.Cpu.Brand is "Core" or "Core Ultra";

        if (hasTurbo || c.Cpu.Brand == "")
        {
            var peak = c.Profile.Observed?.PeakProcessorPerformancePercent;
            var observed = peak is > 103;
            var mhz = peak is { } p && c.Profile.Cpu.BaseClockMhz > 0 ? (int)Math.Round(c.Profile.Cpu.BaseClockMhz * p / 100 / 50) * 50 : (int?)null;
            yield return new AdvisorRecommendation
            {
                Id = "intel-turbo", BiosTarget = @"^(Enabled|Auto)$", SettingId = Settings.IntelTurbo, Category = AdvisorCategory.Cpu,
                Name = T("Intel Turbo Boost", "Intel Turbo Boost"),
                Description = T("Deixa o processador subir acima do clock base quando há margem de energia e temperatura.",
                    "Lets the CPU run above its base clock when there is power and thermal headroom."),
                RecommendedValue = Enabled,
                CurrentValue = observed ? T($"Turbo em uso (~{mhz} MHz estimados pelo Windows)", $"Turbo in use (~{mhz} MHz estimated by Windows)") : null,
                Evidence = observed ? Evidence.Detected : Evidence.NeedsBiosCheck,
                Compliance = observed ? Compliance.Ok : Compliance.Unknown,
                ExpectedBenefit = T("Clock bem maior em jogos e tarefas leves; desligado, o processador fica preso no clock base.",
                    "Much higher clocks in games and light tasks; when off, the CPU is stuck at base clock."),
                Gain = Level.High, Risk = Level.Low, Thermal = ThermalImpact.Medium,
                Rollback = Undo("Enabled/Auto", "Enabled/Auto"),
            };
        }

        yield return new AdvisorRecommendation
        {
            Id = "intel-speedstep", BiosTarget = @"^(Enabled|Auto)$", SettingId = Settings.IntelSpeedStep, Category = AdvisorCategory.Cpu,
            Name = T("Intel SpeedStep", "Intel SpeedStep"),
            Description = T("Permite variar a frequência e a tensão conforme a carga. Em muitas BIOS o Speed Shift depende dele.",
                "Allows frequency and voltage to change with load. On many BIOSes Speed Shift depends on it."),
            RecommendedValue = AutoOrEnabled,
            Evidence = Evidence.NeedsBiosCheck, Compliance = Compliance.Unknown,
            ExpectedBenefit = T("Mantém o Turbo e o Speed Shift funcionando; desligar não aumenta o FPS.", "Keeps Turbo and Speed Shift working; turning it off won't raise FPS."),
            Gain = Level.Low, Risk = Level.Low, Thermal = ThermalImpact.None,
            Rollback = UndoToAuto,
        };

        if (c.Cpu.Brand == "Core Ultra" || c.Cpu.Generation >= 6)
            yield return new AdvisorRecommendation
            {
                Id = "intel-speedshift", BiosTarget = @"^(Enabled|Auto)$", SettingId = Settings.IntelSpeedShift, Category = AdvisorCategory.Latency,
                Name = T("Intel Speed Shift", "Intel Speed Shift"),
                Description = T("O próprio processador escolhe a frequência (P-states por hardware), em vez de esperar o Windows.",
                    "The CPU picks its own frequency (hardware P-states) instead of waiting for Windows."),
                RecommendedValue = Enabled,
                Evidence = Evidence.NeedsBiosCheck, Compliance = Compliance.Unknown,
                ExpectedBenefit = T("Resposta mais rápida do processador às mudanças de carga.", "Faster CPU response to load changes."),
                Gain = Level.Medium, Risk = Level.Low, Thermal = ThermalImpact.None,
                Rollback = UndoToAuto,
            };

        yield return new AdvisorRecommendation
        {
            Id = "intel-thermal-monitor", BiosTarget = @"^(Enabled|Auto)$", SettingId = Settings.ThermalMonitor, Category = AdvisorCategory.Thermal,
            Name = T("Thermal Monitor", "Thermal Monitor"),
            Description = T("Proteção que reduz o clock quando o processador chega ao limite de temperatura. Nunca desative.",
                "Protection that lowers clocks when the CPU reaches its temperature limit. Never disable it."),
            RecommendedValue = Enabled,
            Evidence = Evidence.NeedsBiosCheck, Compliance = Compliance.Unknown,
            ExpectedBenefit = T("Evita danos e travamentos por superaquecimento.", "Prevents damage and crashes from overheating."),
            Gain = Level.Low, Risk = Level.Low, Thermal = ThermalImpact.None,
            Rollback = Undo("Enabled", "Enabled"),
        };

        // C-States: troca de economia por resposta; opcional e só para desktop no modo competitivo
        if (c.IsDesktop)
            yield return new AdvisorRecommendation
            {
                Id = "intel-cstates", SettingId = Settings.CStates, Category = AdvisorCategory.Latency, MinPreset = AdvisorPreset.Competitive, Weight = 0,
                Name = T("CPU C-States (opcional)", "CPU C-States (optional)"),
                Description = T("Os estados de economia fazem os núcleos \"dormirem\" quando ociosos. Desligar reduz o tempo de despertar, mas aumenta consumo e temperatura em repouso. O ganho em FPS costuma ser pequeno: meça antes e depois.",
                    "Power-saving states let idle cores sleep. Turning them off reduces wake-up time but raises idle power and temperature. The FPS gain is usually small: measure before and after."),
                RecommendedValue = T("Disabled (opcional) — ou Enabled/Auto para economia", "Disabled (optional) — or Enabled/Auto to save power"),
                Evidence = Evidence.NeedsBiosCheck, Compliance = Compliance.Info,
                ExpectedBenefit = T("Latência um pouco menor e frametime mais estável em alguns jogos competitivos.", "Slightly lower latency and steadier frametimes in some competitive games."),
                Gain = Level.Low, Risk = Level.Low, Thermal = ThermalImpact.Medium,
                Rollback = Undo("Auto/Enabled", "Auto/Enabled"),
            };
    }
}
