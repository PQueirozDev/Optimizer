using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PQueirozOptimizer.Services;

public sealed record LicenseInfo(string Licensee, DateTime? ExpiresAtUtc, string MachineId, string Role)
{
    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.Ordinal);
    /// <summary>Dias de calendário até o vencimento (0 = vence hoje); null para licença vitalícia.</summary>
    public int? DaysLeft => ExpiresAtUtc is { } expires ? (expires.ToLocalTime().Date - DateTime.Today).Days : null;
    /// <summary>Mesma janela de aviso do License Manager ("vencem em 7 dias").</summary>
    public bool IsExpiringSoon => DaysLeft is >= 0 and <= 7;
}

public sealed class LicenseService
{
    // Public verification key. The private signing key is kept outside the application.
    private const string PublicKey = "BgIAAACkAABSU0ExAAgAAAEAAQB9+XduNkAH/W9GiugLLAh4G7CWFx5go0gQbke9prfzxnQXkuzKf4689pZq02aWwUtSwAIt1zel+Pq90cGT2QT8rxXE4mflu8t9Om2DrFZGO1/anX+FzNunbEW/2BOhZFqUY/lpF0ueZL59XS7hUhbksXJyAH4pbgYHKW5RDo4WLtoEjxLpxdX3R8yhDYDo+FrWeVkVZwf8lvYULAQJdUbjaiUOLVVu5VkE3i2WW0NbfS1Mhjq1KkpHbrC7QVgmYHE11RtrsEo75zCAM5ccBN4UUZW4yT04n0iVcX0tUrWdHerhyyMQqpEdcBOmywgkmc4bAp+351KSe6b5OjyEkqrC";
    private static string LicensePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "license.key");
    public string MachineId { get; } = CreateMachineId();
    // Mesmo ID em blocos de 5 (XXXXX-XXXXX-...), mais fácil de ler e ditar. O emissor aceita os dois formatos.
    public string DisplayMachineId => string.Join("-", Enumerable.Range(0, MachineId.Length / 5).Select(i => MachineId.Substring(i * 5, 5)));
    // ID antigo (nome do PC + usuário). Só é aceito para manter válidas as chaves já emitidas.
    private readonly string _legacyMachineId = CreateLegacyMachineId();

    /// <summary>Chave legítima deste computador que já venceu (última validada), para a tela de ativação pedir a renovação.</summary>
    public LicenseInfo? ExpiredLicense { get; private set; }
    public bool HasStoredKey => File.Exists(LicensePath);

    public bool TryGetActiveLicense(out LicenseInfo? license, out string error)
    {
        license = null; error = "Nenhuma chave de acesso foi ativada neste computador.";
        try { return File.Exists(LicensePath) && TryValidate(File.ReadAllText(LicensePath), out license, out error); }
        catch { error = "Não foi possível ler a licença local."; return false; }
    }

    public bool TryActivate(string accessKey, out LicenseInfo? license, out string error)
    {
        accessKey = ExtractKey(accessKey) ?? accessKey.Trim();
        if (!TryValidate(accessKey, out license, out error)) return false;
        try { Directory.CreateDirectory(Path.GetDirectoryName(LicensePath)!); File.WriteAllText(LicensePath, accessKey, new UTF8Encoding(false)); return true; }
        catch { error = "A chave é válida, mas não foi possível salvá-la neste perfil do Windows."; return false; }
    }

    // Texto pronto para o cliente enviar ao pedir a chave (WhatsApp, e-mail...). O License Manager lê o ID direto dele.
    // Na renovação, o titular vai junto para facilitar achar o cliente no histórico do emissor.
    public string BuildActivationRequest(string? renewalLicensee = null) => renewalLicensee is null
        ? $"Pedido de ativação - PQueiroz Optimizer\nID do computador: {DisplayMachineId}\nComputador: {Environment.MachineName}"
        : $"Pedido de renovação - PQueiroz Optimizer\nTitular: {renewalLicensee}\nID do computador: {DisplayMachineId}\nComputador: {Environment.MachineName}";

    // Localiza a chave dentro de qualquer texto colado, mesmo quebrada em linhas ou junto de uma mensagem.
    public static string? ExtractKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // A assinatura RSA-2048 em base64url tem sempre 342 caracteres: o limite evita "colar" o texto seguinte na chave.
        var match = Regex.Match(Regex.Replace(text, @"\s+", ""), @"PQO1-[A-Za-z0-9_-]+\.[A-Za-z0-9_-]{1,342}");
        return match.Success ? match.Value : null;
    }

    private bool TryValidate(string accessKey, out LicenseInfo? license, out string error)
    {
        license = null; error = "Chave de acesso inválida.";
        var parts = accessKey.Trim().Split('.', 2);
        if (parts.Length != 2 || !parts[0].StartsWith("PQO1-", StringComparison.Ordinal)) return false;
        try
        {
            var data = FromBase64Url(parts[0][5..]);
            using var rsa = new RSACryptoServiceProvider(); rsa.ImportCspBlob(Convert.FromBase64String(PublicKey));
            if (!rsa.VerifyData(data, CryptoConfig.MapNameToOID("SHA256")!, FromBase64Url(parts[1]))) { error = "A assinatura da chave não é válida."; return false; }
            var payload = JsonSerializer.Deserialize<LicensePayload>(data);
            if (payload is null || payload.Product != "PQueirozOptimizer") return false;
            if (string.IsNullOrWhiteSpace(payload.Licensee)) { error = "A chave não informa o titular."; return false; }
            if (string.IsNullOrWhiteSpace(payload.MachineId)) { error = "Esta chave não está vinculada a um computador."; return false; }
            if (!string.Equals(payload.MachineId, MachineId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(payload.MachineId, _legacyMachineId, StringComparison.OrdinalIgnoreCase)) { error = "Esta chave foi emitida para outro computador."; return false; }
            var role = string.Equals(payload.Role, "Admin", StringComparison.Ordinal) ? "Admin" : "Standard";
            var info = new LicenseInfo(payload.Licensee, payload.ExpiresAtUtc, payload.MachineId, role);
            // O computador é conferido antes da validade: só a chave deste PC conta como "expirada, renove"
            if (payload.ExpiresAtUtc is { } expires && expires < DateTime.UtcNow) { ExpiredLicense = info; error = "Esta chave de acesso expirou."; return false; }
            license = info; error = string.Empty; return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException) { error = "O formato da chave está inválido."; return false; }
    }

    // MachineGuid é gerado na instalação do Windows: não muda ao renomear o PC ou o usuário
    // e não pode ser reproduzido em outra máquina apenas copiando nomes.
    private static string CreateMachineId()
    {
        try
        {
            using var hive = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            if (key?.GetValue("MachineGuid") is string guid && Guid.TryParse(guid, out var parsed))
                return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("PQO2|" + parsed.ToString("D").ToUpperInvariant())))[..20];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return CreateLegacyMachineId();
    }
    private static string CreateLegacyMachineId() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Environment.MachineName}|{Environment.UserDomainName}|{Environment.UserName}")))[..20];
    private static byte[] FromBase64Url(string value) { value = value.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=')); }
    private sealed class LicensePayload { public string Product { get; set; } = ""; public string Licensee { get; set; } = ""; public string? MachineId { get; set; } public string? Role { get; set; } public DateTime? ExpiresAtUtc { get; set; } }
}
