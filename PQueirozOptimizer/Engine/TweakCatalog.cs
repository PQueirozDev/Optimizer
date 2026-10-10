using Microsoft.Win32;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer.Engine;

/// <summary>Objetivo principal do PC no Smart Optimize.</summary>
public enum OptimizationGoal { CompetitiveGaming, Laptop, Development, DailyUse, Custom }

/// <summary>Estado de um ajuste neste PC, lido antes de recomendar.</summary>
public enum TweakState { Recommended, Applied, NotApplicable, Incompatible, NeedsReview, ReadFailed }

[Flags]
public enum GoalSet { None = 0, Gaming = 1, Laptop = 2, Development = 4, Daily = 8, All = Gaming | Laptop | Development | Daily }

/// <summary>Leitura do estado atual de um ajuste, com o detalhe do que foi lido.</summary>
public sealed record TweakReading(TweakState State, string Detail);

/// <summary>
/// Um ajuste do catálogo (fase 3): metadados padronizados e como ler o estado atual. O ajuste em si continua sendo
/// a etapa do script (<see cref="Operation"/> + <see cref="Step"/>), que já tem backup, ponto de restauração e reversão;
/// o catálogo só descreve e verifica. Nenhum item altera BIOS, desliga proteções do Windows ou é irreversível sem aviso.
/// </summary>
public sealed record TweakDefinition
{
    public required string Id { get; init; }
    /// <summary>Operação do script que aplica (padrao, gamer, debloat).</summary>
    public required string Operation { get; init; }
    /// <summary>Nome exato da etapa no script.</summary>
    public required string Step { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required StepRisk Risk { get; init; }
    public bool RequiresReboot { get; init; }
    /// <summary>Ação pontual (limpeza, TRIM): não fica registrada como aplicada.</summary>
    public bool OneOff { get; init; }
    public required string SideEffects { get; init; }
    public required string Verification { get; init; }
    public required string Revert { get; init; }
    /// <summary>Por que o ajuste existe e quão comprovado é o benefício. Sem números inventados.</summary>
    public required string Evidence { get; init; }
    public GoalSet Goals { get; init; }
    /// <summary>Objetivos para os quais o ajuste atrapalha, com o motivo.</summary>
    public IReadOnlyDictionary<OptimizationGoal, string> AvoidFor { get; init; } = new Dictionary<OptimizationGoal, string>();
    public required Func<TweakContext, TweakReading> Read { get; init; }

    public bool RecommendedFor(OptimizationGoal goal) => goal switch
    {
        OptimizationGoal.CompetitiveGaming => Goals.HasFlag(GoalSet.Gaming),
        OptimizationGoal.Laptop => Goals.HasFlag(GoalSet.Laptop),
        OptimizationGoal.Development => Goals.HasFlag(GoalSet.Development),
        OptimizationGoal.DailyUse => Goals.HasFlag(GoalSet.Daily),
        _ => false,
    };
}

/// <summary>Leitura do sistema compartilhada pelos ajustes (registro, serviços, apps da Loja, tarefas).</summary>
public sealed class TweakContext
{
    public required ISystemState System { get; init; }
    public bool IsDesktop { get; init; }
    public bool DualCcdX3d { get; init; }
    public bool HasAmdGpu { get; init; }
    public int WindowsBuild { get; init; }
}

/// <summary>Acesso ao estado do Windows. A implementação real lê o registro; os testes usam valores fixos.</summary>
public interface ISystemState
{
    /// <summary>Valor do registro (64 bits), ou null se não existe. Lança se não puder ler.</summary>
    object? Registry(RegistryHive hive, string key, string name);
    /// <summary>Subchaves de uma chave (vazio se não existe).</summary>
    IReadOnlyList<string> SubKeys(RegistryHive hive, string key);
    /// <summary>Tipo de início do serviço (2 automático, 3 manual, 4 desativado) ou null se o serviço não existe.</summary>
    int? ServiceStart(string name);
    Guid? ActivePowerPlan { get; }
    /// <summary>Nomes dos pacotes da Loja instalados; null se não foi possível consultar.</summary>
    IReadOnlySet<string>? AppxPackages { get; }
    /// <summary>Tarefas agendadas desativadas (caminho completo); null se não foi possível consultar.</summary>
    IReadOnlyDictionary<string, bool>? TaskEnabled { get; }
    bool OneDriveInstalled { get; }
}

public static class TweakCatalog
{
    private const RegistryHive HKLM = RegistryHive.LocalMachine, HKCU = RegistryHive.CurrentUser;
    private const string Multimedia = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";

    /// <summary>Etapas do script que ficam fora do catálogo, com o motivo (o teste de sincronia confere a lista).</summary>
    public static readonly IReadOnlyDictionary<string, string> Excluded = new Dictionary<string, string>
    {
        ["gamer|Plano de energia Qrz"] = "mesma etapa da Versão Padrão",
        ["gamer|Limpando cache DNS"] = "mesma etapa da Versão Padrão",
        ["gamer|Otimizando unidades de disco"] = "mesma ação do TRIM da Versão Padrão",
        ["gamer|Desativando experiencias personalizadas com dados de diagnostico"] = "mesma etapa do Debloat",
        ["gamer|Ajustando efeitos visuais p/ desempenho"] = "mesma ação do Debloat",
        ["debloat|Arquivos temporarios removidos"] = "a limpeza de arquivos é feita pela Limpeza rápida, com análise antes",
    };

    // ---------- Leituras ----------
    private static TweakReading Applied(string detail) => new(TweakState.Applied, detail);
    private static TweakReading Pending(string detail) => new(TweakState.Recommended, detail);

