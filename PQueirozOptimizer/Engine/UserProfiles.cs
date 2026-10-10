using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PQueirozOptimizer.Engine;

/// <summary>
/// Perfil personalizado (fase 15): só dados — objetivo, ajustes do catálogo, processos de jogo e opções do Modo Jogo.
/// Nunca guarda comandos ou scripts: ao importar, o que não é reconhecido é descartado.
/// </summary>
public sealed record UserProfile
{
    public const int CurrentFormat = 1;
    [JsonPropertyName("format")] public int Format { get; init; } = CurrentFormat;
    [JsonPropertyName("id")] public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("goal"), JsonConverter(typeof(JsonStringEnumConverter))] public OptimizationGoal Goal { get; init; } = OptimizationGoal.Custom;
    [JsonPropertyName("tweaks")] public string[] Tweaks { get; init; } = Array.Empty<string>();
    [JsonPropertyName("processes")] public string[] Processes { get; init; } = Array.Empty<string>();
    /// <summary>Ativar o Modo Jogo temporário quando um dos processos abrir (e restaurar ao fechar).</summary>
    [JsonPropertyName("autoGameMode")] public bool AutoGameMode { get; init; }
    [JsonPropertyName("closeApps")] public bool CloseBackgroundApps { get; init; }
    [JsonPropertyName("pauseServices")] public bool PauseServices { get; init; } = true;
    [JsonPropertyName("highPerformance")] public bool HighPerformancePlan { get; init; } = true;
    [JsonPropertyName("purgeStandby")] public bool PurgeStandby { get; init; }
    [JsonPropertyName("updated")] public DateTime UpdatedAt { get; init; } = DateTime.Now;
}

public static class ProfileValidation
{
    public const int MaxBytes = 64 * 1024, MaxTweaks = 60, MaxProcesses = 20, MaxName = 60, MaxDescription = 300;
    private static readonly Regex ProcessName = new(@"^[A-Za-z0-9 _.\-()]{1,80}\.exe$", RegexOptions.Compiled);

    public static bool IsValidProcess(string name) => ProcessName.IsMatch(name) && !name.Contains("..");

    /// <summary>
    /// Limpa um perfil vindo de arquivo: só ajustes que existem no catálogo, nomes de processo simples, textos
    /// curtos. Devolve os avisos do que foi descartado. Lança se o arquivo não for um perfil.
    /// </summary>
    public static (UserProfile Profile, List<string> Warnings) Sanitize(UserProfile raw)
    {
        var warnings = new List<string>();
        if (raw.Format != UserProfile.CurrentFormat) throw new InvalidDataException($"Formato de perfil desconhecido ({raw.Format}).");
        var name = (raw.Name ?? "").Trim();
        if (name.Length == 0) throw new InvalidDataException("O perfil não tem nome.");
        if (name.Length > MaxName) { name = name[..MaxName]; warnings.Add("Nome encurtado."); }
        var description = (raw.Description ?? "").Trim();
        if (description.Length > MaxDescription) description = description[..MaxDescription];
        var tweaks = (raw.Tweaks ?? Array.Empty<string>()).Distinct().ToList();
        var unknown = tweaks.Where(t => TweakCatalog.Find(t) is null).ToList();
        if (unknown.Count > 0) warnings.Add($"Ajustes desconhecidos ignorados: {string.Join(", ", unknown.Take(5))}" + (unknown.Count > 5 ? "..." : ""));
        tweaks = tweaks.Where(t => TweakCatalog.Find(t) is not null).Take(MaxTweaks).ToList();
        var processes = (raw.Processes ?? Array.Empty<string>()).Select(p => (p ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var badProcesses = processes.Where(p => !IsValidProcess(p)).ToList();
        if (badProcesses.Count > 0) warnings.Add($"Processos inválidos ignorados: {string.Join(", ", badProcesses.Take(5))}");
        processes = processes.Where(IsValidProcess).Take(MaxProcesses).ToList();
        if (!Enum.IsDefined(raw.Goal)) throw new InvalidDataException("Objetivo do perfil inválido.");
        return (raw with { Name = name, Description = description, Tweaks = tweaks.ToArray(), Processes = processes.ToArray() }, warnings);
    }

    public static (UserProfile Profile, List<string> Warnings) Import(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Arquivo não encontrado.", path);
        if (info.Length > MaxBytes) throw new InvalidDataException("Arquivo grande demais para ser um perfil.");
        UserProfile? raw;
        try { raw = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(path)); }
        catch (JsonException ex) { throw new InvalidDataException("O arquivo não é um perfil do Qrztweaks: " + ex.Message); }
        if (raw is null) throw new InvalidDataException("O arquivo está vazio.");
        var (profile, warnings) = Sanitize(raw);
        // Importar sempre cria um perfil novo (não sobrescreve um existente com o mesmo id)
        return (profile with { Id = Guid.NewGuid().ToString("N"), UpdatedAt = DateTime.Now }, warnings);
    }

    public static string Export(UserProfile profile) => JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>Perfis salvos em %LocalAppData%\PQueirozOptimizer\profiles.json, com a opção de ativação automática.</summary>
public sealed class ProfileStore
{
    private sealed class Data { public List<UserProfile> Profiles { get; set; } = new(); public bool WatchGames { get; set; } public bool Seeded { get; set; } }
    public static ProfileStore Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "profiles.json"));
    private readonly string _path;
    public ProfileStore(string path) => _path = path;

