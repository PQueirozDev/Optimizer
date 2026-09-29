using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Chaves revogadas pelo License Manager, publicadas em site/revocations.json no repositório público.
/// A lista só vale assinada com a mesma chave das licenças, e a última lista válida fica guardada
/// para funcionar offline.
/// </summary>
public sealed class RevocationService
{
    public const string ListUrl = "https://raw.githubusercontent.com/PQueirozDev/Optimizer/main/site/revocations.json";
    private static readonly HttpClient Client = CreateClient();
    public static RevocationService Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "revocations.json"));

    private readonly string _cachePath;
    private readonly string _publicKeyBlob;
    private HashSet<string>? _revoked;
    private DateTime _issuedAtUtc;

    /// <param name="publicKeyBlob">Chave pública que assina a lista; só os testes trocam a do emissor.</param>
    public RevocationService(string cachePath, string? publicKeyBlob = null)
    {
        _cachePath = cachePath;
        _publicKeyBlob = publicKeyBlob ?? LicenseService.PublicKeyBlob;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PQueirozOptimizer", version));
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return client;
    }

    /// <summary>Identificador de uma chave na lista: SHA-256 do conteúdo assinado (mesmo cálculo do License Manager).</summary>
    public static string KeyId(byte[] signedPayload) => Convert.ToHexString(SHA256.HashData(signedPayload));

    public bool IsRevoked(string keyId) => Load().Contains(keyId);

    /// <summary>Aplica uma lista baixada: precisa ter assinatura válida e ser mais nova que a guardada.</summary>
    public bool TryApply(string json)
    {
        if (!TryVerify(json, _publicKeyBlob, out var issuedAtUtc, out var ids)) return false;
        Load();
        if (issuedAtUtc <= _issuedAtUtc) return false;
        try { Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!); File.WriteAllText(_cachePath, json); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // vale nesta sessão mesmo sem cache
        _revoked = ids; _issuedAtUtc = issuedAtUtc;
        return true;
    }

    /// <summary>
    /// Baixa a lista publicada. Devolve a hora do servidor (cabeçalho Date), que a trava do relógio usa.
    /// Lança HttpRequestException/TaskCanceledException sem internet.
    /// </summary>
    public async Task<DateTime?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var response = await Client.GetAsync(ListUrl, timeout.Token);
        // 404: nenhuma lista publicada ainda; a guardada continua valendo
        if (response.IsSuccessStatusCode) TryApply(await response.Content.ReadAsStringAsync(timeout.Token));
        return response.Headers.Date?.UtcDateTime;
    }

    private HashSet<string> Load()
    {
        if (_revoked is not null) return _revoked;
        _revoked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(_cachePath) && TryVerify(File.ReadAllText(_cachePath), _publicKeyBlob, out var issuedAtUtc, out var ids)) { _revoked = ids; _issuedAtUtc = issuedAtUtc; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return _revoked;
    }

    public static bool TryVerify(string json, string publicKeyBlob, out DateTime issuedAtUtc, out HashSet<string> ids)
    {
        issuedAtUtc = default; ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (JsonSerializer.Deserialize<SignedList>(json) is not { } envelope) return false;
            var payload = LicenseService.FromBase64Url(envelope.Payload);
            using var rsa = new RSACryptoServiceProvider(); rsa.ImportCspBlob(Convert.FromBase64String(publicKeyBlob));
            if (!rsa.VerifyData(payload, CryptoConfig.MapNameToOID("SHA256")!, LicenseService.FromBase64Url(envelope.Signature))) return false;
            // "Kind" impede reaproveitar a assinatura de uma licença como se fosse uma lista
            if (JsonSerializer.Deserialize<RevocationPayload>(payload) is not { Product: "PQueirozOptimizer", Kind: "revocations" } list) return false;
            issuedAtUtc = list.IssuedAtUtc;
            ids = new HashSet<string>(list.Revoked, StringComparer.OrdinalIgnoreCase);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or ArgumentException) { return false; }
    }

    private sealed record SignedList(string Payload, string Signature);
    private sealed record RevocationPayload(string Product, string Kind, DateTime IssuedAtUtc, List<string> Revoked);
}
