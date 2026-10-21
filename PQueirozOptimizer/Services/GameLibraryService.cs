using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>Um jogo instalado, de qualquer loja, e como abri-lo.</summary>
public sealed record LibraryGame(string Store, string Id, string Name, string InstallDir, string? ExePath = null, string? LaunchUri = null, string Args = "")
{
    public bool IsManual => Store == "Manual";
}

/// <summary>Jogo adicionado à mão (qualquer .exe).</summary>
public sealed record CustomGame(string Name, string Exe, string Args = "");

/// <summary>
/// Biblioteca única dos jogos instalados: Steam (appmanifest), Epic Games (manifestos .item), GOG Galaxy e Ubisoft
/// Connect (registro) e os jogos adicionados à mão. Só lê arquivos e registro; abrir usa o próprio inicializador
/// da loja, então conquistas, nuvem e anti-cheat continuam funcionando.
/// </summary>
public static class GameLibraryService
{
    private static string CustomPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "library.json");

    public static List<LibraryGame> Scan()
    {
        var list = new List<LibraryGame>();
        foreach (var source in new Func<IEnumerable<LibraryGame>>[] { Steam, Epic, Gog, Ubisoft })
        {
            try { list.AddRange(source()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException) { }
        }
        foreach (var c in LoadCustom().Where(c => File.Exists(c.Exe)))
            list.Add(new LibraryGame("Manual", c.Exe, string.IsNullOrWhiteSpace(c.Name) ? Path.GetFileNameWithoutExtension(c.Exe) : c.Name, Path.GetDirectoryName(c.Exe) ?? "", c.Exe, null, c.Args));
        return list.GroupBy(g => (g.Store, g.Id)).Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // Ferramentas que a Steam instala como "app" e não são jogos
    private static bool SteamTool(string appId, string name) =>
        appId == "228980" || name.StartsWith("Steamworks Common", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lê um appmanifest_*.acf: (appid, nome, pasta, instalado por completo).</summary>
    public static (string? AppId, string? Name, string? InstallDir, bool Installed) ParseAppManifest(string text)
    {
        string? Field(string key) => Regex.Match(text, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;
        // StateFlags: o bit 4 = totalmente instalado (sem ele, o jogo ainda está baixando)
        var installed = !long.TryParse(Field("StateFlags"), out var flags) || (flags & 4) != 0;
        return (Field("appid"), Field("name"), Field("installdir"), installed);
    }

    private static IEnumerable<LibraryGame> Steam()
    {
        foreach (var library in GameConfigService.SteamLibraries())
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                string text;
                try { text = File.ReadAllText(manifest); }
                catch (IOException) { continue; }
                var (appId, name, dir, installed) = ParseAppManifest(text);
                if (appId is null || name is null || dir is null || !installed || SteamTool(appId, name)) continue;
                var folder = Path.Combine(apps, "common", dir);
                if (Directory.Exists(folder)) yield return new LibraryGame("Steam", appId, name, folder, LaunchUri: "steam://rungameid/" + appId);
            }
        }
    }

    private static IEnumerable<LibraryGame> Epic()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(folder)) yield break;
        foreach (var file in Directory.EnumerateFiles(folder, "*.item"))
        {
            LibraryGame? game = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                string? S(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var (name, dir, app, ns, item, exe) = (S("DisplayName"), S("InstallLocation"), S("AppName"), S("CatalogNamespace"), S("CatalogItemId"), S("LaunchExecutable"));
                if (name != null && dir != null && app != null && Directory.Exists(dir))
                {
                    var exePath = string.IsNullOrEmpty(exe) ? null : Path.Combine(dir, exe);
                    game = new LibraryGame("Epic Games", app, name, dir, exePath != null && File.Exists(exePath) ? exePath : null,
                        ns != null && item != null ? $"com.epicgames.launcher://apps/{ns}%3A{item}%3A{app}?action=launch&silent=true" : null);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
            if (game != null) yield return game;
        }
    }

    private static IEnumerable<LibraryGame> Gog()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (root is null) yield break;
        foreach (var id in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(id);
            if (key?.GetValue("gameName") is not string name || key.GetValue("path") is not string dir || !Directory.Exists(dir)) continue;
            var exe = key.GetValue("exe") as string;
            yield return new LibraryGame("GOG", id, name, dir, exe != null && File.Exists(exe) ? exe : null, null, key.GetValue("launchParam") as string ?? "");
        }
    }

    private static IEnumerable<LibraryGame> Ubisoft()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (root is null) yield break;
        foreach (var id in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(id);
            var dir = (key?.GetValue("InstallDir") as string)?.Replace('/', '\\').TrimEnd('\\');
            if (dir is null || !Directory.Exists(dir)) continue;
            yield return new LibraryGame("Ubisoft Connect", id, Path.GetFileName(dir), dir, LaunchUri: $"uplay://launch/{id}/0");
        }
    }

    // ================= Jogos manuais =================
    public static List<CustomGame> LoadCustom()
    {
        try { return File.Exists(CustomPath) ? JsonSerializer.Deserialize<List<CustomGame>>(File.ReadAllText(CustomPath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public static void SaveCustom(List<CustomGame> games)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CustomPath)!);
        var temp = CustomPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(games, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, CustomPath, overwrite: true);
    }

    public static void AddCustom(string exe, string? name = null)
    {
        var games = LoadCustom();
        if (games.Any(g => g.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase))) return;
        games.Add(new CustomGame(name ?? Path.GetFileNameWithoutExtension(exe), exe));
        SaveCustom(games);
    }

    public static void RemoveCustom(string exe)
    {
        var games = LoadCustom();
        if (games.RemoveAll(g => g.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase)) > 0) SaveCustom(games);
    }

    /// <summary>Abre o jogo pela loja (URI) ou, sem ela, pelo executável na pasta do jogo.</summary>
    public static void Launch(LibraryGame game)
    {
        // O app roda como administrador; o jogo e a loja abrem pelo Explorer, com as permissões normais do usuário
        var target = game.LaunchUri ?? game.ExePath ?? throw new InvalidOperationException(Translator.IsEnglish ? "This game has no launcher or executable." : "Este jogo não tem inicializador nem executável.");
        if (game.LaunchUri is null && game.Args.Length > 0)
        {
            // Com argumentos (GOG): um atalho temporário guarda destino, argumentos e pasta, e o Explorer abre o atalho
            // sem elevação (abrir o .exe direto daqui faria o jogo herdar o administrador)
            var link = Path.Combine(Path.GetTempPath(), "Qrztweaks-launch-" + Math.Abs(game.Id.GetHashCode()) + ".lnk");
            ShortcutFile.Create(link, target, game.Args, game.InstallDir, target, game.Name);
            target = link;
        }
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + target + "\"") { UseShellExecute = false });
    }

    /// <summary>Executável principal provável (o maior .exe da pasta, fora instaladores e utilitários).</summary>
    public static string? GuessExecutable(LibraryGame game)
    {
        if (game.ExePath != null) return game.ExePath;
        try
        {
            return Directory.EnumerateFiles(game.InstallDir, "*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true })
                .Where(f => !Regex.IsMatch(Path.GetFileName(f), "(setup|unins|redist|vc_?redist|crash|report|launcher|helper|dxsetup|easyanticheat|battleye|ue4prereq)", RegexOptions.IgnoreCase))
                .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
