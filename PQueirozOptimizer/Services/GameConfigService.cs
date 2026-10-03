using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

public enum ConfigFormat { Ini, QuotedKeyValue }

/// <summary>Um valor do arquivo de configuração do jogo (seção vazia no formato "chave" "valor").</summary>
public sealed record ConfigValue(string Section, string Key, string Value);

/// <summary>Preset competitivo de um jogo: onde fica o arquivo e quais valores muda.</summary>
public sealed record GamePreset(string Id, string Name, string[] Processes, ConfigFormat Format, string Description, ConfigValue[] Values, string[] Notes);

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
            _ => null,
        };
        return path != null && File.Exists(path) ? path : null;
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

    private static string BackupPath(GamePreset preset, string configPath) => Path.Combine(BackupDirectory, preset.Id, "original" + Path.GetExtension(configPath));
    public static bool HasBackup(GamePreset preset) => ConfigPath(preset) is { } path && File.Exists(BackupPath(preset, path));

    // ---------- Aplicar e restaurar ----------
    public int Apply(GamePreset preset)
    {
        var path = ConfigPath(preset) ?? throw new FileNotFoundException($"Arquivo de configuração do {preset.Name} não encontrado. Abra o jogo uma vez e feche para ele criar o arquivo.");
        if (IsRunning(preset)) throw new InvalidOperationException($"Feche o {preset.Name} antes de aplicar: ele regrava as configurações ao fechar.");
        var backup = BackupPath(preset, path);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        if (!File.Exists(backup)) File.Copy(path, backup);

        var text = File.ReadAllText(path);
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var (updated, changes) = preset.Format == ConfigFormat.Ini ? SetIniValues(text, preset.Values, newline) : SetQuotedValues(text, preset.Values, newline);
        // O arquivo pode estar como somente leitura (truque comum de guias); o preset precisa gravar
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        File.WriteAllText(path, updated, new UTF8Encoding(false));
        _log.Write("SUCCESS", $"Preset competitivo aplicado: {preset.Name} ({changes} valores)");
        return changes;
    }

    public void Restore(GamePreset preset)
    {
        var path = ConfigPath(preset) ?? throw new FileNotFoundException($"Arquivo de configuração do {preset.Name} não encontrado.");
        if (IsRunning(preset)) throw new InvalidOperationException($"Feche o {preset.Name} antes de restaurar.");
        var backup = BackupPath(preset, path);
        if (!File.Exists(backup)) throw new FileNotFoundException("Não há cópia original deste jogo.");
        File.Copy(backup, path, overwrite: true);
        File.Delete(backup);
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
