using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PQueirozOptimizer.BiosAdvisor;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer.Engine;

/// <summary>Resumo do PC que a análise mostra e usa para decidir.</summary>
public sealed record MachineSummary(
    string Cpu, string Gpu, string GpuDriver, double RamGb, string Storage, string Board, string Windows, int Build,
    string PowerPlan, bool IsLaptop, bool HypervisorPresent, bool WslInstalled, bool DockerInstalled, bool HyperVInstalled, bool GameModeSessionActive, bool DualCcdX3d);

/// <summary>Avaliação de um ajuste: estado lido, bloqueio de plano e se entra pré-selecionado para o objetivo.</summary>
public sealed record TweakAssessment(TweakDefinition Tweak, TweakState State, string Detail, bool Locked, PlanTier RequiredTier, string? GoalNote, bool Preselected)
{
    /// <summary>Pode ser escolhido: há algo a aplicar, o plano libera e o PC suporta.</summary>
    public bool Selectable => !Locked && State is TweakState.Recommended or TweakState.NeedsReview;
}

public sealed record ConflictNotice(string Title, string Detail, IReadOnlyList<string> TweakIds, bool Blocking);

public sealed record SmartAnalysis(MachineSummary Machine, OptimizationGoal Goal, IReadOnlyList<TweakAssessment> Items, IReadOnlyList<ConflictNotice> Notices, DateTime At)
{
    public int Count(TweakState state) => Items.Count(i => i.State == state);
}

/// <summary>
/// Smart Optimize: lê o estado de cada ajuste do catálogo, aplica as regras do objetivo e as condições do PC e
/// detecta conflitos. Não altera nada: a aplicação é feita pelo script (com backup e ponto de restauração).
/// </summary>
public static class SmartOptimizer
{
    public static readonly string[] OperationOrder = { "padrao", "debloat", "gamer" };

    public static bool IsDualCcdX3d(string cpuName) => Regex.IsMatch(cpuName ?? "", @"Ryzen\s+\d+\s+(7900|7950|9900|9950)X3D", RegexOptions.IgnoreCase);

    /// <param name="availableSteps">Etapas que o script oferece neste PC, por operação (já filtradas pelas condições do script).</param>
    public static SmartAnalysis Analyze(MachineSummary machine, TweakContext context, OptimizationGoal goal, LicenseInfo? license, Func<string, IReadOnlyCollection<string>> availableSteps)
    {
        var steps = OperationOrder.ToDictionary(op => op, op => availableSteps(op));
        var items = new List<TweakAssessment>();
        foreach (var tweak in TweakCatalog.All)
        {
            TweakReading reading;
            try { reading = tweak.Read(context); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidCastException) { reading = new(TweakState.ReadFailed, "Não foi possível ler: " + ex.Message); }
            // A condição do script (Windows, GPU, desktop) vale mais que a leitura: o script pularia a etapa
            if (reading.State is TweakState.Recommended or TweakState.NeedsReview && !steps[tweak.Operation].Contains(tweak.Step))
                reading = new(TweakState.NotApplicable, "Não se aplica a este PC (versão do Windows, placa de vídeo ou tipo de PC).");
            var page = PlanAccess.OperationPage(tweak.Operation);
            var locked = !PlanAccess.Allows(license, page);
            string? goalNote = tweak.AvoidFor.TryGetValue(goal, out var avoid) ? avoid : null;
            var preselect = goal != OptimizationGoal.Custom && !locked && reading.State == TweakState.Recommended && goalNote is null
                && tweak.RecommendedFor(goal) && tweak.Risk != StepRisk.High;
            items.Add(new TweakAssessment(tweak, reading.State, reading.Detail, locked, PlanAccess.Required(page), goalNote, preselect));
        }
        return new SmartAnalysis(machine, goal, items, Notices(machine, goal), DateTime.Now);
    }