    private static int? Dword(TweakContext c, RegistryHive hive, string key, string name) => c.System.Registry(hive, key, name) switch
    {
        int i => i,
        long l => (int)l,
        string s when int.TryParse(s, out var p) => p,
        _ => null,
    };

    /// <summary>Todos os valores DWORD conferem com o esperado (ausente conta como diferente).</summary>
    private static Func<TweakContext, TweakReading> AllDword(string appliedText, string pendingText, params (RegistryHive Hive, string Key, string Name, int Value)[] values) => c =>
    {
        var missing = values.Where(v => Dword(c, v.Hive, v.Key, v.Name) != v.Value).Select(v => v.Name).ToList();
        return missing.Count == 0 ? Applied(appliedText) : Pending(pendingText + (missing.Count < values.Length ? $" (faltam: {string.Join(", ", missing)})" : ""));
    };

    private static Func<TweakContext, TweakReading> ServicesDisabled(params string[] names) => c =>
    {
        var existing = names.Select(n => (Name: n, Start: c.System.ServiceStart(n))).Where(s => s.Start != null).ToList();
        if (existing.Count == 0) return new(TweakState.NotApplicable, "Os serviços não existem neste Windows.");
        var active = existing.Where(s => s.Start != 4).Select(s => s.Name).ToList();
        return active.Count == 0 ? Applied("Serviços já desativados.") : Pending("Ativos: " + string.Join(", ", active));
    };

    private static Func<TweakContext, TweakReading> AppxAbsent(string appliedText, params string[] patterns) => c =>
    {
        if (c.System.AppxPackages is not { } packages) return new(TweakState.ReadFailed, "Não foi possível consultar os apps da Loja.");
        var found = packages.Where(p => patterns.Any(pattern => Like(p, pattern))).ToList();
        return found.Count == 0 ? Applied(appliedText) : Pending($"Instalado: {string.Join(", ", found.Take(6))}" + (found.Count > 6 ? $" e mais {found.Count - 6}" : ""));
    };

