namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>Texto nos dois idiomas do app. O motor não depende da interface; a página escolhe o idioma ao desenhar.</summary>
public sealed record LocalizedText(string Pt, string En)
{
    public string Get(bool english) => english ? En : Pt;
    public override string ToString() => Pt;
}

public enum AdvisorCategory { Cpu, Ram, Power, Latency, Pcie, Security, Virtualization, Boot, Thermal }
public enum Level { Low, Medium, High }
public enum ThermalImpact { None, Low, Medium, High }
public enum AdvisorPreset { Safe, Performance, Competitive }

/// <summary>De onde vem a informação sobre o estado atual.</summary>
public enum Evidence
{
    /// <summary>Lido diretamente do Windows (WMI, registro, driver).</summary>
    Detected,
    /// <summary>Deduzido de dados indiretos (part number, BAR1, nome do slot); pode estar errado.</summary>
    Inferred,
    /// <summary>O Windows não expõe: só dá para saber olhando na BIOS.</summary>
    NeedsBiosCheck,
    /// <summary>O usuário conferiu na BIOS e marcou como feito (vale só para a versão de BIOS atual).</summary>
    UserConfirmed,
    /// <summary>Não se aplica a este hardware (mostrado com o motivo, fora da nota).</summary>
    NotApplicable,
}

/// <summary>Se o estado atual atende à recomendação. Separado da evidência: "detectado" não quer dizer "certo".</summary>
public enum Compliance { Ok, Attention, Unknown, Info }

/// <summary>Passo a passo na BIOS da fabricante. Steps vazio = caminho não confirmado.</summary>
public sealed record BiosGuide(string Interface, IReadOnlyList<string> Steps, string? TargetValue, string? Source, IReadOnlyList<string> OptionNames, LocalizedText? Note = null)
{
    public bool HasConfirmedPath => Steps.Count > 0;
}

public sealed record AdvisorRecommendation
{
    public required string Id { get; init; }
    /// <summary>Configuração de BIOS correspondente (para achar o caminho na fabricante). Null quando não é ajuste de BIOS.</summary>
    public string? SettingId { get; init; }
    public required LocalizedText Name { get; init; }
    public required AdvisorCategory Category { get; init; }
    public required LocalizedText Description { get; init; }
    public required LocalizedText RecommendedValue { get; init; }
    public LocalizedText? CurrentValue { get; init; }
    public required Evidence Evidence { get; init; }
    public required Compliance Compliance { get; init; }
    public required LocalizedText ExpectedBenefit { get; init; }
    public required Level Gain { get; init; }
    public required Level Risk { get; init; }
    public required ThermalImpact Thermal { get; init; }
    public required LocalizedText Rollback { get; init; }
    /// <summary>Preset mínimo em que aparece (Safe aparece em todos).</summary>
    public AdvisorPreset MinPreset { get; init; } = AdvisorPreset.Safe;
    /// <summary>Preset máximo: evita duas recomendações opostas para a mesma opção (ex.: limites padrão só no Seguro).</summary>
    public AdvisorPreset MaxPreset { get; init; } = AdvisorPreset.Competitive;
    /// <summary>Peso na nota (0 = só informativo).</summary>
    public int Weight { get; init; } = 1;
    public BiosGuide? Guide { get; init; }

    public bool RequiresExtraConfirmation => Risk == Level.High;
}

public sealed record CategoryScore(AdvisorCategory Category, int? Score, int Verifiable, int Total);

public sealed record AdvisorScore(int? Overall, IReadOnlyList<CategoryScore> Categories, int Verifiable, int Total, int Inferred, int UserConfirmed);

public sealed record AdvisorReport(
    HardwareProfile Profile,
    AdvisorPreset Preset,
    IReadOnlyList<AdvisorRecommendation> Recommendations,
    AdvisorScore Score,
    bool SpecificProfileAvailable,
    string? SpecificProfileName,
    IReadOnlyList<LocalizedText> Warnings,
    PlatformInfo Platform);
