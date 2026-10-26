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
/// Connect (registro), Riot, Battle.net e EA app (entradas de desinstalação), Xbox/Game Pass (pastas XboxGames) e os
/// jogos adicionados à mão. Só lê arquivos e registro; abrir usa o próprio inicializador da loja, então conquistas,
/// nuvem e anti-cheat continuam funcionando. Battle.net, EA app e Xbox não expõem um link estável por jogo: "Jogar"
/// abre o aplicativo da loja, onde o jogo já está instalado.
/// </summary>
public static class GameLibraryService
{
    private static string CustomPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "library.json");

    public static List<LibraryGame> Scan()
    {
        var list = new List<LibraryGame>();
        foreach (var source in new Func<IEnumerable<LibraryGame>>[] { Steam, Epic, Gog, Ubisoft, Riot, BattleNet, EaApp, Xbox })
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

    /// <summary>Entradas de desinstalação (do PC, 64 e 32 bits, e do usuário) que combinam: nome, pasta e o texto de desinstalação.</summary>
    private static IEnumerable<(string Key, string Name, string Dir, string Uninstall)> UninstallEntries(Func<string, string, bool> match)
    {
        foreach (var (hive, path) in new[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"), (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        })
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) continue;
            foreach (var id in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(id);
                if (key?.GetValue("DisplayName") is not string name || key.GetValue("InstallLocation") is not string dir) continue;
                dir = dir.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
                var publisher = key.GetValue("Publisher") as string ?? "";
                var uninstall = key.GetValue("UninstallString") as string ?? "";
                if (dir.Length > 3 && Directory.Exists(dir) && match(publisher, uninstall)) yield return (id, name, dir, uninstall);
            }
        }
    }

    private static string? ExistingPath(params string[] candidates) => candidates.FirstOrDefault(File.Exists);

    /// <summary>
    /// Jogos da Riot (VALORANT, League of Legends...): o desinstalador é o Riot Client com --uninstall-product; o mesmo
    /// cliente abre o jogo com --launch-product, como os atalhos que a própria Riot cria.
    /// </summary>
    private static IEnumerable<LibraryGame> Riot()
    {
        foreach (var (key, name, dir, uninstall) in UninstallEntries((_, uninstall) => uninstall.Contains("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase)))
        {
            if (ParseRiotUninstall(uninstall) is not { } riot || riot.Product.Equals("Riot_Client", StringComparison.OrdinalIgnoreCase) || !File.Exists(riot.Client)) continue;
            var args = "--launch-product=" + riot.Product + (riot.Patchline.Length > 0 ? " --launch-patchline=" + riot.Patchline : "");
            yield return new LibraryGame("Riot", key, name.Trim(), dir, riot.Client, null, args);
        }
    }

    /// <summary>Lê o comando de desinstalação da Riot: caminho do Riot Client, produto e patchline (só letras, números, _ . -).</summary>
    public static (string Client, string Product, string Patchline)? ParseRiotUninstall(string uninstall)
    {
        var m = Regex.Match(uninstall, "^\"(?<exe>[^\"]+RiotClientServices\\.exe)\"\\s+--uninstall-product=(?<product>[A-Za-z0-9_.-]+)(\\s+--uninstall-patchline=(?<patch>[A-Za-z0-9_.-]*))?", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups["exe"].Value, m.Groups["product"].Value, m.Groups["patch"].Value) : null;
    }

    private static IEnumerable<LibraryGame> BattleNet()
    {
        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var launcher = ExistingPath(Path.Combine(x86, "Battle.net", "Battle.net Launcher.exe"), Path.Combine(x86, "Battle.net", "Battle.net.exe"));
        if (launcher is null) yield break;
        // Os jogos da Blizzard se desinstalam pelo "Blizzard Uninstaller" do Battle.net, com --uid do produto
        foreach (var (key, name, dir, _) in UninstallEntries((publisher, uninstall) => uninstall.Contains("Blizzard Uninstaller", StringComparison.OrdinalIgnoreCase) && uninstall.Contains("--uid=", StringComparison.OrdinalIgnoreCase)))
            yield return new LibraryGame("Battle.net", key, name, dir, LaunchUri: launcher);
    }

    private static IEnumerable<LibraryGame> EaApp()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var launcher = ExistingPath(Path.Combine(programFiles, "Electronic Arts", "EA Desktop", "EA Desktop", "EALauncher.exe"), Path.Combine(programFiles, "Electronic Arts", "EA Desktop", "EA Desktop", "EADesktop.exe"));
        if (launcher is null) yield break;
        // Jogos instalados pelo EA app: publicados pela EA, com o desinstalador do EAInstaller ou do EA app (o app em si fica de fora)
        foreach (var (key, name, dir, _) in UninstallEntries((publisher, uninstall) => publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase)
                     && (uninstall.Contains("EAInstaller", StringComparison.OrdinalIgnoreCase) || uninstall.Contains("EA Desktop", StringComparison.OrdinalIgnoreCase))))
            if (!name.StartsWith("EA app", StringComparison.OrdinalIgnoreCase)) yield return new LibraryGame("EA app", key, name, dir, LaunchUri: launcher);
    }

    /// <summary>Jogos do app Xbox/Game Pass: pastas "XboxGames" na raiz dos discos, com o MicrosoftGame.config de cada jogo.</summary>
    private static IEnumerable<LibraryGame> Xbox()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            string root;
            try { if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue; root = Path.Combine(drive.RootDirectory.FullName, "XboxGames"); }
            catch (IOException) { continue; }
            if (!Directory.Exists(root)) continue;
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                var config = Path.Combine(folder, "Content", "MicrosoftGame.config");
                if (!File.Exists(config)) continue;
                string? name = null;
                try { name = Regex.Match(File.ReadAllText(config), "DefaultDisplayName=\"([^\"]+)\"") is { Success: true } m ? m.Groups[1].Value : null; }
                catch (IOException) { }
                // Nomes "ms-resource:" vêm do pacote: a pasta tem o nome legível
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileName(folder);
                yield return new LibraryGame("Xbox", folder, name, folder, LaunchUri: XboxAppUri);
            }
        }
    }

    /// <summary>Abre o app Xbox (o Explorer resolve o shell:AppsFolder sem elevação).</summary>
    public const string XboxAppUri = "shell:AppsFolder\\Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";

    /// <summary>"Jogar" desta loja abre o aplicativo da loja, não o jogo direto.</summary>
    public static bool OpensStoreApp(LibraryGame game) => game.Store is "Battle.net" or "EA app" or "Xbox";

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

    /// <summary>
    /// O perfil é deste jogo: algum processo do perfil existe na pasta do jogo (o .exe aberto pode ser um
    /// inicializador, como o Riot Client, e não o processo que o perfil observa).
    /// </summary>
    public static bool BelongsTo(LibraryGame game, IReadOnlyCollection<string> processes)
    {
        var wanted = processes.Where(Engine.ProfileStore.IsGameTrigger).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return false;
        if (game.ExePath is { } exe && wanted.Contains(Path.GetFileName(exe)) && !game.Store.Equals("Riot", StringComparison.Ordinal)) return true;
        try
        {
            return Directory.Exists(game.InstallDir) && Directory.EnumerateFiles(game.InstallDir, "*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true })
                .Any(f => wanted.Contains(Path.GetFileName(f)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
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