    /// <summary>Mesmo curinga do -like do PowerShell, só com '*'.</summary>
    internal static bool Like(string value, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(value, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static readonly string[] CeipTasks =
    {
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask",
        @"\Microsoft\Windows\Feedback\Siuf\DmClient",
        @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
    };

    public static readonly string[] BloatAppPatterns =
    {
        "*BingNews*", "*BingWeather*", "*GetHelp*", "*Getstarted*", "*MicrosoftOfficeHub*", "*MicrosoftSolitaireCollection*", "Microsoft.People",
        "*PowerAutomateDesktop*", "*Todos*", "*YourPhone*", "*MicrosoftTeams*", "*SkypeApp*", "*MixedReality*", "*WindowsMaps*",
    };

    private static readonly IReadOnlyDictionary<OptimizationGoal, string> NoGoalConflicts = new Dictionary<OptimizationGoal, string>();

    public static IReadOnlyList<TweakDefinition> All { get; } = Build();

    public static TweakDefinition? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    private static List<TweakDefinition> Build() => new()
    {
        // ================= Energia =================
        new()
        {
            Id = "power.qrz", Operation = "padrao", Step = "Plano de energia Qrz", Name = "Plano de energia Qrz",
            Description = "Plano de baixa latência baseado no Equilibrado, com estacionamento de núcleos e estados de economia reduzidos.",
            Category = "Energia", Risk = StepRisk.Moderate,
            SideEffects = "Mais consumo e calor em repouso. Em notebooks reduz a bateria (por isso só aparece em desktops).",
            Verification = "O plano ativo passa a ser o Qrz (powercfg /getactivescheme).",
            Revert = "Volta ao plano anterior pela reversão ou pela página Modo Jogo.",
            Evidence = "Reduz a latência de troca de estados de energia da CPU. O ganho em FPS varia por jogo e processador; meça no Optimization Lab.",
            Goals = GoalSet.Gaming,
            AvoidFor = new Dictionary<OptimizationGoal, string> { [OptimizationGoal.Laptop] = "Aumenta o consumo e reduz a autonomia.", [OptimizationGoal.DailyUse] = "Consome mais energia sem ganho perceptível no uso comum." },
            Read = c =>
            {
                if (c.DualCcdX3d) return new(TweakState.Incompatible, "Ryzen X3D com dois CCDs: o Equilibrado é necessário para o jogo usar o CCD com 3D V-Cache.");
                if (!c.IsDesktop) return new(TweakState.NotApplicable, "Notebook: o plano atual é mantido para preservar a bateria.");
                if (c.System.ActivePowerPlan == PowerPlanService.QrzGuid) return Applied("O plano Qrz está ativo.");
                return c.System.ActivePowerPlan is { } plan && !KnownPlans.Contains(plan)
                    ? new(TweakState.NeedsReview, "Outro plano personalizado está ativo (de outro programa ou do fabricante). Confira antes de trocar.")
                    : Pending("Plano padrão do Windows ativo.");
            },
        },
        new()
        {
            Id = "power.hibernate", Operation = "padrao", Step = "Desativando a hibernacao (libera o espaco do hiberfil.sys)", Name = "Desativar a hibernação",
            Description = "Apaga o hiberfil.sys, que pode ocupar até 40% da RAM no disco.", Category = "Energia", Risk = StepRisk.Moderate,
            SideEffects = "A opção Hibernar some e a inicialização rápida deixa de funcionar.", Verification = "HibernateEnabled = 0 em HKLM\\SYSTEM\\CurrentControlSet\\Control\\Power.",
            Revert = "A reversão religa com powercfg /h on.", Evidence = "Ganho de espaço em disco, documentado pela Microsoft (powercfg /hibernate). Não muda desempenho.",
            Goals = GoalSet.Gaming | GoalSet.Daily, AvoidFor = new Dictionary<OptimizationGoal, string> { [OptimizationGoal.Laptop] = "Notebooks usam a hibernação quando a bateria acaba." },
            Read = c => !c.IsDesktop ? new(TweakState.NotApplicable, "Notebook: a hibernação é mantida.")
                : Dword(c, HKLM, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") == 0 ? Applied("A hibernação já está desligada.") : Pending("A hibernação está ligada."),
        },
        new()
        {
            Id = "power.throttling", Operation = "gamer", Step = "Desativando limitacao de energia de processos (Power Throttling)", Name = "Desligar o Power Throttling",
            Description = "Impede o Windows de reduzir a energia de programas em segundo plano.", Category = "Energia", Risk = StepRisk.Moderate,
            SideEffects = "Programas em segundo plano consomem mais energia.", Verification = "PowerThrottlingOff = 1 em HKLM\\...\\Control\\Power\\PowerThrottling.",
            Revert = "Remova o valor pela reversão.", Evidence = "Afeta principalmente processos em segundo plano; o jogo em foco já não é limitado. Benefício pequeno e variável.",
            Goals = GoalSet.None, AvoidFor = new Dictionary<OptimizationGoal, string> { [OptimizationGoal.Laptop] = "Reduz a autonomia da bateria." },
            Read = c => !c.IsDesktop ? new(TweakState.NotApplicable, "Notebook: mantido para preservar a bateria.")
                : Dword(c, HKLM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff") == 1 ? Applied("Já desligado.") : Pending("Ligado (padrão do Windows)."),
        },

        // ================= Jogos =================
        new()
        {
            Id = "game.mode", Operation = "gamer", Step = "Ativando Modo de Jogo do Windows", Name = "Modo de Jogo do Windows",
            Description = "Liga o Modo de Jogo nativo, que prioriza o jogo e adia atualizações durante a partida.", Category = "Jogos", Risk = StepRisk.Safe,
            SideEffects = "Nenhum conhecido.", Verification = "AutoGameModeEnabled = 1 (ou ausente, que é o padrão ligado).",
            Revert = "Desligue em Configurações → Jogos → Modo de Jogo.", Evidence = "Recurso oficial do Windows 10/11.",
            Goals = GoalSet.Gaming, Read = c => Dword(c, HKCU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") is null or 1 ? Applied("O Modo de Jogo está ligado.") : Pending("O Modo de Jogo está desligado."),
        },
        new()
        {
            Id = "game.dvr", Operation = "gamer", Step = "Desativando gravacao de jogos em segundo plano", Name = "Desligar a gravação em segundo plano",
            Description = "Desliga a captura contínua da Game Bar (os últimos minutos de jogo ficam sempre sendo gravados).", Category = "Jogos", Risk = StepRisk.Moderate,
            SideEffects = "Os atalhos de gravação da Game Bar param de funcionar.", Verification = "AppCaptureEnabled = 0 e GameDVR_Enabled = 0.",
            Revert = "Religue pela reversão ou em Configurações → Jogos → Capturas.", Evidence = "A gravação contínua usa o codificador de vídeo da GPU o tempo todo. Desligar libera esse trabalho.",
            Goals = GoalSet.Gaming | GoalSet.Laptop,
            Read = c => c.DualCcdX3d ? new(TweakState.Incompatible, "Ryzen X3D com dois CCDs: a Game Bar é usada pelo Windows para levar o jogo ao CCD com 3D V-Cache.")
                : AllDword("A gravação em segundo plano já está desligada.", "A gravação em segundo plano está ligada.", (HKCU, GameDvr, "AppCaptureEnabled", 0), (HKCU, @"System\GameConfigStore", "GameDVR_Enabled", 0))(c),
        },
        new()
        {
            Id = "gpu.hags", Operation = "gamer", Step = "Habilitando GPU Scheduling por hardware", Name = "Agendamento de GPU por hardware (HAGS)",
            Description = "Deixa a própria placa de vídeo gerenciar a fila de trabalho.", Category = "Jogos", Risk = StepRisk.Moderate, RequiresReboot = true,
            SideEffects = "Em alguns drivers antigos causa travadas; se notar piora, reverta.", Verification = "HwSchMode = 2 em HKLM\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers.",
            Revert = "Volta ao valor anterior pela reversão (vale após reiniciar).", Evidence = "Necessário para o DLSS Frame Generation. Fora disso o efeito em FPS é pequeno e varia por driver.",
            Goals = GoalSet.Gaming,
            Read = c => Dword(c, HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") == 2 ? Applied("O HAGS já está ligado.") : Pending("O HAGS está desligado. O script confere se a GPU suporta antes de aplicar."),
        },
        new()
        {
            Id = "gpu.windowed", Operation = "gamer", Step = "Ativando otimizacoes para jogos em janela", Name = "Otimizações para jogos em janela",
            Description = "Usa o modelo de apresentação moderno em jogos DirectX 10/11 em janela ou sem borda.", Category = "Jogos", Risk = StepRisk.Moderate,
            SideEffects = "Raros problemas com sobreposições antigas.", Verification = "DirectXUserGlobalSettings contém SwapEffectUpgradeEnable=1.",
            Revert = "Volta ao valor anterior pela reversão.", Evidence = "Recurso oficial do Windows 11 que reduz a latência de apresentação em janela.",
            Goals = GoalSet.Gaming,
            Read = c => c.WindowsBuild < 22000 ? new(TweakState.NotApplicable, "Disponível apenas no Windows 11.")
                : (c.System.Registry(HKCU, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings") as string ?? "").Contains("SwapEffectUpgradeEnable=1", StringComparison.OrdinalIgnoreCase)
                    ? Applied("Já ligado.") : Pending("Desligado."),
        },
        new()
        {
            Id = "gpu.ulps", Operation = "gamer", Step = "Desativando o ULPS da placa de video AMD", Name = "Desligar o ULPS (AMD)",
            Description = "Impede a placa AMD de entrar em repouso profundo, que causa engasgos e telas pretas em alguns PCs.", Category = "Jogos", Risk = StepRisk.Moderate, RequiresReboot = true,
            SideEffects = "Um pouco mais de consumo em repouso.", Verification = "EnableUlps = 0 nas chaves da placa AMD.",
            Revert = "Volta ao valor anterior pela reversão.", Evidence = "Solução conhecida para telas pretas e travadas ao sair do repouso em placas AMD. Sem efeito em FPS.",
            Goals = GoalSet.None,
            Read = c =>
            {
                if (!c.HasAmdGpu) return new(TweakState.NotApplicable, "Sem placa de vídeo AMD.");
                const string cls = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                var values = c.System.SubKeys(HKLM, cls).Where(k => k.Length == 4 && k.All(char.IsDigit))
                    .Select(k => Dword(c, HKLM, cls + "\\" + k, "EnableUlps")).Where(v => v != null).ToList();
                return values.Count == 0 ? new(TweakState.NotApplicable, "O driver não usa ULPS.") : values.All(v => v == 0) ? Applied("Já desligado.") : Pending("Ligado.");
            },
        },
        new()
        {
            Id = "cpu.priority", Operation = "gamer", Step = "Priorizando CPU para o jogo em foco", Name = "Prioridade para o programa em foco",
            Description = "Garante que o Windows dê mais tempo de CPU ao programa em primeiro plano.", Category = "Jogos", Risk = StepRisk.Safe,
            SideEffects = "Nenhum: corresponde ao padrão do Windows cliente.", Verification = "Win32PrioritySeparation = 2 ou 38.",
            Revert = "Volta ao valor anterior pela reversão.", Evidence = "O padrão do Windows (2) já equivale a isso; o ajuste só corrige PCs alterados para \"serviços em segundo plano\".",
            Goals = GoalSet.Gaming | GoalSet.Daily,
            Read = c => Dword(c, HKLM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation") is null or 2 or 38 ? Applied("O Windows já prioriza o programa em foco.") : Pending("Configurado para priorizar serviços em segundo plano."),
        },
        new()
        {
            Id = "mm.responsiveness", Operation = "gamer", Step = "Reduzindo latencia de rede/multimidia", Name = "Prioridade multimídia para jogos",
            Description = "Reserva menos CPU para tarefas de fundo do agendador multimídia e sobe a prioridade da categoria Games.", Category = "Jogos", Risk = StepRisk.Moderate, RequiresReboot = true,
            SideEffects = "Tarefas de fundo (downloads, gravação) podem ficar um pouco mais lentas durante o jogo.", Verification = "SystemResponsiveness = 10 e GPU Priority = 8, Priority = 6 em Tasks\\Games.",
            Revert = "Volta aos valores anteriores pela reversão.", Evidence = "Documentado pela Microsoft (MMCSS). O efeito em jogos modernos é pequeno; meça antes de manter.",
            Goals = GoalSet.Gaming,
            Read = AllDword("Já configurado.", "Valores padrão do Windows.", (HKLM, Multimedia, "SystemResponsiveness", 10), (HKLM, Multimedia + @"\Tasks\Games", "GPU Priority", 8), (HKLM, Multimedia + @"\Tasks\Games", "Priority", 6)),
        },
        new()
        {
            Id = "net.throttling", Operation = "gamer", Step = "Desativando limitacao de rede (Throttling)", Name = "Desligar o limite de rede do MMCSS",
            Description = "Remove o limite de pacotes que o Windows aplica enquanto toca áudio ou vídeo.", Category = "Rede", Risk = StepRisk.Moderate, RequiresReboot = true,
            SideEffects = "Nenhum relevante em PCs modernos.", Verification = "NetworkThrottlingIndex = 0xFFFFFFFF.",
            Revert = "Volta ao valor anterior pela reversão.", Evidence = "O limite foi criado para PCs antigos; em conexões rápidas o efeito é pequeno. Benefício não comprovado em todos os casos.",
            Goals = GoalSet.Gaming,
            Read = c => Dword(c, HKLM, Multimedia, "NetworkThrottlingIndex") == -1 ? Applied("Já desligado.") : Pending("Limite padrão do Windows."),
        },
        new()
        {
            Id = "sys.mmcss", Operation = "gamer", Step = "Verificando agendador multimidia do Windows (MMCSS)", Name = "Reparar o agendador multimídia (MMCSS)",
            Description = "Restaura valores padrão do MMCSS que outros otimizadores costumam apagar.", Category = "Jogos", Risk = StepRisk.Safe,
            SideEffects = "Nenhum: só recria valores padrão que faltam.", Verification = "SystemResponsiveness, NetworkThrottlingIndex e Tasks\\Games existem.",
            Revert = "Os valores recriados ficam no backup.", Evidence = "Sem esses valores o MMCSS pode não priorizar áudio e jogos.",
            Goals = GoalSet.Gaming,
            Read = c => c.System.Registry(HKLM, Multimedia, "SystemResponsiveness") != null && c.System.Registry(HKLM, Multimedia, "NetworkThrottlingIndex") != null && c.System.Registry(HKLM, Multimedia + @"\Tasks\Games", "GPU Priority") != null
                ? Applied("Os valores do MMCSS estão presentes.") : Pending("Faltam valores do MMCSS."),
        },
        new()
        {
            Id = "input.mouseaccel", Operation = "gamer", Step = "Desativando aceleracao do ponteiro do mouse", Name = "Desligar a aceleração do mouse",
            Description = "Desliga a \"precisão aprimorada do ponteiro\": o cursor anda sempre a mesma distância para o mesmo movimento.", Category = "Jogos", Risk = StepRisk.Moderate,
            SideEffects = "Muda a sensação do mouse no Windows; pode ser preciso ajustar a sensibilidade.", Verification = "MouseSpeed = 0 em HKCU\\Control Panel\\Mouse.",
            Revert = "Volta aos valores anteriores pela reversão.", Evidence = "Movimento consistente é preferido em jogos de mira. Jogos com entrada direta (raw input) já ignoram a aceleração.",
            Goals = GoalSet.Gaming,
            Read = c => (c.System.Registry(HKCU, @"Control Panel\Mouse", "MouseSpeed")?.ToString()) == "0" ? Applied("A aceleração já está desligada.") : Pending("A aceleração está ligada."),
        },

        // ================= Interface =================
        new()
        {
            Id = "ui.visualfx", Operation = "debloat", Step = "Efeitos visuais ajustados", Name = "Efeitos visuais para desempenho",
            Description = "Desliga animações de janelas, menus e da barra de tarefas, e a transparência.", Category = "Interface", Risk = StepRisk.Safe,
            SideEffects = "O Windows fica sem animações e sem transparência.", Verification = "VisualFXSetting = 3 e TaskbarAnimations = 0.",
            Revert = "Volta aos valores anteriores pela reversão.", Evidence = "Ajuda em PCs com GPU integrada ou pouca memória; em PCs potentes a diferença é pequena.",
            Goals = GoalSet.Laptop,
            AvoidFor = new Dictionary<OptimizationGoal, string> { [OptimizationGoal.DailyUse] = "Deixa o Windows com aparência mais simples sem ganho perceptível em PCs atuais." },
            Read = AllDword("Já ajustados.", "Efeitos padrão do Windows.", (HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3), (HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0)),
        },

        // ================= Sistema =================
        new()
        {
            Id = "boot.faststartup", Operation = "padrao", Step = "Desativando a inicializacao rapida (Fast Startup)", Name = "Desligar a inicialização rápida",
            Description = "Com ela ligada, \"Desligar\" só hiberna o kernel: drivers e atualizações não reiniciam de verdade.", Category = "Sistema", Risk = StepRisk.Safe,
            SideEffects = "A inicialização a frio pode levar alguns segundos a mais.", Verification = "HiberbootEnabled = 0.",
            Revert = "Volta ao valor anterior pela reversão.", Evidence = "Recomendado pela Microsoft para resolver problemas de drivers e dual boot.",
            Goals = GoalSet.Gaming | GoalSet.Development | GoalSet.Daily,
            AvoidFor = new Dictionary<OptimizationGoal, string> { [OptimizationGoal.Laptop] = "Em notebooks a inicialização rápida economiza tempo e bateria ao ligar." },
            Read = c => Dword(c, HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled") == 0 ? Applied("Já desligada.") : Pending("Ligada."),
        },
        new()
        {
            Id = "net.deliveryopt", Operation = "padrao", Step = "Limitando o upload de atualizacoes a rede local (Delivery Optimization)", Name = "Atualizações só com a rede local",
            Description = "O Windows deixa de enviar atualizações para outros PCs pela internet.", Category = "Rede", Risk = StepRisk.Safe,
            SideEffects = "Nenhum: o download das atualizações continua igual.", Verification = "DODownloadMode = 1 (ou mais restrito).",
            Revert = "Volta ao valor anterior pela reversão.", Evidence = "Opção oficial da Otimização de Entrega. Evita uso de upload em segundo plano.",
            Goals = GoalSet.All,
            Read = c => Dword(c, HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode") is 0 or 1 or 99 or 100 ? Applied("O envio pela internet já está bloqueado.") : Pending("Padrão do Windows."),
        },
        new()
        {
            Id = "sys.remoteassist", Operation = "debloat", Step = "Assistencia Remota desativada", Name = "Desligar a Assistência Remota",
            Description = "Impede convites de Assistência Remota para este PC.", Category = "Sistema", Risk = StepRisk.Safe,
            SideEffects = "Ninguém consegue ajudar por Assistência Remota (Quick Assist continua funcionando).", Verification = "fAllowToGetHelp = 0.",
            Revert = "Volta ao valor anterior pela reversão.", Evidence = "Reduz a superfície de ataque; não muda desempenho.", Goals = GoalSet.Daily | GoalSet.Gaming,
            Read = c => Dword(c, HKLM, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp") == 0 ? Applied("Já desligada.") : Pending("Ligada."),
        },

        // ================= Serviços =================
        new()
        {
            Id = "svc.telemetry", Operation = "debloat", Step = "Telemetria e diagnostico desativados", Name = "Serviços de telemetria e relatórios",
            Description = "Desativa DiagTrack, dmwappushservice, o coletor de diagnóstico, o Relatório de Erros e o Assistente de Compatibilidade.", Category = "Serviços", Risk = StepRisk.Moderate,
            SideEffects = "Sem relatórios de erro para a Microsoft e sem avisos de compatibilidade de programas antigos.", Verification = "Os serviços ficam com início Desativado.",
            Revert = "A reversão devolve o tipo de início original de cada serviço.", Evidence = "Reduz atividade em segundo plano e envio de dados. Ganho de desempenho pequeno.",
            Goals = GoalSet.Gaming | GoalSet.Daily | GoalSet.Laptop,
            AvoidFor = new Dictionary<OptimizationGoal, string> { [OptimizationGoal.Development] = "O Relatório de Erros e os dumps ajudam a depurar programas que travam." },
            Read = ServicesDisabled("DiagTrack", "dmwappushservice", "diagnosticshub.standardcollector.service", "WerSvc", "PcaSvc"),
        },
        new()
        {
            Id = "svc.fax", Operation = "debloat", Step = "Servico de Fax desativado", Name = "Desativar o serviço de Fax", Description = "Fax do Windows.", Category = "Serviços", Risk = StepRisk.Safe,
            SideEffects = "Envio de fax pelo Windows para de funcionar.", Verification = "Fax com início Desativado.", Revert = "A reversão devolve o tipo de início.",
            Evidence = "Serviço raramente usado; desativar não muda desempenho de forma mensurável.", Goals = GoalSet.Daily | GoalSet.Gaming, Read = ServicesDisabled("Fax"),
        },
        new()
        {
            Id = "svc.remoteregistry", Operation = "debloat", Step = "Remote Registry desativado", Name = "Desativar o Registro Remoto", Description = "Edição do registro deste PC pela rede.", Category = "Serviços", Risk = StepRisk.Safe,
            SideEffects = "Ferramentas de administração remota de empresas podem depender dele.", Verification = "RemoteRegistry com início Desativado.", Revert = "A reversão devolve o tipo de início.",
            Evidence = "Já vem desativado no Windows 10/11; reduz a superfície de ataque.", Goals = GoalSet.All, Read = ServicesDisabled("RemoteRegistry"),
        },
        new()
        {
            Id = "svc.maps", Operation = "debloat", Step = "Servico de Mapas desativado", Name = "Desativar o serviço de Mapas", Description = "Download de mapas offline.", Category = "Serviços", Risk = StepRisk.Safe,
            SideEffects = "Mapas offline deixam de ser atualizados.", Verification = "MapsBroker com início Desativado.", Revert = "A reversão devolve o tipo de início.",
            Evidence = "Serviço de uso raro; desativar não muda desempenho de forma mensurável.", Goals = GoalSet.Daily | GoalSet.Gaming, Read = ServicesDisabled("MapsBroker"),
        },
        new()
        {
            Id = "tasks.ceip", Operation = "debloat", Step = "Desativando tarefas opcionais de CEIP e feedback", Name = "Tarefas de CEIP e feedback",
            Description = "Desativa tarefas agendadas do Programa de Aperfeiçoamento e de pesquisas de feedback.", Category = "Privacidade", Risk = StepRisk.Safe,
            SideEffects = "Nenhum.", Verification = "As cinco tarefas ficam desativadas.", Revert = "A reversão reativa as tarefas.",
            Evidence = "Tarefas opcionais de coleta de dados.", Goals = GoalSet.All,
            Read = c =>
            {
                if (c.System.TaskEnabled is not { } tasks) return new(TweakState.ReadFailed, "Não foi possível consultar as tarefas agendadas.");
                var present = CeipTasks.Where(tasks.ContainsKey).ToList();
                if (present.Count == 0) return new(TweakState.NotApplicable, "As tarefas não existem neste Windows.");
                var enabled = present.Count(t => tasks[t]);
                return enabled == 0 ? Applied("As tarefas já estão desativadas.") : Pending($"{enabled} tarefa(s) ativa(s).");
            },
        },

        // ================= Privacidade =================
        new()
        {
            Id = "privacy.suggestions", Operation = "padrao", Step = "Desativando sugestoes, anuncios e apps instalados automaticamente", Name = "Sem anúncios e apps instalados sozinhos",
            Description = "Impede o Windows de instalar jogos promocionais e mostrar sugestões no Iniciar e no Explorador.", Category = "Privacidade", Risk = StepRisk.Safe,
            SideEffects = "Nenhum.", Verification = "SilentInstalledAppsEnabled e SystemPaneSuggestionsEnabled = 0.", Revert = "Volta aos valores anteriores pela reversão.",
            Evidence = "Configurações oficiais do Gerenciador de Conteúdo do Windows.", Goals = GoalSet.All,
            Read = AllDword("Já desligados.", "Sugestões e instalações automáticas ligadas.", (HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0), (HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0)),
        },
        new()
        {
            Id = "privacy.tailored", Operation = "debloat", Step = "Desativando experiencias personalizadas com dados de diagnostico", Name = "Sem experiências personalizadas",
            Description = "A Microsoft deixa de usar dados de diagnóstico para dicas e anúncios personalizados.", Category = "Privacidade", Risk = StepRisk.Safe,
            SideEffects = "Nenhum.", Verification = "TailoredExperiencesWithDiagnosticDataEnabled = 0.", Revert = "Volta ao valor anterior pela reversão.",
            Evidence = "Opção oficial de privacidade do Windows.", Goals = GoalSet.All,
            Read = c => Dword(c, HKCU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled") == 0 ? Applied("Já desligado.") : Pending("Ligado."),
        },
        new()
        {
            Id = "privacy.adid", Operation = "debloat", Step = "ID de publicidade desativado", Name = "Desligar o ID de publicidade",
            Description = "Apps deixam de usar um identificador para anúncios personalizados.", Category = "Privacidade", Risk = StepRisk.Safe,
            SideEffects = "Nenhum.", Verification = "AdvertisingInfo\\Enabled = 0.", Revert = "Volta ao valor anterior pela reversão.", Evidence = "Opção oficial de privacidade do Windows.", Goals = GoalSet.All,
            Read = c => Dword(c, HKCU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled") == 0 ? Applied("Já desligado.") : Pending("Ligado."),
        },
        new()
        {
            Id = "privacy.websearch", Operation = "debloat", Step = "Pesquisa na web removida do menu Iniciar", Name = "Pesquisa do Iniciar sem resultados da web",
            Description = "A pesquisa do menu Iniciar mostra só o que está no PC.", Category = "Privacidade", Risk = StepRisk.Safe,
            SideEffects = "Sem sugestões do Bing na pesquisa.", Verification = "DisableSearchBoxSuggestions = 1.", Revert = "Volta ao valor anterior pela reversão.",
            Evidence = "Política oficial do Windows; deixa a pesquisa local mais rápida e sem envio do que você digita.", Goals = GoalSet.All,
            Read = c => Dword(c, HKCU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions") == 1 ? Applied("Já desligado.") : Pending("Resultados da web ligados."),
        },
        new()
        {
            Id = "privacy.activity", Operation = "debloat", Step = "Historico de atividades desativado", Name = "Desligar o histórico de atividades",
            Description = "O Windows deixa de registrar e enviar os apps e arquivos abertos.", Category = "Privacidade", Risk = StepRisk.Safe,
            SideEffects = "Sem continuar atividades entre dispositivos.", Verification = "EnableActivityFeed = 0 e PublishUserActivities = 0.", Revert = "Volta aos valores anteriores pela reversão.",
            Evidence = "Política oficial do Windows.", Goals = GoalSet.All,
            Read = AllDword("Já desligado.", "Ligado.", (HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0), (HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0)),
        },
        new()
        {
            Id = "privacy.policies", Operation = "gamer", Step = "Aplicando politicas do Editor de Politica de Grupo (diagnostico, nuvem, IA e Push)", Name = "Políticas de diagnóstico, nuvem e IA",
            Description = "Conjunto de políticas: telemetria no mínimo, sem conteúdo de consumidor, sem inventário, sem análise de IA e sem instalação remota (Push).", Category = "Privacidade", Risk = StepRisk.Moderate,
            SideEffects = "Sugestões da nuvem e alguns recursos de IA do Windows ficam indisponíveis.", Verification = "AllowTelemetry = 0, DisableWindowsConsumerFeatures = 1 e DisablePushToInstall = 1.",
            Revert = "A reversão remove as políticas.", Evidence = "Políticas oficiais. No Windows Home e Pro, AllowTelemetry = 0 é tratado como 1 (só obrigatório).",
            Goals = GoalSet.Gaming | GoalSet.Daily,
            Read = AllDword("Já aplicadas.", "Políticas não aplicadas.", (HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0), (HKLM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1), (HKLM, @"SOFTWARE\Policies\Microsoft\PushToInstall", "DisablePushToInstall", 1)),
        },
        new()
        {
            Id = "privacy.diagrequired", Operation = "debloat", Step = "Limitando dados de diagnostico ao nivel obrigatorio", Name = "Só dados de diagnóstico obrigatórios",
            Description = "Limita a telemetria do Windows ao nível obrigatório.", Category = "Privacidade", Risk = StepRisk.Moderate,
            SideEffects = "O Programa Windows Insider exige dados opcionais.", Verification = "AllowTelemetry = 1 (ou 0, mais restrito).", Revert = "A reversão remove a política.",
            Evidence = "Política oficial do Windows.", Goals = GoalSet.Daily | GoalSet.Laptop | GoalSet.Development,
            Read = c => Dword(c, HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") is 0 or 1 ? Applied("A telemetria já está no nível obrigatório ou abaixo.") : Pending("Dados opcionais permitidos."),
        },

        // ================= IA do Windows =================
        new()
        {
            Id = "ai.copilot", Operation = "debloat", Step = "Desativando Copilot integrado do Windows", Name = "Desligar o Copilot integrado",
            Description = "Desliga o Copilot embutido do Windows 11 (antes do 24H2) e o botão da barra de tarefas.", Category = "IA do Windows", Risk = StepRisk.Moderate,
            SideEffects = "O Copilot integrado deixa de abrir pelo atalho.", Verification = "TurnOffWindowsCopilot = 1.", Revert = "A reversão remove a política.",
            Evidence = "Política oficial do Windows. No 24H2 o Copilot virou um app comum e esta política não se aplica.", Goals = GoalSet.Daily | GoalSet.Gaming,
            Read = c => Dword(c, HKCU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot") == 1 ? Applied("Já desligado.") : Pending("Ligado."),
        },
        new()
        {
            Id = "ai.recall", Operation = "debloat", Step = "Recall desativado", Name = "Desligar o Recall",
            Description = "Impede o Recall de tirar capturas periódicas da tela para a busca com IA.", Category = "IA do Windows", Risk = StepRisk.Safe,
            SideEffects = "Sem a linha do tempo de capturas.", Verification = "AllowRecallEnablement = 0.", Revert = "A reversão remove a política.",
            Evidence = "Política oficial (Windows 11 24H2 em PCs Copilot+).", Goals = GoalSet.All,
            Read = c => Dword(c, HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement") == 0 ? Applied("Já desligado.") : Pending("Permitido."),
        },
        new()
        {
            Id = "ai.clicktodo", Operation = "debloat", Step = "Click To Do desativado", Name = "Desligar o Click To Do",
            Description = "Desliga a sobreposição de IA que analisa o que está na tela.", Category = "IA do Windows", Risk = StepRisk.Safe,
            SideEffects = "Sem o Click To Do.", Verification = "DisableClickToDo = 1.", Revert = "A reversão remove a política.", Evidence = "Política oficial do Windows 11 24H2.", Goals = GoalSet.All,
            Read = c => Dword(c, HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo") == 1 ? Applied("Já desligado.") : Pending("Ligado."),
        },

        // ================= Apps =================
        new()
        {
            Id = "app.bloat", Operation = "debloat", Step = "Apps desnecessarios removidos", Name = "Remover apps pré-instalados",
            Description = "Remove Notícias, Clima, Solitaire, Teams pessoal, Skype, Vincular ao Celular, Mapas e outros.", Category = "Apps", Risk = StepRisk.Moderate,
            SideEffects = "Os apps somem para todos os usuários. A reversão não reinstala: use a Microsoft Store.", Verification = "Nenhum dos pacotes da lista continua instalado.",
            Revert = "Não reversível pelo backup; reinstale pela Microsoft Store.", Evidence = "Libera espaço e tarefas em segundo plano desses apps.", Goals = GoalSet.Gaming,
            AvoidFor = new Dictionary<OptimizationGoal, string> { [OptimizationGoal.Development] = "Remove o Teams e o Vincular ao Celular, usados em trabalho.", [OptimizationGoal.DailyUse] = "Pode remover apps que você usa (Teams, Clima, Celular)." },
            Read = AppxAbsent("Nenhum app da lista está instalado.", BloatAppPatterns),
        },
        new()
        {
            Id = "app.cortana", Operation = "debloat", Step = "Cortana removida", Name = "Remover a Cortana", Description = "Remove o app Cortana.", Category = "Apps", Risk = StepRisk.Moderate,
            SideEffects = "Não reversível pelo backup; reinstale pela Microsoft Store.", Verification = "O pacote Microsoft.549981C3F5F10 não está instalado.", Revert = "Reinstale pela Microsoft Store.",
            Evidence = "A Cortana foi descontinuada pela Microsoft.", Goals = GoalSet.Gaming | GoalSet.Daily, Read = AppxAbsent("A Cortana não está instalada.", "*Microsoft.549981C3F5F10*"),
        },
        new()
        {
            Id = "app.widgets", Operation = "debloat", Step = "Widgets removidos", Name = "Remover os Widgets", Description = "Remove o painel de Widgets (Web Experience).", Category = "Apps", Risk = StepRisk.Moderate,
            SideEffects = "O painel de Widgets e o clima da barra de tarefas somem. Não reversível pelo backup.", Verification = "O pacote WebExperience não está instalado.", Revert = "Reinstale pela Microsoft Store.",
            Evidence = "O painel carrega conteúdo da web em segundo plano.", Goals = GoalSet.Gaming,
            AvoidFor = new Dictionary<OptimizationGoal, string> { [OptimizationGoal.DailyUse] = "Remove o clima e as notícias da barra de tarefas." },
            Read = AppxAbsent("Os Widgets não estão instalados.", "*WebExperience*", "*WindowsWidgets*"),
        },
        new()
        {
            Id = "app.onedrive", Operation = "debloat", Step = "OneDrive removido", Name = "Remover o OneDrive", Description = "Desinstala o OneDrive.", Category = "Apps", Risk = StepRisk.Moderate,
            SideEffects = "Arquivos deixam de sincronizar com a nuvem. Confira se tudo está salvo localmente antes.", Verification = "O OneDrive não está mais instalado.",
            Revert = "No Windows 10 a reversão reinstala; no 11, baixe no site da Microsoft.", Evidence = "Remove a sincronização em segundo plano. Só faça se não usar o OneDrive.",
            Goals = GoalSet.None,
            AvoidFor = new Dictionary<OptimizationGoal, string> { [OptimizationGoal.Development] = "Muitos projetos e documentos ficam na pasta sincronizada.", [OptimizationGoal.DailyUse] = "Você pode depender da sincronização de fotos e documentos." },
            Read = c => c.System.OneDriveInstalled ? new(TweakState.NeedsReview, "Instalado. Confira se você usa a sincronização antes de remover.") : Applied("O OneDrive não está instalado."),
        },

        // ================= Manutenção (ações pontuais) =================
        new()
        {
            Id = "maint.trim", Operation = "padrao", Step = "Otimizando/TRIM das unidades de disco", Name = "TRIM / otimização das unidades", OneOff = true,
            Description = "Envia o TRIM aos SSDs e desfragmenta HDDs.", Category = "Manutenção", Risk = StepRisk.Safe, SideEffects = "Nenhum; pode levar alguns minutos em HDDs.",
            Verification = "Ação pontual: o Windows registra a última otimização em Otimizar Unidades.", Revert = "Não altera configurações.", Evidence = "Mesma tarefa que o Windows agenda semanalmente.",
            Goals = GoalSet.Daily, Read = _ => Pending("Ação pontual."),
        },
        new()
        {
            Id = "maint.dns", Operation = "padrao", Step = "Limpando cache DNS", Name = "Limpar o cache DNS", OneOff = true,
            Description = "Apaga endereços de sites guardados em cache.", Category = "Manutenção", Risk = StepRisk.Safe, SideEffects = "Nenhum.",
            Verification = "Ação pontual.", Revert = "Não altera configurações.", Evidence = "Resolve sites que não abrem depois de mudanças de DNS; não acelera a internet.",
            Goals = GoalSet.None, Read = _ => Pending("Ação pontual."),
        },
        new()
        {
            Id = "maint.cleanmgr", Operation = "padrao", Step = "Executando limpeza de disco (cleanmgr)", Name = "Limpeza de disco do Windows", OneOff = true,
            Description = "Roda a Limpeza de Disco do Windows nas categorias seguras.", Category = "Manutenção", Risk = StepRisk.Safe,
            SideEffects = "Os arquivos apagados não voltam (são temporários e sobras de atualização).", Verification = "Ação pontual.", Revert = "Não reversível (arquivos temporários).",
            Evidence = "Ferramenta oficial do Windows.", Goals = GoalSet.Daily, Read = _ => Pending("Ação pontual."),
        },
    };

    private static readonly HashSet<Guid> KnownPlans = new()
    {
        new("381b4222-f694-41f0-9685-ff5bb260df2e"), // Equilibrado
        new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), // Alto desempenho
        new("a1841308-3541-4fab-bc81-f71556f20b4a"), // Economia de energia
        new("e9a42b02-d5df-448d-aa00-03f14749eb61"), // Desempenho máximo
        PowerPlanService.QrzGuid,
    };
}