    /// <summary>Avisos do PC (não dependem da seleção).</summary>
    public static List<ConflictNotice> Notices(MachineSummary m, OptimizationGoal goal)
    {
        var list = new List<ConflictNotice>();
        if (m.GameModeSessionActive)
            list.Add(new("Modo Jogo ativo", "O Modo Jogo temporário está ligado e controla o plano de energia e os serviços pausados. Desative-o antes de aplicar ajustes de energia.", new[] { "power.qrz" }, false));
        if (m.DualCcdX3d)
            list.Add(new("Ryzen X3D com dois CCDs", "O Windows usa a Game Bar e o plano Equilibrado para levar o jogo ao CCD com 3D V-Cache. Esses ajustes ficam bloqueados neste PC.", new[] { "power.qrz", "game.dvr" }, false));
        if (m.WslInstalled || m.DockerInstalled || m.HyperVInstalled || m.HypervisorPresent)
        {
            var found = new[] { m.WslInstalled ? "WSL" : null, m.DockerInstalled ? "Docker" : null, m.HyperVInstalled ? "Hyper-V" : null }.OfType<string>().ToList();
            list.Add(new("Virtualização em uso" + (found.Count > 0 ? $" ({string.Join(", ", found)})" : ""),
                "O Smart Optimize não mexe em Hyper-V, WSL, Docker nem nos recursos de virtualização. Para desligá-los, use Serviços, de forma consciente.", Array.Empty<string>(), false));
        }
        if (goal == OptimizationGoal.Laptop && !m.IsLaptop)
            list.Add(new("Objetivo Notebook em um desktop", "Este PC não tem bateria. O perfil Notebook prioriza consumo e temperatura; para jogos, escolha Gaming competitivo.", Array.Empty<string>(), false));
        return list;
    }

    /// <summary>
    /// Conflitos da seleção. Os bloqueantes impedem aplicar até o usuário resolver: nada conflitante é aplicado
    /// automaticamente.
    /// </summary>
    public static List<ConflictNotice> SelectionConflicts(IReadOnlyCollection<string> selected, MachineSummary machine)
    {
        var list = new List<ConflictNotice>();
        if (selected.Contains("privacy.policies") && selected.Contains("privacy.diagrequired"))
            list.Add(new("Telemetria configurada duas vezes", "\"Políticas de diagnóstico, nuvem e IA\" grava AllowTelemetry = 0 e \"Só dados de diagnóstico obrigatórios\" grava 1, desfazendo parte da primeira. Escolha só uma.",
                new[] { "privacy.policies", "privacy.diagrequired" }, true));
        if (machine.GameModeSessionActive && selected.Contains("power.qrz"))
            list.Add(new("Plano de energia com Modo Jogo ativo", "O Modo Jogo vai restaurar o plano anterior ao ser desligado e desfaria a troca. Desative o Modo Jogo antes.", new[] { "power.qrz" }, true));
        if (selected.Contains("power.hibernate") && machine.IsLaptop)
            list.Add(new("Hibernação em notebook", "Notebooks usam a hibernação quando a bateria acaba.", new[] { "power.hibernate" }, true));
        return list;
    }

    /// <summary>Agrupa a seleção por operação do script, na ordem em que serão executadas.</summary>
    public static List<(string Operation, string[] Steps)> Plan(IEnumerable<string> selectedIds) =>
        OperationOrder.Select(op => (op, TweakCatalog.All.Where(t => t.Operation == op && selectedIds.Contains(t.Id)).Select(t => t.Step).ToArray()))
            .Where(g => g.Item2.Length > 0).ToList();

    /// <summary>Resultado conferido depois de aplicar: o estado lido de novo, não a saída do script.</summary>
    public static List<(TweakDefinition Tweak, bool Verified, string Detail)> Verify(IEnumerable<string> selectedIds, TweakContext after) =>
        selectedIds.Select(id => TweakCatalog.Find(id)).OfType<TweakDefinition>().Select(t =>
        {
            if (t.OneOff) return (t, true, "Ação pontual executada (não fica registrada como estado).");
            TweakReading r;
            try { r = t.Read(after); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return (t, false, "Não foi possível ler o estado: " + ex.Message); }
            return r.State == TweakState.Applied ? (t, true, r.Detail + (t.RequiresReboot ? " Reinicie para valer." : ""))
                : (t, false, r.State == TweakState.NotApplicable ? r.Detail : "Estado continua: " + r.Detail + " O script pode ter pulado a etapa (veja o registro da execução).");
        }).ToList();
}

