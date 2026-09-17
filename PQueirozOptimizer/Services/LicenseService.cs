using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PQueirozOptimizer.Services;

public sealed record LicenseInfo(string Licensee, DateTime? ExpiresAtUtc, string MachineId, string Role)
{
    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.Ordinal);
}

public sealed class LicenseService
{
    // Public verification key. The private signing key is kept outside the application.
    private const string PublicKey = "BgIAAACkAABSU0ExAAgAAAEAAQB9+XduNkAH/W9GiugLLAh4G7CWFx5go0gQbke9prfzxnQXkuzKf4689pZq02aWwUtSwAIt1zel+Pq90cGT2QT8rxXE4mflu8t9Om2DrFZGO1/anX+FzNunbEW/2BOhZFqUY/lpF0ueZL59XS7hUhbksXJyAH4pbgYHKW5RDo4WLtoEjxLpxdX3R8yhDYDo+FrWeVkVZwf8lvYULAQJdUbjaiUOLVVu5VkE3i2WW0NbfS1Mhjq1KkpHbrC7QVgmYHE11RtrsEo75zCAM5ccBN4UUZW4yT04n0iVcX0tUrWdHerhyyMQqpEdcBOmywgkmc4bAp+351KSe6b5OjyEkqrC";
    private static string LicensePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "license.key");
    public string MachineId { get; } = CreateMachineId();

    public bool TryGetActiveLicense(out LicenseInfo? license, out string error)
    {
        license = null; error = "Nenhuma chave de acesso foi ativada neste computador.";
        try { return File.Exists(LicensePath) && TryValidate(File.ReadAllText(LicensePath), out license, out error); }
        catch { error = "Não foi possível ler a licença local."; return false; }
    }

    public bool TryActivate(string accessKey, out LicenseInfo? license, out string error)
    {
        if (!TryValidate(accessKey, out license, out error)) return false;
        try { Directory.CreateDirectory(Path.GetDirectoryName(LicensePath)!); File.WriteAllText(LicensePath, accessKey.Trim(), new UTF8Encoding(false)); return true; }
        catch { error = "A chave é válida, mas não foi possível salvá-la neste perfil do Windows."; return false; }
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
            if (payload.ExpiresAtUtc is { } expires && expires < DateTime.UtcNow) { error = "Esta chave de acesso expirou."; return false; }
            if (string.IsNullOrWhiteSpace(payload.MachineId)) { error = "Esta chave não está vinculada a um computador."; return false; }
            if (!string.Equals(payload.MachineId, MachineId, StringComparison.OrdinalIgnoreCase)) { error = "Esta chave foi emitida para outro computador."; return false; }
            var role = string.Equals(payload.Role, "Admin", StringComparison.Ordinal) ? "Admin" : "Standard";
            license = new LicenseInfo(payload.Licensee, payload.ExpiresAtUtc, payload.MachineId, role); error = string.Empty; return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException) { error = "O formato da chave está inválido."; return false; }
    }

    private static string CreateMachineId() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Environment.MachineName}|{Environment.UserDomainName}|{Environment.UserName}")))[..20];
    private static byte[] FromBase64Url(string value) { value = value.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=')); }
    private sealed class LicensePayload { public string Product { get; set; } = ""; public string Licensee { get; set; } = ""; public string? MachineId { get; set; } public string? Role { get; set; } public DateTime? ExpiresAtUtc { get; set; } }
}
