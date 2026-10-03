using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>Ajuste de um aplicativo: o que faz, se o app está instalado e se já foi aplicado.</summary>
public sealed record AppTweak(string Id, string App, string Title, string Description, string[] Processes);

/// <summary>Programa instalado (área de trabalho ou Microsoft Store).</summary>
public sealed record InstalledApp(string Name, string Publisher, string Version, bool IsStore, string Uninstall, string? Package);

/// <summary>
/// Otimizador de apps: desliga a aceleração por hardware e a execução em segundo plano de Discord, navegadores e
/// Spotify (eles disputam a GPU e a CPU com o jogo). Tudo reversível: arquivos de configuração são copiados antes
/// e as políticas de navegador usam o armazenamento de ajustes reversíveis.
/// </summary>
public sealed class AppOptimizerService
{
    private readonly ActivityLog _log;
    private readonly RegistryTweakStore _tweaks;
    private static readonly string BackupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "AppConfigs");

    public AppOptimizerService(ActivityLog log, RegistryTweakStore? tweaks = null) { _log = log; _tweaks = tweaks ?? new RegistryTweakStore(); }

    private static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string DiscordSettings = Path.Combine(Roaming, "discord", "settings.json");
    private static readonly string SpotifyPrefs = Path.Combine(Roaming, "Spotify", "prefs");

    private static readonly (string Id, string App, string PolicyKey, string Exe)[] Browsers =
    {
        ("chrome", "Google Chrome", @"SOFTWARE\Policies\Google\Chrome", @"Google\Chrome\Application\chrome.exe"),
        ("edge", "Microsoft Edge", @"SOFTWARE\Policies\Microsoft\Edge", @"Microsoft\Edge\Application\msedge.exe"),
        ("brave", "Brave", @"SOFTWARE\Policies\BraveSoftware\Brave", @"BraveSoftware\Brave-Browser\Application\brave.exe"),
    };

    public static readonly AppTweak[] Tweaks =
    {
        new("discord-gpu", "Discord", "Desligar a aceleração por hardware", "O Discord deixa de usar a placa de vídeo para desenhar a janela, liberando a GPU para o jogo.", new[] { "Discord" }),
        new("spotify-gpu", "Spotify", "Desligar a aceleração por hardware", "O Spotify deixa de usar a placa de vídeo, que fica livre para o jogo.", new[] { "Spotify" }),
        new("chrome-bg", "Google Chrome", "Não rodar em segundo plano", "O Chrome fecha de verdade ao fechar a janela, em vez de continuar rodando escondido.", new[] { "chrome" }),
        new("edge-bg", "Microsoft Edge", "Não rodar em segundo plano nem pré-carregar", "Desliga o modo em segundo plano e a Inicialização Rápida, que deixa o Edge aberto desde o login.", new[] { "msedge" }),
        new("brave-bg", "Brave", "Não rodar em segundo plano", "O Brave fecha de verdade ao fechar a janela.", new[] { "brave" }),
    };

    public static bool IsInstalled(AppTweak t) => t.Id switch
    {
        "discord-gpu" => File.Exists(DiscordSettings),
        "spotify-gpu" => File.Exists(SpotifyPrefs),
        _ => Browsers.First(b => t.Id.StartsWith(b.Id)) is var b && new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }.Any(r => File.Exists(Path.Combine(r, b.Exe))),
    };

    public bool IsApplied(AppTweak t) => t.Id switch
    {
        "discord-gpu" or "spotify-gpu" => File.Exists(Path.Combine(BackupDirectory, t.Id + ".bak")),
        _ => _tweaks.IsApplied("app-" + t.Id),
    };

    public static bool IsRunning(AppTweak t) =>
        t.Processes.Any(n => { var p = Process.GetProcessesByName(n); foreach (var x in p) x.Dispose(); return p.Length > 0; });

    public void Apply(AppTweak t)
    {
        if (t.Id is "discord-gpu" or "spotify-gpu" && IsRunning(t)) throw new InvalidOperationException($"Feche o {t.App} antes: ele regrava as configurações ao fechar.");
        switch (t.Id)
        {
            case "discord-gpu":
                Backup(t.Id, DiscordSettings);
                var json = JsonNode.Parse(File.ReadAllText(DiscordSettings)) as JsonObject ?? new JsonObject();
                json["enableHardwareAcceleration"] = false;
                File.WriteAllText(DiscordSettings, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                break;
            case "spotify-gpu":
                Backup(t.Id, SpotifyPrefs);
                var lines = File.ReadAllLines(SpotifyPrefs).Where(l => !l.StartsWith("ui.hardware_acceleration=", StringComparison.Ordinal)).ToList();
                lines.Add("ui.hardware_acceleration=false");
                File.WriteAllLines(SpotifyPrefs, lines);
                break;
            default:
                var browser = Browsers.First(b => t.Id.StartsWith(b.Id));
                var writes = new List<RegistryWrite> { new(RegistryHive.LocalMachine, browser.PolicyKey, "BackgroundModeEnabled", RegistryValueKind.DWord, 0) };
                if (browser.Id == "edge") writes.Add(new(RegistryHive.LocalMachine, browser.PolicyKey, "StartupBoostEnabled", RegistryValueKind.DWord, 0));
                _tweaks.Apply("app-" + t.Id, $"{t.App}: {t.Title}", writes);
                break;
        }
        _log.Write("SUCCESS", $"App otimizado: {t.App} — {t.Title}");
    }

    public void Revert(AppTweak t)
    {
        switch (t.Id)
        {
            case "discord-gpu": RestoreBackup(t, DiscordSettings); break;
            case "spotify-gpu": RestoreBackup(t, SpotifyPrefs); break;
            default:
                var browser = Browsers.First(b => t.Id.StartsWith(b.Id));
                _tweaks.Revert("app-" + t.Id, (hive, key, name) => hive == RegistryHive.LocalMachine && key.Equals(browser.PolicyKey, StringComparison.OrdinalIgnoreCase) && name is "BackgroundModeEnabled" or "StartupBoostEnabled");
                break;
        }
        _log.Write("SUCCESS", $"Ajuste do app revertido: {t.App} — {t.Title}");
    }

    private static void Backup(string id, string file)
    {
        Directory.CreateDirectory(BackupDirectory);
        var target = Path.Combine(BackupDirectory, id + ".bak");
        if (!File.Exists(target)) File.Copy(file, target);
    }

    private static void RestoreBackup(AppTweak t, string file)
    {
        if (IsRunning(t)) throw new InvalidOperationException($"Feche o {t.App} antes de restaurar.");
        var backup = Path.Combine(BackupDirectory, t.Id + ".bak");
        if (!File.Exists(backup)) return;
        File.Copy(backup, file, overwrite: true);
        File.Delete(backup);
    }

    // ================= Programas instalados =================
    public static List<InstalledApp> DesktopApps()
    {
        var list = new List<InstalledApp>();
        var sources = new[]
        {
            (Registry.LocalMachine, RegistryView.Registry64), (Registry.LocalMachine, RegistryView.Registry32), (Registry.CurrentUser, RegistryView.Default),
        };
        foreach (var (hive, view) in sources)
        {
            using var root = RegistryKey.OpenBaseKey(hive == Registry.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, view);
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (key is null) continue;
            foreach (var name in key.GetSubKeyNames())
            {
                using var app = key.OpenSubKey(name);
                if (app?.GetValue("DisplayName") is not string display || string.IsNullOrWhiteSpace(display)) continue;
                if (app.GetValue("SystemComponent") is 1 || app.GetValue("ParentKeyName") != null) continue;
                var uninstall = app.GetValue("QuietUninstallString") as string ?? app.GetValue("UninstallString") as string;
                if (string.IsNullOrWhiteSpace(uninstall) || Regex.IsMatch(display, @"^(Update for|Security Update|Hotfix)", RegexOptions.IgnoreCase)) continue;
                if (list.Any(a => a.Name.Equals(display, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(new InstalledApp(display.Trim(), (app.GetValue("Publisher") as string ?? "").Trim(), (app.GetValue("DisplayVersion") as string ?? "").Trim(), false, uninstall, null));
            }
        }
        return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static async Task<List<InstalledApp>> StoreAppsAsync()
    {
        var json = await PowerShellBridge.RunScriptAsync(
            "@(Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and $_.SignatureKind -eq 'Store' } | ForEach-Object { [pscustomobject]@{ N = $_.Name; V = [string]$_.Version; P = $_.PackageFullName; Pub = $_.Publisher } }) | ConvertTo-Json -Compress",
            timeout: TimeSpan.FromSeconds(60));
        var list = new List<InstalledApp>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
        foreach (var e in items)
        {
            var name = e.GetProperty("N").GetString() ?? "";
            var publisher = Regex.Match(e.GetProperty("Pub").GetString() ?? "", @"CN=([^,]+)").Groups[1].Value;
            list.Add(new InstalledApp(name, publisher, e.GetProperty("V").GetString() ?? "", true, "", e.GetProperty("P").GetString()));
        }
        return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Desinstala: apps da Loja pelo PowerShell; programas abrem o desinstalador do próprio fabricante.</summary>
    public async Task UninstallAsync(InstalledApp app)
    {
        if (app.IsStore)
            await PowerShellBridge.RunScriptAsync("Remove-AppxPackage -Package $env:PQO_PKG", new Dictionary<string, string> { ["PQO_PKG"] = app.Package! }, TimeSpan.FromMinutes(5));
        else
            await Task.Run(() =>
            {
                // O comando vem do registro do próprio programa (como faz o Painel de Controle)
                using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + app.Uninstall + "\"") { UseShellExecute = false, CreateNoWindow = true });
                p?.WaitForExit(30 * 60 * 1000);
            });
        _log.Write("SUCCESS", $"Desinstalação iniciada: {app.Name}");
    }
}
