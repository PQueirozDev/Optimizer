using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.IO;

namespace PQueirozOptimizer.Services;

public sealed record UpdateInfo(bool IsAvailable, string CurrentVersion, string LatestVersion, string? DownloadUrl, string? AssetUrl, string? AssetName);

public sealed class UpdateService
{
    private static readonly HttpClient Client = CreateClient();
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PQueirozOptimizer", "1.0"));
        return client;
    }

    public async Task<UpdateInfo> CheckAsync(string currentVersion, CancellationToken token = default)
    {
        var current = Normalize(currentVersion);
        using var response = await Client.GetAsync("https://api.github.com/repos/PQueirozDev/Optimizer/releases/latest", token);
        if (!response.IsSuccessStatusCode) return new(false, current, current, null, null, null);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var tag = json.RootElement.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag)) return new(false, current, current, null, null, null);
        var latest = Normalize(tag);
        var url = json.RootElement.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
        string? assetUrl = null, assetName = null;
        if (json.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                {
                    assetName = name;
                    assetUrl = asset.TryGetProperty("browser_download_url", out var a) ? a.GetString() : null;
                    break;
                }
            }
        }
        return new(Compare(latest, current) > 0, current, latest, url, assetUrl, assetName);
    }

    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<(long read, long total)>? progress = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(update.AssetUrl)) throw new InvalidOperationException("A release não possui instalador disponível.");
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, string.IsNullOrWhiteSpace(update.AssetName) ? $"PQueirozOptimizer-Setup-v{update.LatestVersion}.exe" : update.AssetName);
        using var response = await Client.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920]; long read = 0; int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count), token); read += count; progress?.Report((read, total));
        }
        return path;
    }

    private static string Normalize(string value) => value.Trim().TrimStart('v', 'V');
    private static int Compare(string left, string right)
    {
        if (Version.TryParse(left, out var l) && Version.TryParse(right, out var r)) return l.CompareTo(r);
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
