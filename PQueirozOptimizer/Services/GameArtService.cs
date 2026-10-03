using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Capas dos jogos para os cartões e perfis. Ordem: imagem que a própria Steam já guardou neste PC,
/// download do CDN da Steam (com cache local) e, para executáveis fora da Steam, o ícone do programa.
/// </summary>
public static class GameArtService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Dictionary<string, BitmapSource?> Memory = new();
    private static readonly string CacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "GameArt");

    /// <summary>ID da Steam dos jogos com preset (o Fortnite só existe na Epic).</summary>
    public static int? SteamAppId(string presetId) => presetId switch { "cs2" => 730, "apex" => 1172470, "rocketleague" => 252950, _ => null };

    public static string? SteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return (key?.GetValue("SteamPath") as string)?.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>Banner largo do jogo da Steam (library_hero), ou o cabeçalho da loja.</summary>
    public static async Task<BitmapSource?> SteamBannerAsync(int appId)
    {
        var key = "steam-" + appId;
        if (Memory.TryGetValue(key, out var cached)) return cached;
        BitmapSource? image = null;
        if (SteamPath() is { } steam)
        {
            var local = Path.Combine(steam, "appcache", "librarycache", appId.ToString());
            foreach (var name in new[] { "library_hero.jpg", "header.jpg" })
                if (File.Exists(Path.Combine(local, name))) { image = Load(Path.Combine(local, name)); break; }
            image ??= File.Exists(Path.Combine(steam, "appcache", "librarycache", $"{appId}_library_hero.jpg")) ? Load(Path.Combine(steam, "appcache", "librarycache", $"{appId}_library_hero.jpg")) : null;
        }
        image ??= await DownloadAsync($"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_hero.jpg", $"{appId}_hero.jpg")
                  ?? await DownloadAsync($"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg", $"{appId}_header.jpg");
        Memory[key] = image;
        return image;
    }

    /// <summary>Capa do jogo de um preset: Steam quando existe lá; o Fortnite usa o ícone da instalação da Epic.</summary>
    public static async Task<BitmapSource?> PresetArtAsync(string presetId)
    {
        if (SteamAppId(presetId) is { } appId) return await SteamBannerAsync(appId);
        if (presetId == "fortnite")
        {
            // Arte oficial da página do Fortnite na Epic Games Store; sem internet, o ícone da instalação
            if (await EpicStoreBannerAsync("fortnite") is { } art) return art;
            if (EpicInstallLocation("Fortnite") is { } dir)
                return ExecutableIcon(Path.Combine(dir, "FortniteGame", "Binaries", "Win64", "FortniteClient-Win64-Shipping.exe"));
        }
        return null;
    }

    /// <summary>
    /// Imagem de destaque da página do jogo na Epic Games Store (a mesma do launcher). Só aceita imagens do
    /// CDN da própria Epic; o arquivo fica em cache e é renovado a cada 7 dias (a arte muda a cada temporada).
    /// </summary>
    public static async Task<BitmapSource?> EpicStoreBannerAsync(string slug)
    {
        var key = "epic-" + slug;
        if (Memory.TryGetValue(key, out var cached)) return cached;
        var file = Path.Combine(CacheDirectory, $"epic_{slug}.jpg");
        BitmapSource? image = null;
        if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromDays(7)) image = Load(file);
        if (image is null)
        {
            try
            {
                var json = await Http.GetStringAsync($"https://store-content-ipv4.ak.epicgames.com/api/pt-BR/content/products/{Uri.EscapeDataString(slug)}");
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var hero = doc.RootElement.GetProperty("pages")[0].GetProperty("data").GetProperty("hero");
                var url = hero.TryGetProperty("backgroundImageUrl", out var bg) ? bg.GetString() : null;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.EndsWith(".unrealengine.com", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = await Http.GetByteArrayAsync(uri);
                    Directory.CreateDirectory(CacheDirectory);
                    await File.WriteAllBytesAsync(file, bytes);
                    image = Load(file);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                                           or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                // Sem internet: usa a cópia antiga, mesmo vencida
                image = File.Exists(file) ? Load(file) : null;
            }
        }
        Memory[key] = image;
        return image;
    }

    /// <summary>Pasta de um jogo instalado pela Epic (lista do Epic Games Launcher).</summary>
    public static string? EpicInstallLocation(string appName)
    {
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
        try
        {
            if (!File.Exists(file)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            foreach (var item in doc.RootElement.GetProperty("InstallationList").EnumerateArray())
                if (item.GetProperty("AppName").GetString() == appName) return item.GetProperty("InstallLocation").GetString();
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException) { }
        return null;
    }

    /// <summary>Capa de um executável: banner da Steam quando o jogo é da Steam, senão o ícone do programa.</summary>
    public static async Task<BitmapSource?> ExecutableArtAsync(string exePath)
    {
        if (SteamAppIdFromPath(exePath) is { } appId && await SteamBannerAsync(appId) is { } banner) return banner;
        return ExecutableIcon(exePath);
    }

    /// <summary>Descobre o jogo da Steam pela pasta (steamapps\common\&lt;pasta&gt;) e o appmanifest correspondente.</summary>
    public static int? SteamAppIdFromPath(string exePath)
    {
        var m = Regex.Match(exePath, @"^(?<lib>.+?\\steamapps)\\common\\(?<dir>[^\\]+)", RegexOptions.IgnoreCase);
        if (!m.Success || !Directory.Exists(m.Groups["lib"].Value)) return null;
        foreach (var manifest in Directory.EnumerateFiles(m.Groups["lib"].Value, "appmanifest_*.acf"))
        {
            try
            {
                var text = File.ReadAllText(manifest);
                var dir = Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"");
                if (!dir.Success || !dir.Groups[1].Value.Equals(m.Groups["dir"].Value, StringComparison.OrdinalIgnoreCase)) continue;
                var id = Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"");
                if (id.Success && int.TryParse(id.Groups[1].Value, out var appId)) return appId;
            }
            catch (IOException) { }
        }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string iconFile, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

    /// <summary>Ícone do executável em 256 px (o tamanho grande que o Explorer usa).</summary>
    public static BitmapSource? ExecutableIcon(string exePath)
    {
        if (!File.Exists(exePath)) return null;
        if (SHDefExtractIcon(exePath, 0, 0, out var large, out var small, (16u << 16) | 256u) != 0 || large == IntPtr.Zero) return null;
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(large, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException) { return null; }
        finally { DestroyIcon(large); if (small != IntPtr.Zero) DestroyIcon(small); }
    }

    private static BitmapSource? Load(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // não mantém o arquivo da Steam aberto
            image.DecodePixelWidth = 900;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or FileFormatException) { return null; }
    }

    private static async Task<BitmapSource?> DownloadAsync(string url, string fileName)
    {
        var path = Path.Combine(CacheDirectory, fileName);
        if (File.Exists(path)) return Load(path);
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllBytesAsync(path, bytes);
            return Load(path);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { return null; }
    }
}
