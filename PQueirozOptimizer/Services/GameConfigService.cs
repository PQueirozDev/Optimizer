using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

public enum ConfigFormat { Ini, QuotedKeyValue }

/// <summary>
/// Um valor do arquivo de configuração do jogo (seção vazia no formato "chave" "valor"). <paramref name="File"/>
/// escolhe o arquivo quando o jogo usa mais de um (vazio = o arquivo principal).
/// </summary>
public sealed record ConfigValue(string Section, string Key, string Value, string File = "");

/// <summary>Uma das opções de configuração de um jogo (ex.: Otimizado ou Qrz).</summary>
public sealed record GameProfile(string Id, string Name, string Description, ConfigValue[] Values);

/// <summary>Preset competitivo de um jogo: onde fica o arquivo e quais valores muda (ou os perfis para escolher).</summary>
public sealed record GamePreset(string Id, string Name, string[] Processes, ConfigFormat Format, string Description, ConfigValue[] Values, string[] Notes, GameProfile[]? Profiles = null)
{
    public GameProfile? Profile(string? id) => Profiles?.FirstOrDefault(p => p.Id == id) ?? Profiles?.FirstOrDefault();
    public ConfigValue[] ValuesFor(string? profileId) => Profile(profileId)?.Values ?? Values;
    /// <summary>Todos os arquivos que o preset pode alterar (de qualquer perfil).</summary>
    public IEnumerable<string> Files => Values.Concat(Profiles?.SelectMany(p => p.Values) ?? Enumerable.Empty<ConfigValue>()).Select(v => v.File).Distinct();
}