/// <summary>Estado real do Windows: registro de 64 bits, serviços, plano de energia, apps da Loja e tarefas.</summary>
public sealed class LiveSystemState : ISystemState
{
    public object? Registry(RegistryHive hive, string key, string name)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var sub = root.OpenSubKey(key);
        return sub?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public IReadOnlyList<string> SubKeys(RegistryHive hive, string key)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var sub = root.OpenSubKey(key);
            return sub?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return Array.Empty<string>(); }
    }

    public int? ServiceStart(string name)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        return key is null ? null : key.GetValue("Start") as int? ?? 3;
    }

    public Guid? ActivePowerPlan { get; } = PowerPlanService.ActiveScheme();
    public IReadOnlySet<string>? AppxPackages { get; private set; }
    public IReadOnlyDictionary<string, bool>? TaskEnabled { get; private set; }

    public bool OneDriveInstalled =>
        new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(f => Environment.GetFolderPath(f)).Where(p => p.Length > 0)
            .Any(p => File.Exists(Path.Combine(p, "Microsoft", "OneDrive", "OneDrive.exe")) || File.Exists(Path.Combine(p, "Microsoft OneDrive", "OneDrive.exe")));

    /// <summary>Lê, numa única chamada ao PowerShell, os apps da Loja e as tarefas que o catálogo confere.</summary>
    public static async Task<LiveSystemState> LoadAsync(CancellationToken token = default)
    {
        var state = new LiveSystemState();
        try
        {
            var json = await PowerShellBridge.RunScriptAsync(
                "$a = try { @(Get-AppxPackage -AllUsers -ErrorAction Stop | ForEach-Object Name) } catch { @(Get-AppxPackage | ForEach-Object Name) };" +
                "$t = @(Get-ScheduledTask -TaskPath '\\Microsoft\\Windows\\Customer Experience Improvement Program\\','\\Microsoft\\Windows\\Feedback\\Siuf\\' -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ P = $_.TaskPath + $_.TaskName; E = [bool]($_.State -ne 'Disabled') } });" +
                "[pscustomobject]@{ A = @($a | Sort-Object -Unique); T = $t } | ConvertTo-Json -Compress -Depth 3",
                timeout: TimeSpan.FromSeconds(60), cancellationToken: token);
            using var doc = JsonDocument.Parse(json);
            var a = doc.RootElement.GetProperty("A");
            state.AppxPackages = new HashSet<string>((a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(e => e.GetString() ?? "") : new[] { a.GetString() ?? "" }).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
            var t = doc.RootElement.GetProperty("T");
            var tasks = t.ValueKind == JsonValueKind.Array ? t.EnumerateArray().ToList() : t.ValueKind == JsonValueKind.Object ? new List<JsonElement> { t } : new List<JsonElement>();
            state.TaskEnabled = tasks.ToDictionary(e => e.GetProperty("P").GetString() ?? "", e => e.GetProperty("E").GetBoolean(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or JsonException or KeyNotFoundException) { } // leituras ficam null: "Falha na leitura"
        return state;
    }
}

/// <summary>Leitura do hardware para a análise (WMI pelo detector do BIOS Advisor) e dos ambientes de desenvolvimento.</summary>
public static class MachineReader
{
    public static MachineSummary Read(HardwareProfile hw, Models.SystemSnapshot snapshot)
    {
        var gpu = hw.PrimaryGpu;
        var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
        var wsl = lxss is not null && lxss.GetSubKeyNames().Length > 0;
        lxss?.Dispose();
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var docker = File.Exists(Path.Combine(programFiles, "Docker", "Docker", "Docker Desktop.exe")) || ServiceExists("com.docker.service");
        var plan = PowerPlanService.ActiveScheme();
        var planName = plan == PowerPlanService.QrzGuid ? "Qrz" : plan?.ToString() switch
        {
            "381b4222-f694-41f0-9685-ff5bb260df2e" => "Equilibrado", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" => "Alto desempenho",
            "a1841308-3541-4fab-bc81-f71556f20b4a" => "Economia de energia", "e9a42b02-d5df-448d-aa00-03f14749eb61" => "Desempenho máximo",
            null => "Desconhecido", _ => "Personalizado",
        };
        var board = string.Join(" ", new[] { hw.Motherboard.Manufacturer, hw.Motherboard.Product }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new MachineSummary(
            hw.Cpu.Name.Length > 0 ? hw.Cpu.Name : snapshot.Processor, gpu?.Name ?? snapshot.Graphics, gpu?.DriverVersion ?? "", snapshot.MemoryGb,
            $"{snapshot.FreeSpace} livres de {snapshot.Storage}", board.Length > 0 ? board : "Não informado", snapshot.OperatingSystem, SystemConditions.WindowsBuild,
            planName, hw.IsLaptop || !SystemConditions.IsDesktop, hw.Cpu.HypervisorPresent, wsl, docker, ServiceExists("vmms"),
            GamingService.ActiveSession() is not null, SmartOptimizer.IsDualCcdX3d(hw.Cpu.Name));
    }

    private static bool ServiceExists(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        return key is not null;
    }

    /// <summary>Contexto sem a leitura completa do hardware (WMI): CPU pelo registro e placa AMD pelas chaves do driver.</summary>
    public static TweakContext QuickContext(ISystemState state)
    {
        var cpu = state.Registry(RegistryHive.LocalMachine, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") as string ?? "";
        const string display = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        var amd = state.SubKeys(RegistryHive.LocalMachine, display).Where(k => k.Length == 4 && k.All(char.IsDigit))
            .Any(k => (state.Registry(RegistryHive.LocalMachine, display + "\\" + k, "ProviderName") as string ?? "") is var p && (p.Contains("AMD", StringComparison.OrdinalIgnoreCase) || p.Contains("ATI", StringComparison.OrdinalIgnoreCase) || p.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase)));
        return new TweakContext { System = state, IsDesktop = SystemConditions.IsDesktop, DualCcdX3d = SmartOptimizer.IsDualCcdX3d(cpu), HasAmdGpu = amd, WindowsBuild = SystemConditions.WindowsBuild };
    }

    public static TweakContext Context(ISystemState state, MachineSummary m, HardwareProfile hw) => new()
    {
        System = state, IsDesktop = !m.IsLaptop, DualCcdX3d = m.DualCcdX3d, HasAmdGpu = hw.Gpus.Any(g => g.IsAmd), WindowsBuild = m.Build,
    };
}

/// <summary>Histórico das execuções do Smart Optimize (para o painel e para a reversão consciente).</summary>
public sealed class SmartHistory
{
    public sealed record Run(DateTime At, OptimizationGoal Goal, string[] Selected, string[] Verified, string[] NotVerified);
    public sealed record AnalysisMark(DateTime At, OptimizationGoal Goal, int Recommended, int Applied);

    private sealed class Data { public List<Run> Runs { get; set; } = new(); public AnalysisMark? LastAnalysis { get; set; } }

    public static SmartHistory Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "smart-history.json"));
    private readonly string _path;
    public SmartHistory(string path) => _path = path;

    private Data Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    private void Save(Data data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public IReadOnlyList<Run> Runs => Load().Runs;
    public AnalysisMark? LastAnalysis => Load().LastAnalysis;

    public void RecordAnalysis(SmartAnalysis analysis)
    {
        var data = Load();
        data.LastAnalysis = new AnalysisMark(analysis.At, analysis.Goal, analysis.Count(TweakState.Recommended), analysis.Count(TweakState.Applied));
        Save(data);
    }

    public void RecordRun(Run run)
    {
        var data = Load();
        data.Runs.Insert(0, run);
        if (data.Runs.Count > 30) data.Runs.RemoveRange(30, data.Runs.Count - 30);
        Save(data);
    }
}
