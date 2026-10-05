using System.Text.RegularExpressions;

namespace PQueirozOptimizer.BiosAdvisor;

public enum ChannelMode { Unknown, Single, Dual, Multi }

/// <summary>Resultado da análise de memória. Evidência separada por item: canais e velocidade são medidos; o perfil do pente é deduzido.</summary>
public sealed record MemoryAnalysis(
    ChannelMode Channels,
    Evidence ChannelEvidence,
    int ConfiguredMhz,
    int? ModuleRatedMhz,
    Evidence RatedEvidence,
    int? ExpectedMhz,
    int? CasLatency,
    bool MixedModules,
    IReadOnlyList<string> SlotsUsed,
    IReadOnlyList<LocalizedText> Issues)
{
    /// <summary>A memória roda claramente abaixo do que o pente e a plataforma permitem.</summary>
    public bool BelowExpected => ExpectedMhz is { } expected && ConfiguredMhz > 0 && ConfiguredMhz < expected - 100;
}

/// <summary>
/// Analisa os pentes: canais pelo nome do slot (DeviceLocator), velocidade configurada x velocidade informada pelo
/// SMBIOS x velocidade do produto (part number). Timings não são expostos pelo Windows: só o CL deduzido do part number.
/// </summary>
public static class MemoryAnalyzer
{
    // Velocidades JEDEC (padrão sem XMP/EXPO) de cada geração
    private static readonly int[] JedecDdr4 = { 1600, 1866, 2133, 2400, 2666, 2933, 3200 };
    private static readonly int[] JedecDdr5 = { 4000, 4400, 4800, 5200, 5600, 6000, 6400 };

