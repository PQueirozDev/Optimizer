using static PQueirozOptimizer.BiosAdvisor.R;

namespace PQueirozOptimizer.BiosAdvisor.Rules;

/// <summary>XMP/EXPO, canal duplo, slots, timings e tensão da memória.</summary>
public sealed class MemoryRules : IAdvisorRule
{
    public IEnumerable<AdvisorRecommendation> Evaluate(AdvisorContext c)
    {
        var mem = c.Memory;
        var modules = c.Profile.Memory.Modules;
        if (modules.Count == 0) yield break;
        var ddr = c.Profile.Memory.Type switch { MemoryType.Ddr5 => "DDR5", MemoryType.Ddr4 => "DDR4", MemoryType.Ddr3 => "DDR3", _ => "" };

        // ---------- Perfil XMP / EXPO ----------
        var profileName = c.IsAmd ? (c.Profile.Memory.Type == MemoryType.Ddr5 ? "EXPO / XMP" : "XMP / DOCP") : "XMP";
        Evidence evidence;
        Compliance compliance;
        LocalizedText current;
        if (mem.BelowExpected)
        {
            // A velocidade é medida; o que o pente suporta pode ter vindo do part number (dedução)
            evidence = mem.RatedEvidence == Evidence.Inferred ? Evidence.Inferred : Evidence.Detected; compliance = Compliance.Attention;
            current = T($"{mem.ConfiguredMhz} MHz (o pente/plataforma permitem ~{mem.ExpectedMhz} MHz)", $"{mem.ConfiguredMhz} MHz (module/platform allow ~{mem.ExpectedMhz} MHz)");
        }
        else if (mem.ExpectedMhz is { } expected && mem.ConfiguredMhz >= expected - 100)
        {
            // Velocidade já no máximo do pente/plataforma: nada a ganhar em frequência. O perfil em si não é lido (dedução)
            evidence = Evidence.Inferred; compliance = Compliance.Ok;
            current = T($"{mem.ConfiguredMhz} MHz — velocidade máxima do pente/plataforma atingida", $"{mem.ConfiguredMhz} MHz — module/platform maximum speed reached");
        }
        else if (MemoryAnalyzer.IsLowJedec(mem.ConfiguredMhz, c.Profile.Memory.Type))
        {
            evidence = Evidence.Inferred; compliance = Compliance.Attention;
            current = T($"{mem.ConfiguredMhz} MHz (padrão JEDEC; XMP aparentemente não utilizado)", $"{mem.ConfiguredMhz} MHz (JEDEC default; XMP appears unused)");
        }
        else
        {
            evidence = Evidence.NeedsBiosCheck; compliance = Compliance.Unknown;
            current = T($"{mem.ConfiguredMhz} MHz", $"{mem.ConfiguredMhz} MHz");
        }
        var capNote = c.Platform.MemoryCapMhz is { } cap
            ? T($" No chipset {c.Platform.Chipset} com este processador a memória vai até {cap} MHz, mesmo com pentes mais rápidos: o XMP ainda aplica os timings do perfil.",
                $" On the {c.Platform.Chipset} chipset with this CPU memory goes up to {cap} MHz, even with faster modules: XMP still applies the profile timings.")
            : T("", "");
        yield return new AdvisorRecommendation
        {
            Id = "mem-profile", BiosTarget = @"^(?!.*(Disabled|Auto|Manual)).*(XMP|EXPO|DOCP|Profile|Enabled)", SettingId = Settings.MemoryProfile, Category = AdvisorCategory.Ram,
            Name = T($"Perfil de memória ({profileName})", $"Memory profile ({profileName})"),
            Description = T("Sem o perfil a memória roda na velocidade padrão JEDEC, mais lenta que a anunciada no pente." + capNote.Pt,
                "Without the profile, memory runs at the slower JEDEC default instead of the advertised speed." + capNote.En),
            RecommendedValue = c.Vendor is AsusVendor && !c.IsAmd ? T("XMP I ou XMP II (Ai Overclock Tuner)", "XMP I or XMP II (Ai Overclock Tuner)") : T($"{profileName} — perfil 1", $"{profileName} — profile 1"),
            CurrentValue = current,
            Evidence = evidence, Compliance = compliance,
            ExpectedBenefit = T("Costuma ser o maior ganho de BIOS em jogos: mais FPS e 1% lows melhores, principalmente quando a CPU limita.",
                "Usually the biggest BIOS gain in games: more FPS and better 1% lows, especially when CPU-bound."),
            Gain = Level.High, Risk = Level.Low, Thermal = ThermalImpact.Low,
            Rollback = T("Volte o perfil para Auto (velocidade padrão) e salve com F10. Se o PC não ligar ou reiniciar em loop, a placa costuma voltar sozinha ao padrão; se não voltar, limpe a CMOS.",
                "Set the profile back to Auto (default speed) and save with F10. If the PC fails to boot or keeps restarting, the board usually reverts on its own; if not, clear the CMOS."),
        };

        // ---------- Canais ----------
        var dual = mem.Channels is ChannelMode.Dual or ChannelMode.Multi;
        yield return new AdvisorRecommendation
        {
            Id = "mem-channels", Category = AdvisorCategory.Ram,
            Name = T("Canal duplo (Dual Channel)", "Dual channel"),
            Description = modules.Count == 1
                ? T("Com um pente só, a memória usa um canal. Um segundo pente igual dobra a largura de banda. Não é uma opção de BIOS: é instalação.",
                    "With a single module, memory uses one channel. A second identical module doubles bandwidth. This isn't a BIOS option: it's installation.")
                : T("Os pentes precisam estar em canais diferentes (A e B) para funcionar em canal duplo. Não é uma opção de BIOS: é a posição nos slots.",
                    "Modules must sit on different channels (A and B) to run in dual channel. This isn't a BIOS option: it's the slot placement."),
            RecommendedValue = T("Dual Channel (pentes em canais A e B)", "Dual channel (modules on channels A and B)"),
            CurrentValue = mem.Channels switch
            {
                ChannelMode.Single => T($"Single Channel · {modules.Count} pente(s)", $"Single channel · {modules.Count} module(s)"),
                ChannelMode.Dual => T($"Dual Channel · {modules.Count} pentes", $"Dual channel · {modules.Count} modules"),
                ChannelMode.Multi => T("Multi Channel", "Multi channel"),
                _ => T("Não identificado pelo nome dos slots", "Not identified from slot names"),
            },
            Evidence = mem.ChannelEvidence,
            Compliance = mem.Channels == ChannelMode.Unknown ? Compliance.Unknown : dual ? Compliance.Ok : Compliance.Attention,
            ExpectedBenefit = T("Até o dobro de largura de banda: FPS e 1% lows melhores, principalmente com vídeo integrado.", "Up to double the bandwidth: better FPS and 1% lows, especially with integrated graphics."),
            Gain = Level.High, Risk = Level.Low, Thermal = ThermalImpact.None,
            Rollback = T("Desligue o PC e devolva os pentes aos slots anteriores.", "Power off the PC and put the modules back in their previous slots."),
        };

        // ---------- Slots recomendados (só com dados da placa) ----------
        if (c.Board?.TwoModuleSlots is { } recommended && modules.Count == 2)
        {
            var used = mem.SlotsUsed.Select(s => s.ToUpperInvariant()).OrderBy(s => s).ToArray();
            var known = used.All(s => s.Length == 2);
            var matches = known && used.SequenceEqual(recommended.OrderBy(s => s));
            yield return new AdvisorRecommendation
            {
                Id = "mem-slots", Category = AdvisorCategory.Ram,
                Name = T("Slots da memória", "Memory slots"),
                Description = c.Board.TwoModuleSlotsConfirmed
                    ? T($"Com dois pentes, o manual da {c.Board.Name} indica os slots {string.Join(" e ", recommended)}.", $"With two modules, the {c.Board.Name} manual indicates slots {string.Join(" and ", recommended)}.")
                    : T($"Com dois pentes em placas ASUS de 4 slots a indicação usual é {string.Join(" e ", recommended)}; confira a figura \"Recommended memory configurations\" no manual da placa.",
                        $"With two modules on 4-slot ASUS boards the usual guidance is {string.Join(" and ", recommended)}; check the \"Recommended memory configurations\" figure in the board manual."),
                RecommendedValue = T(string.Join(" + ", recommended), string.Join(" + ", recommended)),
                CurrentValue = T(string.Join(" + ", mem.SlotsUsed), string.Join(" + ", mem.SlotsUsed)),
                Evidence = known ? Evidence.Inferred : Evidence.NeedsBiosCheck,
                Compliance = !known ? Compliance.Unknown : matches ? Compliance.Ok : Compliance.Attention,
                ExpectedBenefit = T("Melhor qualidade de sinal: mais estabilidade com XMP.", "Better signal quality: more stability with XMP."),
                Gain = Level.Low, Risk = Level.Low, Thermal = ThermalImpact.None,
                Rollback = T("Desligue o PC e devolva os pentes aos slots anteriores.", "Power off the PC and put the modules back in their previous slots."),
            };
        }

        // ---------- Timings e tensão: informativos (o Windows não expõe) ----------
        yield return new AdvisorRecommendation
        {
            Id = "mem-timings", SettingId = Settings.MemoryTimings, Category = AdvisorCategory.Ram, MinPreset = AdvisorPreset.Performance, Weight = 0,
            Name = T("Timings primários", "Primary timings"),
            Description = T("O Windows não informa os timings. Use os do perfil XMP/EXPO; ajuste manual (CL, tRCD, tRP, tRAS) só com teste de estabilidade de horas (ex.: TestMem5, Karhu) depois de cada mudança.",
                "Windows doesn't report timings. Use the XMP/EXPO profile ones; manual tuning (CL, tRCD, tRP, tRAS) only with hours-long stability testing (e.g. TestMem5, Karhu) after each change."),
            RecommendedValue = T("Os do perfil (Auto com XMP/EXPO)", "Profile values (Auto with XMP/EXPO)"),
            CurrentValue = mem.CasLatency is { } cl ? T($"CL{cl} pelo part number (dedução)", $"CL{cl} from the part number (inferred)") : T("Não disponível pelo Windows", "Not available from Windows"),
            Evidence = mem.CasLatency is null ? Evidence.NeedsBiosCheck : Evidence.Inferred, Compliance = Compliance.Info,
            ExpectedBenefit = T("Timings mais apertados reduzem a latência da memória, mas o ganho exige testes e varia por jogo.", "Tighter timings reduce memory latency, but the gain needs testing and varies by game."),
            Gain = Level.Medium, Risk = Level.Medium, Thermal = ThermalImpact.Low,
            Rollback = T("Volte os timings para Auto (o perfil aplica os do pente) e salve com F10.", "Set timings back to Auto (the profile applies the module values) and save with F10."),
        };

        var voltage = c.Profile.Memory.Type switch { MemoryType.Ddr4 => "1,35 V", MemoryType.Ddr5 => "1,25–1,40 V", _ => null };
        yield return new AdvisorRecommendation
        {
            Id = "mem-voltage", SettingId = Settings.MemoryVoltage, Category = AdvisorCategory.Ram, MinPreset = AdvisorPreset.Performance, Weight = 0,
            Name = T("DRAM Voltage", "DRAM Voltage"),
            Description = T($"O perfil XMP/EXPO já aplica a tensão do pente{(voltage is null ? "" : $" ({ddr} com perfil costuma usar {voltage})")}. Não aumente além do que o fabricante do pente especifica.",
                $"The XMP/EXPO profile already applies the module voltage{(voltage is null ? "" : $" ({ddr} with a profile usually uses {voltage.Replace(',', '.')})")}. Never go above what the module maker specifies."),
            RecommendedValue = T("Auto (valor do perfil)", "Auto (profile value)"),
            Evidence = Evidence.NeedsBiosCheck, Compliance = Compliance.Info,
            ExpectedBenefit = T("Estabilidade com o perfil ativo.", "Stability with the profile enabled."),
            Gain = Level.Low, Risk = Level.Low, Thermal = ThermalImpact.Low,
            Rollback = UndoToAuto,
        };
    }
}
