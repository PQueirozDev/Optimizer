using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Xml.Linq;

namespace PQueirozOptimizer.Services;

public sealed record UpdateInfo(bool IsAvailable, string CurrentVersion, string LatestVersion, string? DownloadUrl, string? AssetUrl, string? AssetName, string? ChecksumUrl, IReadOnlyList<string>? Notes = null, string? SignatureUrl = null);

/// <summary>Instalador baixado, validado e travado contra escrita até ser executado.</summary>
public sealed class VerifiedInstaller : IDisposable
{
    private readonly FileStream _lock;
    internal VerifiedInstaller(string path, FileStream lockHandle) { Path = path; _lock = lockHandle; }
    public string Path { get; }

    /// <summary>Executa o instalador sem interface; ele fecha o app e o reabre ao terminar.</summary>
    public void LaunchSilent()
    {
        var psi = new ProcessStartInfo(Path) { UseShellExecute = true, Verb = "runas" };
        psi.ArgumentList.Add("/VERYSILENT");
        psi.ArgumentList.Add("/SUPPRESSMSGBOXES");
        psi.ArgumentList.Add("/NORESTART");
        psi.ArgumentList.Add("/CLOSEAPPLICATIONS");
        // O handle continua aberto (somente leitura) até o processo ter sido criado.
        Process.Start(psi);
    }

    public void Dispose() => _lock.Dispose();
}

public sealed class UpdateService
{
    public const string ChecksumAssetName = "SHA256SUMS.txt";
    public const string SignatureAssetName = "SHA256SUMS.txt.sig";

    /// <summary>
    /// Chave pública de atualização (RSA 3072, só para releases; diferente da chave das licenças e do banco da BIOS).
    /// O SHA-256 prova que o instalador chegou inteiro; a assinatura do SHA256SUMS.txt prova quem o publicou.
    /// A chave privada fica fora do Git (tools/Sign-ReleaseChecksums.ps1) e no secret UPDATE_SIGNING_KEY do GitHub.
    /// </summary>
    internal const string UpdatePublicKeyBlob = "BgIAAACkAABSU0ExAAwAAAEAAQD1JhoUvzZNkiyMhiEgbo1I8wRDxM9cysEpxuulqGsOLSZnXli7nDl8HiGlcd1OuGLW+o5MaafYePc6okeytW/K6XUkvJXK6ghyJytcbD/76LbXKjYIj+YAgzaRevcyJcgt3RAFT2PByCjEMcZLd0p/SjYjX9TLlE1YtzFgWTliIu+tVotZh3q7MqruDdsF0jhtWVFpDc4aG0ajZBmBXJOUXXuTZPWaURVL+Q23TzRFUSPbXwWBYP3jDBAfrYv37+UMhvMUIuG4wOAGX+QdgmlQdneWXA3QF/GIP0DAssqjkQyuft6NSAkLV8HZ/lpYCyJ1TfgWOBL2DzRN674W6pz4fyoITZs9R/Lc2t8TKEIie//Ozph08ySbF0GBDoHEzqipX0FIUjxCUyJ+3qFd/DpWMZxsaY2mkTNgHVJNV7ZdyMiTsZRLqM3JY2fuvmPHH6IsSGeB6oGlmKGNikIDrbwH0BrrtIgQKzNQnl5k+UISQkuZxSJOgFjBxzqP+z9QUJw=";