    /// <summary>Canal pelo nome do slot: "ChannelA-DIMM2", "DIMM_A1", "P0 CHANNEL A", "A1_DIMM0". Null = formato desconhecido.</summary>
    public static string? ChannelOf(string locator, string bank)
    {
        foreach (var text in new[] { locator ?? "", bank ?? "" })
        {
            var m = Regex.Match(text, @"CHANNEL\s*([A-H])\b|Channel([A-H])-|DIMM[_ ]?([A-H])\d|^([A-H])\d?_?DIMM|\bCH(?:ANNEL)?[_ ]?([A-H])\b", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value.ToUpperInvariant();
        }
        return null;
    }

    /// <summary>Slot no formato curto da placa ("A2"), quando o nome permite.</summary>
    public static string? SlotOf(string locator)
    {
        var m = Regex.Match(locator ?? "", @"Channel([A-H])-DIMM(\d)|DIMM[_ ]?([A-H])(\d)|^([A-H])(\d)$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var groups = m.Groups.Cast<Group>().Skip(1).Where(g => g.Success).Select(g => g.Value).ToArray();
        // ChannelA-DIMM0/1 (ASUS numera 1 e 2; algumas placas 0 e 1)
        return groups.Length == 2 ? groups[0].ToUpperInvariant() + groups[1] : null;
    }

    /// <summary>Velocidade e CL do produto pelo part number. Só padrões conhecidos; o resto fica null.</summary>
    public static (int? Mhz, int? Cl) ParsePartNumber(string partNumber)
    {
        var p = (partNumber ?? "").Trim().ToUpperInvariant();
        if (p.Length == 0) return (null, null);
        Match m;
        // G.Skill: F4-3200C16D-16GVKB · F5-6000J3038F16G
        if ((m = Regex.Match(p, @"^F[45]-(\d{4})[CJ](\d{2})")).Success) return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        // Corsair: CMK16GX4M2B3200C16 · CMH32GX5M2B6000C30
        if ((m = Regex.Match(p, @"^CM[A-Z]\w*?X[45]M\d[A-Z](\d{4})C(\d{2})")).Success) return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        // Kingston Fury/HyperX: KF432C16BB/8 · HX432C16FB3/8 · KF560C36BBE-16
        // (o primeiro dígito é a geração DDR: KF4 32 = DDR4-3200)
        if ((m = Regex.Match(p, @"^(?:KF|HX)[345](\d{2})C(\d{2})")).Success) return (int.Parse(m.Groups[1].Value) * 100, int.Parse(m.Groups[2].Value));
        // Texto direto: "DDR4 3000", "DDR4-3200"
        if ((m = Regex.Match(p, @"DDR[45][- ]?(\d{4})\b")).Success) return (int.Parse(m.Groups[1].Value), null);
        return (null, null);
    }

    public static MemoryAnalysis Analyze(MemoryInfo memory, PlatformInfo platform)
    {
        var modules = memory.Modules;
        var issues = new List<LocalizedText>();
        if (modules.Count == 0)
            return new MemoryAnalysis(ChannelMode.Unknown, Evidence.NeedsBiosCheck, 0, null, Evidence.NeedsBiosCheck, null, null, false, Array.Empty<string>(), issues);

        // ---------- Canais ----------
        var channels = modules.Select(m => ChannelOf(m.Locator, m.Bank)).ToList();
        ChannelMode mode;
        Evidence channelEvidence;
        if (modules.Count == 1) { mode = ChannelMode.Single; channelEvidence = Evidence.Detected; }
        else if (channels.All(c => c != null))
        {
            var distinct = channels.Distinct().Count();
            mode = distinct >= 3 ? ChannelMode.Multi : distinct == 2 ? ChannelMode.Dual : ChannelMode.Single;
            // O nome do slot indica a posição, não o modo que o controlador negociou: dedução forte, mas dedução
            channelEvidence = Evidence.Inferred;
        }
        else { mode = ChannelMode.Unknown; channelEvidence = Evidence.NeedsBiosCheck; }

        if (mode == ChannelMode.Single && modules.Count == 1)
            issues.Add(new("Só um pente instalado: a memória roda em canal único (Single Channel), com cerca de metade da largura de banda.",
                "Only one module installed: memory runs in single channel, with about half the bandwidth."));
        else if (mode == ChannelMode.Single)
            issues.Add(new("Os pentes parecem estar no mesmo canal: mova um deles para o outro canal (veja os slots recomendados).",
                "The modules seem to be on the same channel: move one to the other channel (see the recommended slots)."));

        // ---------- Velocidade ----------
        var configured = memory.ConfiguredMhz;
        var parsed = modules.Select(m => ParsePartNumber(m.PartNumber)).ToList();
        int? productMhz = parsed.All(x => x.Mhz != null) ? parsed.Min(x => x.Mhz) : null;
        int? cl = parsed.All(x => x.Cl != null) ? parsed.Max(x => x.Cl) : null;
        var smbiosRated = modules.Where(m => m.RatedMhz > 0).Select(m => m.RatedMhz).DefaultIfEmpty(0).Min();
        int? rated = productMhz ?? (smbiosRated > 0 ? smbiosRated : null);
        var ratedEvidence = productMhz != null ? Evidence.Inferred : smbiosRated > 0 ? Evidence.Detected : Evidence.NeedsBiosCheck;
        int? expected = rated is { } r ? Math.Min(r, platform.MemoryCapMhz ?? int.MaxValue) : null;
        var mixed = modules.Select(m => (m.PartNumber ?? "").Trim().ToUpperInvariant()).Distinct().Count() > 1
                    || modules.Select(m => m.CapacityBytes).Distinct().Count() > 1;

        if (expected is { } exp && configured > 0 && configured < exp - 100)
            issues.Add(new($"A memória está a {configured} MHz, mas o pente e a plataforma permitem cerca de {exp} MHz: o perfil XMP/EXPO parece desligado.",
                $"Memory runs at {configured} MHz, but the module and platform allow about {exp} MHz: the XMP/EXPO profile seems to be off."));
        else if (expected is null && configured > 0 && IsLowJedec(configured, memory.Type))
            issues.Add(new($"A memória está a {configured} MHz, velocidade padrão JEDEC: se os pentes forem de uma velocidade maior, ative o XMP/EXPO.",
                $"Memory runs at {configured} MHz, a JEDEC default speed: if the modules are rated faster, enable XMP/EXPO."));
        if (platform.MemoryCapMhz is { } cap && rated is { } rr && rr > cap)
            issues.Add(new($"O chipset {platform.Chipset} limita a memória a {cap} MHz com este processador; pentes de {rr} MHz rodam a {cap} MHz (isso é normal).",
                $"The {platform.Chipset} chipset limits memory to {cap} MHz with this CPU; {rr} MHz modules run at {cap} MHz (this is expected)."));
        if (mixed)
            issues.Add(new("Pentes diferentes (modelo ou capacidade): o controlador usa a configuração mais lenta e o canal duplo pode ser parcial.",
                "Mismatched modules (model or capacity): the controller uses the slowest settings and dual channel may be partial."));

        var slots = modules.Select(m => SlotOf(m.Locator) ?? m.Locator).ToList();
        return new MemoryAnalysis(mode, channelEvidence, configured, rated, ratedEvidence, expected, cl, mixed, slots, issues);
    }

    /// <summary>Velocidade JEDEC baixa (sem perfil), onde ativar XMP/EXPO costuma fazer diferença.</summary>
    public static bool IsLowJedec(int mhz, MemoryType type) => type switch
    {
        MemoryType.Ddr4 => mhz <= 2400 && JedecDdr4.Contains(mhz),
        MemoryType.Ddr5 => mhz <= 4800 && JedecDdr5.Contains(mhz),
        _ => false,
    };
}