/// <summary>
/// Presets de configuração de jogos (menos efeitos pesados, sem V-Sync, sem desfoque de movimento) gravados
/// direto no arquivo de configuração. O arquivo original é copiado antes; restaurar devolve a cópia.
/// </summary>
public sealed class GameConfigService
{
    private readonly ActivityLog _log;
    private static readonly string BackupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "GameConfigs");

    public GameConfigService(ActivityLog log) => _log = log;

    private const string FortniteUser = "/Script/FortniteGame.FortGameUserSettings";
    private const string RocketSystem = "SystemSettings";
    private const string ValorantMachine = "/Script/ShooterGame.ShooterGameUserSettings";

    // VALORANT: o arquivo da máquina (tela, V-Sync, limite de FPS) e o da conta Riot (qualidade e opções de jogo).
    // Só chaves que o próprio jogo grava; tela cheia = modo 0 do Unreal.
    private static ConfigValue[] ValorantValues(int ui, int anisotropic, bool hideTracers) => new[]
    {
        new ConfigValue(ValorantMachine, "PreferredFullscreenMode", "0", "game"), new ConfigValue(ValorantMachine, "LastConfirmedFullscreenMode", "0", "game"),
        new ConfigValue(ValorantMachine, "bUseVSync", "False", "game"), new ConfigValue(ValorantMachine, "bUseDynamicResolution", "False", "game"),
        new ConfigValue(ValorantMachine, "FrameRateLimit", "0.000000", "game"),
        new ConfigValue("Settings", "EAresIntSettingName::MaterialQuality", "0", "account"), new ConfigValue("Settings", "EAresIntSettingName::TextureQuality", "0", "account"),
        new ConfigValue("Settings", "EAresIntSettingName::DetailQuality", "0", "account"), new ConfigValue("Settings", "EAresIntSettingName::UIQuality", ui.ToString(), "account"),
        new ConfigValue("Settings", "EAresIntSettingName::AnisotropicFiltering", anisotropic.ToString(), "account"),
        new ConfigValue("Settings", "EAresBoolSettingName::DisableDistortion", "True", "account"),
        new ConfigValue("Settings", "EAresIntSettingName::NvidiaReflexLowLatencySetting", "2", "account"),
        new ConfigValue("Settings", "EAresBoolSettingName::ShowBlood", "False", "account"), new ConfigValue("Settings", "EAresBoolSettingName::ShowCorpses", "False", "account"),
    }.Concat(hideTracers ? new[] { new ConfigValue("Settings", "EAresBoolSettingName::ShowBulletTracers", "False", "account") } : Array.Empty<ConfigValue>()).ToArray();

    public static readonly GamePreset[] Presets =
    {
        new("fortnite", "Fortnite", new[] { "FortniteClient-Win64-Shipping", "FortniteLauncher" }, ConfigFormat.Ini,
            "Sombras, efeitos, pós-processamento e folhagem no mínimo, sem V-Sync e sem desfoque de movimento; distância de visão alta para enxergar longe.",
            new ConfigValue[]
            {
                new(FortniteUser, "bUseVSync", "False"), new(FortniteUser, "bMotionBlur", "False"), new(FortniteUser, "bShowGrass", "False"),
                new("ScalabilityGroups", "sg.ShadowQuality", "0"), new("ScalabilityGroups", "sg.PostProcessQuality", "0"),
                new("ScalabilityGroups", "sg.EffectsQuality", "0"), new("ScalabilityGroups", "sg.FoliageQuality", "0"),
                new("ScalabilityGroups", "sg.AntiAliasingQuality", "0"), new("ScalabilityGroups", "sg.GlobalIlluminationQuality", "0"),
                new("ScalabilityGroups", "sg.ReflectionQuality", "0"), new("ScalabilityGroups", "sg.ViewDistanceQuality", "3"),
                new("ScalabilityGroups", "sg.ResolutionQuality", "100.000000"),
            },
            new[] { "Feche o Fortnite antes de aplicar: ele regrava o arquivo ao fechar.", "Para o modo Desempenho (DX11 leve), escolha-o no menu de vídeo do jogo." }),
        new("apex", "Apex Legends", new[] { "r5apex", "r5apex_dx12" }, ConfigFormat.QuotedKeyValue,
            "Sombras, oclusão de ambiente, iluminação volumétrica e decalques desligados; sem V-Sync e sem resolução dinâmica.",
            new ConfigValue[]
            {
                new("", "setting.mat_vsync_mode", "0"), new("", "setting.dvs_enable", "0"), new("", "setting.shadow_enable", "0"),
                new("", "setting.csm_enabled", "0"), new("", "setting.ssao_enabled", "0"), new("", "setting.volumetric_lighting", "0"),
                new("", "setting.mat_depthfeather_enable", "0"), new("", "setting.r_createmodeldecals", "0"),
                new("", "setting.modeldecals_forceAllowed", "0"), new("", "setting.cl_ragdoll_maxcount", "0"),
                new("", "setting.particle_cpu_level", "0"), new("", "setting.mat_antialias_mode", "0"),
            },
            new[] { "Feche o Apex antes de aplicar.", "Texturas e modelos não mudam: ajuste-os pela VRAM da sua placa." }),
        new("cs2", "Counter-Strike 2", new[] { "cs2" }, ConfigFormat.QuotedKeyValue,
            "Sem V-Sync e com NVIDIA Reflex ligado; oclusão de ambiente desligada e partículas no mínimo. As sombras ficam como estão (elas ajudam a ver inimigos).",
            new ConfigValue[]
            {
                new("", "setting.mat_vsync", "0"), new("", "setting.r_low_latency", "1"),
                new("", "setting.videocfg_ao_detail", "0"), new("", "setting.videocfg_particle_detail", "0"),
            },
            new[] { "Feche o CS2 antes de aplicar.", "Vale para a conta Steam que jogou CS2 por último neste PC." }),
        new("rocketleague", "Rocket League", new[] { "RocketLeague" }, ConfigFormat.Ini,
            "Desfoque de movimento, profundidade de campo, bloom, reflexos de luz e sombras dinâmicas desligados; sem V-Sync.",
            new ConfigValue[]
            {
                new(RocketSystem, "MotionBlur", "False"), new(RocketSystem, "DepthOfField", "False"), new(RocketSystem, "Bloom", "False"),
                new(RocketSystem, "LightShafts", "False"), new(RocketSystem, "LensFlares", "False"), new(RocketSystem, "DynamicShadows", "False"),
                new(RocketSystem, "AmbientOcclusion", "False"), new(RocketSystem, "UseVsync", "False"),
            },
            new[] { "Feche o Rocket League antes de aplicar." }),
        new("valorant", "VALORANT", new[] { "VALORANT-Win64-Shipping", "VALORANT" }, ConfigFormat.Ini,
            "Escolha o perfil: Otimizado (o máximo de FPS) ou Qrz (a configuração usada pelo Qrz). Sensibilidade, mira, teclas e volume continuam os seus.",
            Array.Empty<ConfigValue>(),
            new[] { "Feche o VALORANT antes de aplicar. Ao entrar, confira em Configurações → Vídeo: o jogo sincroniza parte das opções com a conta Riot." },
            new GameProfile[]
            {
                new("otimizado", "Otimizado", "Material, textura, detalhes e interface no baixo, filtragem anisotrópica 4x, sem distorção, NVIDIA Reflex com Boost, tela cheia sem V-Sync e sem limite de FPS; sangue e corpos desligados.",
                    ValorantValues(ui: 0, anisotropic: 4, hideTracers: false)),
                new("qrz", "Qrz", "A configuração do Qrz: material, textura e detalhes no baixo, interface no alto, filtragem anisotrópica 16x, sem distorção, NVIDIA Reflex com Boost, tela cheia sem V-Sync e sem limite de FPS; sangue, corpos e rastros de bala desligados.",
                    ValorantValues(ui: 2, anisotropic: 16, hideTracers: true)),
            }),
    };

    // ---------- Localização dos arquivos ----------
    public static string? ConfigPath(GamePreset preset)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string? path = preset.Id switch
        {
            "fortnite" => Path.Combine(local, "FortniteGame", "Saved", "Config", "WindowsClient", "GameUserSettings.ini"),
            "apex" => Path.Combine(profile, "Saved Games", "Respawn", "Apex", "local", "videoconfig.txt"),
            "rocketleague" => Path.Combine(documents, "My Games", "Rocket League", "TAGame", "Config", "TASystemSettings.ini"),
            "cs2" => Cs2VideoConfig(),
            "valorant" => ValorantAccountConfig(),
            _ => null,
        };
        return path != null && File.Exists(path) ? path : null;
    }

    /// <summary>Arquivo de configuração pelo nome usado nos valores ("" = o principal; VALORANT: "game" e "account").</summary>
    public static string? FilePath(GamePreset preset, string file)
    {
        if (file.Length == 0 || (preset.Id == "valorant" && file == "account")) return ConfigPath(preset);
        if (preset.Id == "valorant" && file == "game")
        {
            var path = Path.Combine(ValorantConfigRoot, "WindowsClient", "GameUserSettings.ini");
            return File.Exists(path) ? path : null;
        }
        return null;
    }

    /// <summary>Pasta de configuração do VALORANT (o teste aponta para uma cópia).</summary>
    internal static string ValorantConfigRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VALORANT", "Saved", "Config");

    /// <summary>Configurações da última conta Riot que entrou neste PC (o próprio jogo grava qual foi).</summary>
    private static string? ValorantAccountConfig()
    {
        if (!Directory.Exists(ValorantConfigRoot)) return null;
        string? lastUser = null;
        var machine = Path.Combine(ValorantConfigRoot, "WindowsClient", "RiotLocalMachine.ini");
        if (File.Exists(machine))
            lastUser = File.ReadLines(machine).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("LastKnownUser=", StringComparison.OrdinalIgnoreCase))?["LastKnownUser=".Length..];
        var accounts = Directory.EnumerateDirectories(ValorantConfigRoot)
            .Select(d => Path.Combine(d, "Windows", "RiotUserSettings.ini"))
            .Where(File.Exists)
            .ToList();
        return accounts.FirstOrDefault(p => !string.IsNullOrEmpty(lastUser) && Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(p))!)!.StartsWith(lastUser, StringComparison.OrdinalIgnoreCase))
            ?? accounts.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    private static string? Cs2VideoConfig()
    {
        using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (steam?.GetValue("SteamPath") is not string steamPath) return null;
        var userdata = Path.Combine(steamPath.Replace('/', Path.DirectorySeparatorChar), "userdata");
        if (!Directory.Exists(userdata)) return null;
        // A conta que jogou por último tem o arquivo modificado mais recentemente
        return Directory.EnumerateDirectories(userdata)
            .Select(d => Path.Combine(d, "730", "local", "cfg", "cs2_video.txt"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static bool IsRunning(GamePreset preset) =>
        preset.Processes.Any(name => { var list = Process.GetProcessesByName(name); foreach (var p in list) p.Dispose(); return list.Length > 0; });

    /// <summary>Pasta das cópias originais (o teste aponta para uma pasta temporária).</summary>
    internal static string BackupRoot { get; set; } = BackupDirectory;

    // O arquivo principal mantém o nome antigo ("original.ext"), para backups feitos por versões anteriores valerem
    private static string BackupPath(GamePreset preset, string configPath, string file = "") =>
        Path.Combine(BackupRoot, preset.Id, (file.Length == 0 ? "original" : "original-" + file) + Path.GetExtension(configPath));
    private static string ProfileMarker(GamePreset preset) => Path.Combine(BackupRoot, preset.Id, "perfil.txt");

    public static bool HasBackup(GamePreset preset) =>
        ConfigPath(preset) != null && preset.Files.Any(file => FilePath(preset, file) is { } path && File.Exists(BackupPath(preset, path, file)));

    /// <summary>Perfil aplicado por último (Otimizado, Qrz...), ou null.</summary>
    public static string? AppliedProfile(GamePreset preset)
    {
        var marker = ProfileMarker(preset);
        return preset.Profiles != null && HasBackup(preset) && File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
    }

    // ---------- Aplicar e restaurar ----------
    public int Apply(GamePreset preset, string? profileId = null)
    {
        if (ConfigPath(preset) is null) throw new FileNotFoundException($"Arquivo de configuração do {preset.Name} não encontrado. Abra o jogo uma vez e feche para ele criar o arquivo.");
        if (IsRunning(preset)) throw new InvalidOperationException($"Feche o {preset.Name} antes de aplicar: ele regrava as configurações ao fechar.");
        var profile = preset.Profile(profileId);
        var changes = 0;
        foreach (var group in preset.ValuesFor(profileId).GroupBy(v => v.File))
        {
            var path = FilePath(preset, group.Key) ?? throw new FileNotFoundException($"Um dos arquivos de configuração do {preset.Name} não foi encontrado. Abra o jogo uma vez e feche para ele criar os arquivos.");
            var backup = BackupPath(preset, path, group.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            if (!File.Exists(backup)) File.Copy(path, backup);

            var text = File.ReadAllText(path);
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var (updated, count) = preset.Format == ConfigFormat.Ini ? SetIniValues(text, group, newline) : SetQuotedValues(text, group, newline);
            // O arquivo pode estar como somente leitura (truque comum de guias); o preset precisa gravar
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            File.WriteAllText(path, updated, new UTF8Encoding(false));
            changes += count;
        }
        if (profile != null) File.WriteAllText(ProfileMarker(preset), profile.Id);
        _log.Write("SUCCESS", $"Preset competitivo aplicado: {preset.Name}{(profile != null ? " — perfil " + profile.Name : "")} ({changes} valores)");
        return changes;
    }

    public void Restore(GamePreset preset)
    {
        if (ConfigPath(preset) is null) throw new FileNotFoundException($"Arquivo de configuração do {preset.Name} não encontrado.");
        if (IsRunning(preset)) throw new InvalidOperationException($"Feche o {preset.Name} antes de restaurar.");
        var restored = 0;
        foreach (var file in preset.Files)
        {
            if (FilePath(preset, file) is not { } path) continue;
            var backup = BackupPath(preset, path, file);
            if (!File.Exists(backup)) continue;
            File.Copy(backup, path, overwrite: true);
            File.Delete(backup);
            restored++;
        }
        if (restored == 0) throw new FileNotFoundException("Não há cópia original deste jogo.");
        if (File.Exists(ProfileMarker(preset))) File.Delete(ProfileMarker(preset));
        _log.Write("SUCCESS", $"Configurações originais restauradas: {preset.Name}");
    }

    /// <summary>Troca (ou acrescenta) chaves em arquivos .ini, respeitando as seções.</summary>
    public static (string Text, int Changes) SetIniValues(string text, IEnumerable<ConfigValue> values, string newline = "\r\n")
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var changes = 0;
        foreach (var v in values)
        {
            var header = lines.FindIndex(l => l.Trim().Equals($"[{v.Section}]", StringComparison.OrdinalIgnoreCase));
            if (header < 0)
            {
                if (lines.Count > 0 && lines[^1].Length > 0) lines.Add("");
                lines.Add($"[{v.Section}]"); lines.Add($"{v.Key}={v.Value}");
                changes++; continue;
            }
            var end = lines.FindIndex(header + 1, l => l.TrimStart().StartsWith('['));
            if (end < 0) end = lines.Count;
            var at = lines.FindIndex(header + 1, end - header - 1, l => l.Split('=')[0].Trim().Equals(v.Key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                if (lines[at] != $"{v.Key}={v.Value}") { lines[at] = $"{lines[at].Split('=')[0]}={v.Value}"; changes++; }
            }
            else
            {
                // Insere antes das linhas em branco do fim da seção
                var insert = end;
                while (insert > header + 1 && string.IsNullOrWhiteSpace(lines[insert - 1])) insert--;
                lines.Insert(insert, $"{v.Key}={v.Value}");
                changes++;
            }
        }
        return (string.Join(newline, lines), changes);
    }

    private static readonly Regex QuotedLine = new("^(?<lead>\\s*)\"(?<key>[^\"]+)\"(?<gap>\\s+)\"(?<value>[^\"]*)\"(?<rest>.*)$");

    /// <summary>Troca (ou acrescenta) pares "chave" "valor" no formato da Valve/Respawn.</summary>
    public static (string Text, int Changes) SetQuotedValues(string text, IEnumerable<ConfigValue> values, string newline = "\r\n")
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var changes = 0;
        foreach (var v in values)
        {
            var at = lines.FindIndex(l => QuotedLine.Match(l) is { Success: true } m && m.Groups["key"].Value.Equals(v.Key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                var m = QuotedLine.Match(lines[at]);
                if (m.Groups["value"].Value != v.Value) { lines[at] = $"{m.Groups["lead"].Value}\"{m.Groups["key"].Value}\"{m.Groups["gap"].Value}\"{v.Value}\"{m.Groups["rest"].Value}"; changes++; }
            }
            else
            {
                var close = lines.FindLastIndex(l => l.Trim() == "}");
                if (close < 0) continue;
                var indent = lines.Select(l => QuotedLine.Match(l)).FirstOrDefault(m => m.Success)?.Groups["lead"].Value ?? "\t";
                lines.Insert(close, $"{indent}\"{v.Key}\"\t\t\"{v.Value}\"");
                changes++;
            }
        }
        return (string.Join(newline, lines), changes);
    }
}
