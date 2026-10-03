using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>Nível de Win32PrioritySeparation: quanto o Windows favorece o programa em foco (o jogo).</summary>
public sealed record PriorityLevel(int Value, string Label, string Description);

/// <summary>
/// Win32PrioritySeparation: define o tamanho do "quantum" de CPU e o reforço de prioridade do programa em foco.
/// O valor original é guardado na primeira mudança; Restaurar volta a ele.
/// </summary>
public sealed class Win32PriorityService
{
    private const string Key = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string Name = "Win32PrioritySeparation";
    private const string TweakId = "win32-priority";
    private readonly ActivityLog _log;
    private readonly RegistryTweakStore _tweaks;

    public Win32PriorityService(ActivityLog log, RegistryTweakStore? tweaks = null) { _log = log; _tweaks = tweaks ?? new RegistryTweakStore(); }

    public static readonly PriorityLevel[] Levels =
    {
        new(0x02, "Padrão do Windows", "Comportamento original do Windows para computadores pessoais."),
        new(0x26, "Recomendado", "Quantum curto e variável, com reforço triplo para o jogo em foco. Bom equilíbrio para jogar."),
        new(0x2A, "Resposta máxima", "Quantum curto e fixo com o maior reforço ao programa em foco. O mais agressivo."),
        new(0x28, "Rápido e estável", "Quantum curto e fixo, sem reforço extra. Tempo de resposta constante."),
        new(0x16, "Adaptável com foco", "Quantum longo e variável, com reforço triplo. Favorece o jogo sem cortar tarefas em segundo plano."),
        new(0x18, "Estável", "Quantum longo e fixo, sem reforço. O menos agressivo."),
    };

    public static int Current()
    {
        using var k = Registry.LocalMachine.OpenSubKey(Key);
        return k?.GetValue(Name) as int? ?? 2;
    }

    public bool IsChanged => _tweaks.IsApplied(TweakId);

    public void Set(PriorityLevel level)
    {
        _tweaks.Apply(TweakId, "Win32 Priority", new[] { new RegistryWrite(RegistryHive.LocalMachine, Key, Name, RegistryValueKind.DWord, level.Value) });
        _log.Write("SUCCESS", $"Win32 Priority: {level.Label} (0x{level.Value:X2})");
    }

    public void Restore()
    {
        _tweaks.Revert(TweakId, (hive, key, name) => hive == RegistryHive.LocalMachine && key.Equals(Key, StringComparison.OrdinalIgnoreCase) && name == Name);
        _log.Write("SUCCESS", "Win32 Priority restaurado ao valor original");
    }
}
