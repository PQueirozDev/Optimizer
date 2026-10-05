using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PQueirozOptimizer.BiosAdvisor.Updates;

public enum BiosUpdateStatus { NotVerified, UpToDate, UpdateAvailable, Newer }

/// <summary>
/// Resultado da verificação. Sem fonte confiável o status é NotVerified e a página mostra só a versão instalada.
/// O link é sempre a página oficial da fabricante; o app nunca baixa nem grava BIOS.
/// </summary>
public sealed record BiosUpdateResult(BiosUpdateStatus Status, string Installed, string? Latest, DateTime? LatestDate, string? OfficialUrl, string? Source, LocalizedText? Note = null);

public interface IBiosUpdateProvider
{
    bool Supports(HardwareProfile profile);
    Task<BiosUpdateResult> CheckAsync(HardwareProfile profile, CancellationToken token);
}

/// <summary>Escolhe o provedor da fabricante; sem provedor, devolve só a versão instalada e o suporte oficial.</summary>
public static class BiosUpdateCheck
{
    public static IReadOnlyList<IBiosUpdateProvider> Providers { get; } = new IBiosUpdateProvider[] { new AsusBiosUpdateProvider() };

    public static async Task<BiosUpdateResult> CheckAsync(HardwareProfile profile, CancellationToken token)
    {
        var url = VendorCatalog.For(profile.Motherboard).SupportUrl(profile.Motherboard);
        var provider = Providers.FirstOrDefault(p => p.Supports(profile));
        if (provider is null) return new BiosUpdateResult(BiosUpdateStatus.NotVerified, profile.Bios.Version, null, null, url, null);
        try { return await provider.CheckAsync(profile, token); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException)
        {
            if (token.IsCancellationRequested) throw;
            return new BiosUpdateResult(BiosUpdateStatus.NotVerified, profile.Bios.Version, null, null, url, null,
                new("Não foi possível consultar o site da fabricante agora.", "Couldn't reach the manufacturer's website right now."));
        }
    }

    /// <summary>Compara versões numéricas ("0708" &lt; "2003"; "F12" &lt; "F15"). Null quando o formato não é comparável.</summary>
    public static int? Compare(string installed, string latest)
    {
        // Formato inteiro: prefixo de letras opcional + número ("0708", "F12"). Sufixos (beta, "a") ou pontos = não comparável
        var a = Regex.Match((installed ?? "").Trim(), @"^([A-Za-z]*)(\d+)$");
        var b = Regex.Match((latest ?? "").Trim(), @"^([A-Za-z]*)(\d+)$");
        if (!a.Success || !b.Success || !a.Groups[1].Value.Equals(b.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) return null;
        return long.Parse(a.Groups[2].Value).CompareTo(long.Parse(b.Groups[2].Value));
    }
}

/// <summary>
/// ASUS: lista oficial de BIOS do suporte da ASUS (JSON de asus.com, o mesmo que a página de download usa).
/// Considera só versões marcadas como liberadas; qualquer formato inesperado vira "não verificado".
/// </summary>
public sealed class AsusBiosUpdateProvider : IBiosUpdateProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public bool Supports(HardwareProfile profile) => new AsusVendor().Matches(profile.Motherboard) && !string.IsNullOrWhiteSpace(profile.Motherboard.Product);

    public async Task<BiosUpdateResult> CheckAsync(HardwareProfile profile, CancellationToken token)
    {
        var product = profile.Motherboard.Product.Trim();
        var support = new AsusVendor().SupportUrl(profile.Motherboard);
        var api = $"https://www.asus.com/support/api/product.asmx/GetPDBIOS?website=global&model={Uri.EscapeDataString(product)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, api);
        request.Headers.UserAgent.ParseAdd("Qrztweaks-BiosAdvisor");
        using var response = await Http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(token);
        var latest = ParseLatest(json);
        if (latest is null)
            return new BiosUpdateResult(BiosUpdateStatus.NotVerified, profile.Bios.Version, null, null, support, "asus.com",
                new("A ASUS não retornou versões de BIOS para este modelo.", "ASUS returned no BIOS versions for this model."));
        var cmp = BiosUpdateCheck.Compare(profile.Bios.Version, latest.Value.Version);
        var status = cmp switch { < 0 => BiosUpdateStatus.UpdateAvailable, 0 => BiosUpdateStatus.UpToDate, > 0 => BiosUpdateStatus.Newer, _ => BiosUpdateStatus.NotVerified };
        return new BiosUpdateResult(status, profile.Bios.Version, latest.Value.Version, latest.Value.Date, support, "asus.com");
    }

    /// <summary>Maior versão liberada da categoria BIOS no JSON da ASUS.</summary>
    public static (string Version, DateTime? Date)? ParseLatest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Result", out var result) || result.ValueKind != JsonValueKind.Object) return null;
        if (!result.TryGetProperty("Obj", out var groups) || groups.ValueKind != JsonValueKind.Array) return null;
        (string Version, DateTime? Date, long Number)? best = null;
        foreach (var group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("Name", out var name) || name.GetString() != "BIOS") continue;
            if (!group.TryGetProperty("Files", out var files) || files.ValueKind != JsonValueKind.Array) continue;
            foreach (var file in files.EnumerateArray())
            {
                var version = file.TryGetProperty("Version", out var v) ? v.GetString() : null;
                var released = file.TryGetProperty("IsRelease", out var r) && r.ToString() == "1";
                var m = Regex.Match(version ?? "", @"^\d+$");
                if (!released || !m.Success) continue;
                DateTime? date = file.TryGetProperty("ReleaseDate", out var d) && DateTime.TryParse(d.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt) ? dt : null;
                var number = long.Parse(version!);
                if (best is null || number > best.Value.Number) best = (version!, date, number);
            }
        }
        return best is { } b ? (b.Version, b.Date) : null;
    }
}