    private Data Load()
    {
        Data data;
        try { data = File.Exists(_path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { data = new(); }
        if (!data.Seeded) { data.Profiles.AddRange(ProfileTemplates.All()); data.Seeded = true; Save(data); }
        // Arquivo editado à mão também passa pela validação
        data.Profiles = data.Profiles.Select(p => { try { return ProfileValidation.Sanitize(p).Profile; } catch (InvalidDataException) { return null; } }).OfType<UserProfile>().ToList();
        return data;
    }

    private void Save(Data data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(_path + ".tmp", _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public IReadOnlyList<UserProfile> Profiles => Load().Profiles;
    public bool WatchGames { get => Load().WatchGames; set { var d = Load(); d.WatchGames = value; Save(d); } }

    public void Upsert(UserProfile profile)
    {
        var (clean, _) = ProfileValidation.Sanitize(profile with { UpdatedAt = DateTime.Now });
        var data = Load();
        var index = data.Profiles.FindIndex(p => p.Id == clean.Id);
        if (index >= 0) data.Profiles[index] = clean; else data.Profiles.Add(clean);
        Save(data);
    }

    public void Delete(string id) { var d = Load(); d.Profiles.RemoveAll(p => p.Id == id); Save(d); }

    public UserProfile Duplicate(string id)
    {
        var source = Load().Profiles.First(p => p.Id == id);
        var copy = source with { Id = Guid.NewGuid().ToString("N"), Name = (source.Name + " (cópia)").Length > ProfileValidation.MaxName ? source.Name : source.Name + " (cópia)" };
        Upsert(copy);
        return copy;
    }

    /// <summary>Perfil cujo processo de jogo está em execução (para a ativação automática).</summary>
    public UserProfile? MatchRunning(IReadOnlySet<string> runningExe) =>
        Load().Profiles.FirstOrDefault(p => p.AutoGameMode && p.Processes.Any(runningExe.Contains));
}

/// <summary>Perfis iniciais (exemplos editáveis). Os processos são os executáveis oficiais dos jogos.</summary>
public static class ProfileTemplates
{
    private static readonly string[] Competitive = { "game.mode", "game.dvr", "gpu.hags", "gpu.windowed", "input.mouseaccel", "power.qrz", "mm.responsiveness", "net.throttling", "cpu.priority", "sys.mmcss" };

    public static IEnumerable<UserProfile> All() => new[]
    {
        new UserProfile { Name = "Valorant", Description = "Competitivo: Modo Jogo, sem gravação em segundo plano e entrada consistente.", Goal = OptimizationGoal.CompetitiveGaming, Tweaks = Competitive, Processes = new[] { "VALORANT-Win64-Shipping.exe" } },
        new UserProfile { Name = "CS2", Description = "Competitivo: os mesmos ajustes do Valorant para o Counter-Strike 2.", Goal = OptimizationGoal.CompetitiveGaming, Tweaks = Competitive, Processes = new[] { "cs2.exe" } },
        new UserProfile { Name = "Fortnite", Description = "Competitivo, com HAGS (útil para Frame Generation).", Goal = OptimizationGoal.CompetitiveGaming, Tweaks = Competitive, Processes = new[] { "FortniteClient-Win64-Shipping.exe" } },
        new UserProfile { Name = "Streaming", Description = "Jogar e transmitir: mantém o codificador livre, sem fechar o OBS.", Goal = OptimizationGoal.CompetitiveGaming, Tweaks = new[] { "game.mode", "game.dvr", "power.qrz", "cpu.priority" }, Processes = new[] { "obs64.exe" }, PauseServices = false },
        new UserProfile { Name = "Programação", Description = "Preserva Docker, WSL e Hyper-V; só privacidade e estabilidade.", Goal = OptimizationGoal.Development, Tweaks = new[] { "boot.faststartup", "net.deliveryopt", "privacy.suggestions", "privacy.tailored", "privacy.websearch", "privacy.diagrequired" }, HighPerformancePlan = false, PauseServices = false },
        new UserProfile { Name = "Notebook", Description = "Temperatura e bateria: sem ajustes que aumentam o consumo.", Goal = OptimizationGoal.Laptop, Tweaks = new[] { "game.dvr", "ui.visualfx", "svc.telemetry", "privacy.suggestions", "net.deliveryopt" }, HighPerformancePlan = false },
        new UserProfile { Name = "Uso diário", Description = "Privacidade, menos anúncios e limpeza segura.", Goal = OptimizationGoal.DailyUse, Tweaks = new[] { "privacy.suggestions", "privacy.tailored", "privacy.adid", "privacy.websearch", "privacy.activity", "net.deliveryopt", "boot.faststartup", "maint.cleanmgr" }, HighPerformancePlan = false },
    };
}

/// <summary>
/// Observa a abertura dos jogos dos perfis (lista de processos a cada 5 s, sem tocar no processo do jogo: nada é
/// injetado nem lido da memória dele, então não interfere em anti-cheat).
/// </summary>
public sealed class GameWatcher : IDisposable
{
    private readonly Func<IReadOnlySet<string>> _running;
    private readonly Func<IReadOnlySet<string>, UserProfile?> _match;
    private readonly System.Threading.Timer? _timer;
    private string? _activeProfile;
    private string[] _activeProcesses = Array.Empty<string>();
    public event Action<UserProfile>? GameStarted;
    public event Action<string>? GameExited;

    public GameWatcher(Func<IReadOnlySet<string>>? running = null, Func<IReadOnlySet<string>, UserProfile?>? match = null, bool start = true)
    {
        _running = running ?? RunningExecutables;
        _match = match ?? ProfileStore.Default.MatchRunning;
        if (start) _timer = new System.Threading.Timer(_ => Poll(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
    }

    public static IReadOnlySet<string> RunningExecutables()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in System.Diagnostics.Process.GetProcesses()) { try { set.Add(p.ProcessName + ".exe"); } catch (InvalidOperationException) { } finally { p.Dispose(); } }
        return set;
    }

    /// <summary>Uma verificação (público para os testes).</summary>
    public void Poll()
    {
        IReadOnlySet<string> running;
        try { running = _running(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return; }
        if (_activeProfile is null)
        {
            if (_match(running) is { } profile) { _activeProfile = profile.Id; _activeProcesses = profile.Processes; GameStarted?.Invoke(profile); }
        }
        else if (!_activeProcesses.Any(running.Contains))
        {
            var id = _activeProfile; _activeProfile = null; _activeProcesses = Array.Empty<string>();
            GameExited?.Invoke(id);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