    /// <summary>Confere a assinatura RSA-SHA256 dos bytes exatos do SHA256SUMS.txt.</summary>
    public static bool VerifyChecksumSignature(byte[] sums, string signatureBase64, string publicKeyBlob = UpdatePublicKeyBlob)
    {
        try
        {
            using var rsa = new RSACryptoServiceProvider();
            rsa.ImportCspBlob(Convert.FromBase64String(publicKeyBlob));
            return rsa.VerifyData(sums, CryptoConfig.MapNameToOID("SHA256")!, Convert.FromBase64String(signatureBase64.Trim()));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException) { return false; }
    }
    private static readonly HttpClient Client = CreateClient();
    private static readonly string UpdatesFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PQueirozOptimizer", "Updates");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PQueirozOptimizer", version));
        return client;
    }

    public async Task<UpdateInfo> CheckAsync(string currentVersion, CancellationToken token = default)
    {
        var current = Normalize(currentVersion);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var response = await Client.GetAsync("https://api.github.com/repos/PQueirozDev/Optimizer/releases/latest", timeout.Token);
        if (!response.IsSuccessStatusCode)
            return await CheckFeedAsync(current, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        var tag = json.RootElement.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag)) return new(false, current, current, null, null, null, null);
        var latest = Normalize(tag);
        var url = json.RootElement.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
        string? assetUrl = null, assetName = null, checksumUrl = null, signatureUrl = null;
        if (json.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var download = asset.TryGetProperty("browser_download_url", out var a) ? a.GetString() : null;
                if (assetUrl is null && name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true) { assetName = name; assetUrl = download; }
                else if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase)) checksumUrl = download;
                else if (string.Equals(name, SignatureAssetName, StringComparison.OrdinalIgnoreCase)) signatureUrl = download;
            }
        }
        var body = json.RootElement.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() : null;
        return new(Compare(latest, current) > 0, current, latest, url, assetUrl, assetName, checksumUrl, ReleaseNotes(body), signatureUrl);
    }

    /// <summary>
    /// Fallback sem API REST: o feed Atom continua acessível quando o limite anônimo
    /// do GitHub foi atingido. Os nomes dos assets são definidos pelo workflow do projeto.
    /// </summary>
    private static async Task<UpdateInfo> CheckFeedAsync(string current, CancellationToken token)
    {
        using var response = await Client.GetAsync("https://github.com/PQueirozDev/Optimizer/releases.atom", token);
        response.EnsureSuccessStatusCode();
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var entry = xml.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "entry");
        var title = entry?.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value?.Trim();
        var tag = title?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(tag)) return new(false, current, current, null, null, null, null);
        var latest = Normalize(tag);
        var releaseTag = "v" + latest;
        var baseUrl = $"https://github.com/PQueirozDev/Optimizer/releases/download/{releaseTag}";
        var assetName = $"Qrztweaks-Setup-{releaseTag}.exe";
        return new(
            Compare(latest, current) > 0,
            current,
            latest,
            $"https://github.com/PQueirozDev/Optimizer/releases/tag/{releaseTag}",
            $"{baseUrl}/{assetName}",
            assetName,
            $"{baseUrl}/{ChecksumAssetName}",
            Array.Empty<string>(),
            $"{baseUrl}/{SignatureAssetName}");
    }

    /// <summary>Novidades da release: os itens de lista do texto dela (as mesmas notas mostradas no app).</summary>
    public static IReadOnlyList<string> ReleaseNotes(string? body) =>
        (body ?? "").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            .Select(line => line[2..].Trim())
            .Where(line => line.Length > 0 && !line.Contains("Full Changelog", StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList();

    /// <summary>Indica se a release permite instalação automática (instalador + hash publicado + assinatura do hash).</summary>
    public static bool CanAutoInstall(UpdateInfo update) => !string.IsNullOrWhiteSpace(update.AssetUrl) && !string.IsNullOrWhiteSpace(update.ChecksumUrl) && !string.IsNullOrWhiteSpace(update.SignatureUrl);

    /// <summary>
    /// Versão a instalar: a encontrada agora, se for mais nova que a encontrada ao abrir o app e já tiver
    /// instalador e hash publicados. Sem isso, com duas versões lançadas enquanto o app estava aberto, ele
    /// instalava a que tinha visto primeiro e só depois a mais recente (uma por uma).
    /// </summary>
    public static UpdateInfo PickNewest(UpdateInfo pending, UpdateInfo? fresh) =>
        fresh is { IsAvailable: true } && CanAutoInstall(fresh) && Compare(Normalize(fresh.LatestVersion), Normalize(pending.LatestVersion)) > 0 ? fresh : pending;

    public async Task<VerifiedInstaller> DownloadAsync(UpdateInfo update, IProgress<(long read, long total)>? progress = null, CancellationToken token = default)
    {
        if (!CanAutoInstall(update)) throw new InvalidOperationException("Esta versão não publica o hash assinado do instalador. Baixe pela página de releases.");
        if (!IsTrustedGitHubUrl(update.AssetUrl!) || !IsTrustedGitHubUrl(update.ChecksumUrl!) || !IsTrustedGitHubUrl(update.SignatureUrl!)) throw new InvalidOperationException("Endereço de download inesperado.");

        var expected = await GetExpectedHashAsync(update, token);
        var folder = PrepareUpdatesFolder();
        var fileName = System.IO.Path.GetFileName(string.IsNullOrWhiteSpace(update.AssetName) ? $"Qrztweaks-Setup-v{update.LatestVersion}.exe" : update.AssetName);
        var path = System.IO.Path.Combine(folder, $"{Guid.NewGuid():N}-{fileName}");

        using (var response = await Client.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1;
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920]; long read = 0; int count;
            while ((count = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), token); read += count; progress?.Report((read, total));
            }
        }

        // Reabre sem permitir escrita por ninguém e calcula o hash a partir desse mesmo handle:
        // o arquivo verificado é exatamente o que será executado.
        var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(handle, token));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O instalador baixado não confere com o hash publicado na release. A atualização foi cancelada.");
            return new VerifiedInstaller(path, handle);
        }
        catch
        {
            handle.Dispose();
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static async Task<string> GetExpectedHashAsync(UpdateInfo update, CancellationToken token)
    {
        var bytes = await Client.GetByteArrayAsync(update.ChecksumUrl, token);
        var signature = await Client.GetStringAsync(update.SignatureUrl, token);
        // Sem assinatura válida, o hash pode ter sido publicado por quem conseguiu acesso à release: não instala
        if (!VerifyChecksumSignature(bytes, signature))
            throw new InvalidDataException("A assinatura do arquivo de hashes não confere com a chave do Qrztweaks. A atualização foi cancelada; baixe pela página oficial se tiver certeza da origem.");
        return FindHash(System.Text.Encoding.UTF8.GetString(bytes), update.AssetName);
    }

    /// <summary>Hash do instalador no formato do sha256sum ("&lt;hash&gt;  &lt;arquivo&gt;", com '*' opcional no modo binário).</summary>
    internal static string FindHash(string sums, string? assetName)
    {
        foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Formato do sha256sum: "<hash>  <arquivo>" (o nome pode vir com '*' no modo binário)
            var parts = line.Split((char[])[' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0].Length == 64 && string.Equals(parts[1].Trim().TrimStart('*'), assetName, StringComparison.OrdinalIgnoreCase))
                return parts[0];
        }
        throw new InvalidDataException("O hash do instalador não foi encontrado na release.");
    }

    // Pasta em ProgramData acessível apenas a Administradores/SYSTEM: um processo sem
    // privilégios não consegue trocar o instalador entre o download e a execução.
    private static string PrepareUpdatesFolder()
    {
        var info = new DirectoryInfo(UpdatesFolder);
        var parent = info.Parent ?? throw new InvalidOperationException("Não foi possível determinar a pasta pai das atualizações.");
        if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("A pasta pai das atualizações é um reparse point; download cancelado.");
        if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("A pasta de atualizações é um reparse point; download cancelado.");
        try
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            security.SetOwner(admins);
            foreach (var sid in new[] { admins, system })
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            info = new DirectoryInfo(UpdatesFolder);
            if (!info.Exists) info.Create(security); else info.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        { throw new InvalidOperationException("Não foi possível proteger a pasta de atualizações; execute o instalador elevado para provisioná-la.", ex); }
        parent = info.Parent!;
        if (parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("A pasta pai das atualizações é um reparse point; download cancelado.");
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("A pasta de atualizações tornou-se um reparse point; download cancelado.");

        foreach (var old in info.EnumerateFiles("*.exe"))
        {
            try { if (old.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)) old.Delete(); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return UpdatesFolder;
    }

    private static bool IsTrustedGitHubUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/PQueirozDev/Optimizer/releases/download/", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) => value.Trim().TrimStart('v', 'V');
    private static int Compare(string left, string right)
    {
        if (Version.TryParse(left, out var l) && Version.TryParse(right, out var r)) return l.CompareTo(r);
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
