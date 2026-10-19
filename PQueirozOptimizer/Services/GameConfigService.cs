using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Formato do arquivo: Ini (chave=valor), QuotedKeyValue ("chave" "valor" da Valve/Respawn), IniQuoted (chave = "valor"
/// do Overwatch), SpaceKeyValue (chave valor, com ou sem aspas: Rust e Battlefield) e Json (objeto do Tarkov).
/// </summary>
public enum ConfigFormat { Ini, QuotedKeyValue, IniQuoted, SpaceKeyValue, Json }

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

    // Grupos de qualidade do Unreal (0 = baixo). Texturas, anti-aliasing e distância de visão ficam como estão:
    // pesam pouco no FPS e ajudam a enxergar inimigos.
    private static ConfigValue[] UnrealLowEffects(params string[] extraGroups) =>
        new[] { "sg.ShadowQuality", "sg.PostProcessQuality", "sg.EffectsQuality", "sg.FoliageQuality" }.Concat(extraGroups)
            .Select(group => new ConfigValue("ScalabilityGroups", group, "0")).ToArray();

    private const string PubgUser = "/Script/TslGame.TslGameUserSettings";
    private const string DbdUser = "/Script/DeadByDaylight.DBDGameUserSettings";

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
        new("pubg", "PUBG: BATTLEGROUNDS", new[] { "TslGame" }, ConfigFormat.Ini,
            "Sombras, pós-processamento, efeitos e folhagem no baixo; sem V-Sync, sem desfoque de movimento e sem resolução dinâmica. Distância de visão, texturas e anti-aliasing continuam os seus.",
            UnrealLowEffects().Concat(new ConfigValue[]
            {
                new(PubgUser, "bUseVSync", "False"), new(PubgUser, "bMotionBlur", "False"), new(PubgUser, "bUseDynamicResolution", "False"),
            }).ToArray(),
            new[] { "Feche o PUBG antes de aplicar.", "Sensibilidade, teclas e áudio não mudam." }),
        new("thefinals", "THE FINALS", new[] { "Discovery", "Discovery-Win64-Shipping", "TheFinals" }, ConfigFormat.Ini,
            "Sombras, pós-processamento, efeitos, folhagem, iluminação global e reflexos no baixo. Texturas, anti-aliasing e distância de visão continuam os seus.",
            UnrealLowEffects("sg.GlobalIlluminationQuality", "sg.ReflectionQuality"),
            new[] { "Feche o THE FINALS antes de aplicar.", "Desligue o RTXGI (iluminação dinâmica) no menu de vídeo do jogo para ganhar mais FPS." }),
        new("marvelrivals", "Marvel Rivals", new[] { "Marvel-Win64-Shipping", "MarvelRivals-Win64-Shipping", "MarvelRivals", "MarvelRivals_Launcher" }, ConfigFormat.Ini,
            "Sombras, pós-processamento, efeitos, folhagem, iluminação global e reflexos no baixo. Texturas, anti-aliasing e distância de visão continuam os seus.",
            UnrealLowEffects("sg.GlobalIlluminationQuality", "sg.ReflectionQuality"),
            new[] { "Feche o Marvel Rivals antes de aplicar.", "No menu de vídeo do jogo, deixe o Lumen desligado e o NVIDIA Reflex ligado." }),
        new("deadbydaylight", "Dead by Daylight", new[] { "DeadByDaylight-Win64-Shipping", "DeadByDaylight-EGS-Shipping", "DeadByDaylight-WinGDK-Shipping" }, ConfigFormat.Ini,
            "Sombras, pós-processamento, efeitos e folhagem no baixo, sem V-Sync e com o limite de FPS no máximo que o jogo aceita (120).",
            UnrealLowEffects().Concat(new ConfigValue[]
            {
                new(DbdUser, "bUseVSync", "False"), new(DbdUser, "FrameRateLimit", "120"),
            }).ToArray(),
            new[] { "Feche o Dead by Daylight antes de aplicar." }),
        new("overwatch", "Overwatch 2", new[] { "Overwatch" }, ConfigFormat.IniQuoted,
            "Sombras, iluminação e oclusão de ambiente no baixo, física reduzida, NVIDIA Reflex com Boost e sem o vídeo de abertura. Resolução, limite de FPS e mira continuam os seus.",
            new ConfigValue[]
            {
                new("Render.13", "GFXPresetLevel", "1"), new("Render.13", "DirectionalShadowDetail", "0"), new("Render.13", "LightQuality", "0"),
                new("Render.13", "SSAODetail", "0"), new("Render.13", "PhysicsQuality", "1"), new("Render.13", "ReflexMode", "2"),
                new("Cinematics.1", "ShowIntro", "0"),
            },
            new[] { "Feche o Overwatch antes de aplicar.", "O Battle.net pode sincronizar as opções de vídeo da nuvem: confira em Opções → Vídeo ao entrar." }),
        new("rainbowsix", "Rainbow Six Siege", new[] { "RainbowSix", "RainbowSix_DX12", "RainbowSix_Vulkan", "RainbowSix_BE" }, ConfigFormat.Ini,
            "Sombras, oclusão de ambiente, reflexos, iluminação, efeitos de lente, efeitos visuais e profundidade de campo no mínimo; sem V-Sync, sem barras pretas e com 1 quadro na fila da GPU (menos atraso).",
            new ConfigValue[]
            {
                new("DISPLAY_SETTINGS", "VSync", "0"), new("DISPLAY_SETTINGS", "MaxGPUBufferedFrame", "1"), new("DISPLAY_SETTINGS", "UseLetterbox", "0"),
                new("DISPLAY", "NVReflex", "1"),
                new("CUSTOM_QUALITY", "Shadow", "0"), new("CUSTOM_QUALITY", "AO", "0"), new("CUSTOM_QUALITY", "Reflection", "0"),
                new("CUSTOM_QUALITY", "Lighting", "0"), new("CUSTOM_QUALITY", "LensEffects", "0"), new("CUSTOM_QUALITY", "VFX", "0"), new("CUSTOM_QUALITY", "DOF", "0"),
            },
            new[] { "Feche o Rainbow Six Siege antes de aplicar.", "Vale para a conta Ubisoft que jogou por último neste PC." }),
        new("dota2", "Dota 2", new[] { "dota2" }, ConfigFormat.QuotedKeyValue,
            "Sem V-Sync, NVIDIA Reflex ligado, água simples, sem oclusão de ambiente, sem neblina, sem brilho especular e sem animações de ambiente.",
            new ConfigValue[]
            {
                new("", "setting.mat_vsync", "0"), new("", "setting.r_low_latency", "1"), new("", "setting.dota_cheap_water", "true"),
                new("", "setting.r_ssao", "false"), new("", "setting.r_deferred_height_fog", "false"), new("", "setting.r_deferred_specular", "false"),
                new("", "setting.r_deferred_specular_bloom", "false"), new("", "setting.dota_ambient_creatures", "false"), new("", "setting.dota_ambient_cloth", "false"),
            },
            new[] { "Feche o Dota 2 antes de aplicar. Ele regrava o arquivo ao abrir: use \"Travar arquivo\" depois de aplicar." }),
        new("deadlock", "Deadlock", new[] { "deadlock", "citadel" }, ConfigFormat.QuotedKeyValue,
            "Sem V-Sync, NVIDIA Reflex ligado e sem desfoque de movimento; oclusão de ambiente e neblina no mínimo. Sombras continuam como estão.",
            new ConfigValue[]
            {
                new("", "setting.mat_vsync", "0"), new("", "setting.r_low_latency", "1"), new("", "setting.r_citadel_motion_blur", "0"),
                new("", "setting.r_citadel_ssao", "0"), new("", "setting.r_citadel_ssao_quality", "0"), new("", "setting.r_citadel_distancefield_ao_quality", "0"),
                new("", "setting.r_citadel_fog_quality", "0"),
            },
            new[] { "Feche o Deadlock antes de aplicar. Ele regrava o arquivo ao abrir: use \"Travar arquivo\" depois de aplicar." }),
        new("rust", "Rust", new[] { "RustClient" }, ConfigFormat.SpaceKeyValue,
            "Sem V-Sync, sem desfoque de movimento, profundidade de campo, bloom, sujeira na lente e vinheta; oclusão de ambiente e sombras de contato e da grama desligadas.",
            new ConfigValue[]
            {
                new("", "graphics.vsync", "0"), new("", "effects.motionblur", "False"), new("", "graphics.dof", "False"), new("", "effects.bloom", "False"),
                new("", "effects.lensdirt", "False"), new("", "effects.vignet", "False"), new("", "effects.ao", "False"),
                new("", "graphics.contactshadows", "False"), new("", "graphics.grassshadows", "False"),
            },
            new[] { "Feche o Rust antes de aplicar." }),
        new("battlefield6", "Battlefield 6", new[] { "bf6", "Battlefield6" }, ConfigFormat.SpaceKeyValue,
            "Sem V-Sync, sem desfoque de movimento, granulação, aberração cromática, vinheta e distorção de lente. Qualidade gráfica e resolução continuam as suas.",
            new ConfigValue[]
            {
                new("", "GstRender.VSyncMode", "0"), new("", "GstRender.MotionBlurWeapon", "0.000000"), new("", "GstRender.MotionBlurWorld", "0.000000"),
                new("", "GstRender.FilmGrain", "0"), new("", "GstRender.ChromaticAberration", "0"), new("", "GstRender.Vignette", "0"), new("", "GstRender.LensDistortion", "0"),
            },
            new[] { "Feche o Battlefield 6 antes de aplicar.", "Vale para o perfil que jogou por último neste PC." }),
        new("tarkov", "Escape from Tarkov", new[] { "EscapeFromTarkov" }, ConfigFormat.Json,
            "Sem desfoque de movimento, aberração cromática e ruído; oclusão de ambiente, reflexos de tela e sombra da grama desligados; sombras no baixo.",
            new ConfigValue[]
            {
                new("", "ZBlur", "false"), new("", "ChromaticAberrations", "false"), new("", "Noise", "false"), new("", "Ssao", "Off"),
                new("", "SSR", "Off"), new("", "GrassShadow", "false"), new("", "ShadowsQuality", "0"),
            },
            new[] { "Feche o Escape from Tarkov e o launcher antes de aplicar." }),
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
            "pubg" => UnrealUserSettings(local, "TslGame", "WindowsNoEditor", "WindowsClient"),
            "thefinals" => UnrealUserSettings(local, "Discovery", "WindowsClient", "Windows"),
            "marvelrivals" => UnrealUserSettings(local, "Marvel", "Windows", "WindowsClient"),
            "deadbydaylight" => UnrealUserSettings(local, "DeadByDaylight", "WindowsClient", "EGS", "WinGDK"),
            "overwatch" => Path.Combine(documents, "Overwatch", "Settings", "Settings_v0.ini"),
            "rainbowsix" => NewestFile(Path.Combine(documents, "My Games", "Rainbow Six - Siege"), "GameSettings.ini", depth: 1),
            "dota2" => SteamGameFile("dota 2 beta", "game", "dota", "cfg", "video.txt"),
            "deadlock" => SteamGameFile("Deadlock", "game", "citadel", "cfg", "video.txt"),
            "rust" => SteamGameFile("Rust", "cfg", "client.cfg"),
            "battlefield6" => NewestFile(Path.Combine(documents, "Battlefield 6", "settings"), "PROFSAVE_profile", depth: 2),
            "tarkov" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Battlestate Games", "Escape from Tarkov", "Settings", "Graphics.ini"),
            _ => null,
        };
        return path != null && File.Exists(path) ? path : null;
    }

    /// <summary>O arquivo com esse nome modificado por último dentro da pasta (até <paramref name="depth"/> subpastas).</summary>
    private static string? NewestFile(string root, string name, int depth)
    {
        if (!Directory.Exists(root)) return null;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = depth > 0, MaxRecursionDepth = depth, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            return Directory.EnumerateFiles(root, name, options).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        catch (IOException) { return null; }
    }

    /// <summary>Arquivo dentro da pasta de instalação de um jogo da Steam, em qualquer biblioteca (outros discos inclusive).</summary>
    private static string? SteamGameFile(string installFolder, params string[] relative) =>
        SteamLibraries().Select(lib => Path.Combine(new[] { lib, "steamapps", "common", installFolder }.Concat(relative).ToArray())).FirstOrDefault(File.Exists);

    /// <summary>Bibliotecas da Steam: a pasta principal e as listadas em libraryfolders.vdf.</summary>
    public static IEnumerable<string> SteamLibraries()
    {
        using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (steam?.GetValue("SteamPath") is not string steamPath) yield break;
        var main = steamPath.Replace('/', Path.DirectorySeparatorChar);
        yield return main;
        var vdf = Path.Combine(main, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        string text;
        try { text = File.ReadAllText(vdf); }
        catch (IOException) { yield break; }
        foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
        {
            var library = m.Groups[1].Value.Replace("\\\\", "\\");
            if (!library.Equals(main, StringComparison.OrdinalIgnoreCase)) yield return library;
        }
    }

    /// <summary>
    /// GameUserSettings.ini de um jogo Unreal em %LOCALAPPDATA%\&lt;pasta&gt;\Saved\Config\&lt;plataforma&gt;. A pasta da
    /// plataforma muda com a loja (Steam, Epic, Xbox); vale a que o jogo gravou por último.
    /// </summary>
    private static string? UnrealUserSettings(string local, string folder, params string[] platforms) =>
        platforms.Select(p => Path.Combine(local, folder, "Saved", "Config", p, "GameUserSettings.ini"))
            .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

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

    // ---------- Travar o arquivo (somente leitura) ----------
    private static IEnumerable<string> ExistingFiles(GamePreset preset) => preset.Files.Select(f => FilePath(preset, f)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Todos os arquivos do preset estão como somente leitura (o jogo não consegue regravá-los).</summary>
    public static bool IsLocked(GamePreset preset)
    {
        var files = ExistingFiles(preset).ToList();
        return files.Count > 0 && files.All(f => File.GetAttributes(f).HasFlag(FileAttributes.ReadOnly));
    }

    /// <summary>
    /// Alguns jogos (Dota 2, Dead by Daylight...) regravam o arquivo ao abrir e desfazem o preset. Travado, o jogo
    /// só lê; em troca, o que for mudado no menu do jogo não fica salvo até destravar.
    /// </summary>
    public void SetLocked(GamePreset preset, bool locked)
    {
        if (IsRunning(preset)) throw new InvalidOperationException($"Feche o {preset.Name} antes: ele regrava as configurações ao fechar.");
        foreach (var file in ExistingFiles(preset)) SetReadOnly(file, locked);
        _log.Write("SUCCESS", $"Configurações do {preset.Name}: arquivo {(locked ? "travado (somente leitura)" : "destravado")}");
    }

    private static void SetReadOnly(string path, bool readOnly)
    {
        var attributes = File.GetAttributes(path);
        File.SetAttributes(path, readOnly ? attributes | FileAttributes.ReadOnly : attributes & ~FileAttributes.ReadOnly);
    }

    // ---------- Aplicar e restaurar ----------
    public int Apply(GamePreset preset, string? profileId = null)
    {
        if (ConfigPath(preset) is null) throw new FileNotFoundException($"Arquivo de configuração do {preset.Name} não encontrado. Abra o jogo uma vez e feche para ele criar o arquivo.");
        if (IsRunning(preset)) throw new InvalidOperationException($"Feche o {preset.Name} antes de aplicar: ele regrava as configurações ao fechar.");
        // Reaplicar num arquivo travado precisa destravar para gravar; a trava volta no fim
        var relock = HasBackup(preset) && IsLocked(preset);
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
            var (updated, count) = preset.Format switch
            {
                ConfigFormat.Ini => SetIniValues(text, group, newline),
                ConfigFormat.IniQuoted => SetIniValues(text, group, newline, quoted: true),
                ConfigFormat.SpaceKeyValue => SetSpacedValues(text, group, newline),
                ConfigFormat.Json => SetJsonValues(text, group),
                _ => SetQuotedValues(text, group, newline),
            };
            // O arquivo pode estar como somente leitura (truque comum de guias); o preset precisa gravar
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            File.WriteAllText(path, updated, new UTF8Encoding(false));
            changes += count;
        }
        if (relock) foreach (var file in ExistingFiles(preset)) SetReadOnly(file, true);
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
            // O original volta destravado: File.Copy não sobrescreve arquivo somente leitura
            SetReadOnly(path, false);
            File.Copy(backup, path, overwrite: true);
            SetReadOnly(path, false);
            // A cópia herda o somente leitura de um original travado por guias; File.Delete recusaria
            SetReadOnly(backup, false);
            File.Delete(backup);
            restored++;
        }
        if (restored == 0) throw new FileNotFoundException("Não há cópia original deste jogo.");
        if (File.Exists(ProfileMarker(preset))) File.Delete(ProfileMarker(preset));
        _log.Write("SUCCESS", $"Configurações originais restauradas: {preset.Name}");
    }

    /// <summary>
    /// Troca (ou acrescenta) chaves em arquivos .ini, respeitando as seções. <paramref name="quoted"/>: formato
    /// <c>Chave = "valor"</c> (Overwatch).
    /// </summary>
    public static (string Text, int Changes) SetIniValues(string text, IEnumerable<ConfigValue> values, string newline = "\r\n", bool quoted = false)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var changes = 0;
        string Line(string key, string value) => quoted ? $"{key} = \"{value}\"" : $"{key}={value}";
        foreach (var v in values)
        {
            var header = lines.FindIndex(l => l.Trim().Equals($"[{v.Section}]", StringComparison.OrdinalIgnoreCase));
            if (header < 0)
            {
                if (lines.Count > 0 && lines[^1].Length > 0) lines.Add("");
                lines.Add($"[{v.Section}]"); lines.Add(Line(v.Key, v.Value));
                changes++; continue;
            }
            var end = lines.FindIndex(header + 1, l => l.TrimStart().StartsWith('['));
            if (end < 0) end = lines.Count;
            var at = lines.FindIndex(header + 1, end - header - 1, l => l.Split('=')[0].Trim().Equals(v.Key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                var left = lines[at].Split('=')[0];
                var updated = quoted ? Line(left.Trim(), v.Value) : $"{left}={v.Value}";
                if (lines[at] != updated) { lines[at] = updated; changes++; }
            }
            else
            {
                // Insere antes das linhas em branco do fim da seção
                var insert = end;
                while (insert > header + 1 && string.IsNullOrWhiteSpace(lines[insert - 1])) insert--;
                lines.Insert(insert, Line(v.Key, v.Value));
                changes++;
            }
        }
        return (string.Join(newline, lines), changes);
    }

    private static readonly Regex SpacedLine = new("^(?<lead>\\s*)(?<key>[A-Za-z0-9_.]+)(?<gap>\\s+)(?<q>\"?)(?<value>[^\"\\r\\n]*?)\\k<q>(?<rest>\\s*(?://.*)?)$");

    /// <summary>
    /// Troca (ou acrescenta) linhas <c>chave valor</c>: Rust (<c>graphics.vsync "0"</c>) e Battlefield
    /// (<c>GstRender.VSyncMode 0</c>). Mantém as aspas como o arquivo já usa.
    /// </summary>
    public static (string Text, int Changes) SetSpacedValues(string text, IEnumerable<ConfigValue> values, string newline = "\r\n")
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var matches = lines.Select(l => SpacedLine.Match(l)).Where(m => m.Success).ToList();
        var fileQuotes = matches.Count > 0 && matches.Count(m => m.Groups["q"].Value.Length > 0) * 2 >= matches.Count;
        var changes = 0;
        foreach (var v in values)
        {
            var at = lines.FindIndex(l => SpacedLine.Match(l) is { Success: true } m && m.Groups["key"].Value.Equals(v.Key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                var m = SpacedLine.Match(lines[at]);
                if (m.Groups["value"].Value == v.Value) continue;
                var q = m.Groups["q"].Value;
                lines[at] = $"{m.Groups["lead"].Value}{m.Groups["key"].Value}{m.Groups["gap"].Value}{q}{v.Value}{q}{m.Groups["rest"].Value}";
            }
            else
            {
                var insert = lines.Count;
                while (insert > 0 && string.IsNullOrWhiteSpace(lines[insert - 1])) insert--;
                lines.Insert(insert, fileQuotes ? $"{v.Key} \"{v.Value}\"" : $"{v.Key} {v.Value}");
            }
            changes++;
        }
        return (string.Join(newline, lines), changes);
    }

    /// <summary>
    /// Troca (ou acrescenta) propriedades de primeiro nível num JSON (Graphics.ini do Tarkov), mantendo o tipo:
    /// "true"/"false" viram booleanos e números continuam números.
    /// </summary>
    public static (string Text, int Changes) SetJsonValues(string text, IEnumerable<ConfigValue> values)
    {
        if (System.Text.Json.Nodes.JsonNode.Parse(text) is not System.Text.Json.Nodes.JsonObject root)
            throw new InvalidDataException("O arquivo de configuração não está no formato esperado.");
        var changes = 0;
        foreach (var v in values)
        {
            System.Text.Json.Nodes.JsonNode node = v.Value switch
            {
                "true" or "false" => System.Text.Json.Nodes.JsonValue.Create(v.Value == "true"),
                _ when double.TryParse(v.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) =>
                    v.Value.Contains('.') ? System.Text.Json.Nodes.JsonValue.Create(number) : System.Text.Json.Nodes.JsonValue.Create((long)number),
                _ => System.Text.Json.Nodes.JsonValue.Create(v.Value),
            };
            if (root[v.Key]?.ToJsonString() == node.ToJsonString()) continue;
            root[v.Key] = node;
            changes++;
        }
        return (root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), changes);
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
