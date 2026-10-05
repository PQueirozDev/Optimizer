using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PQueirozOptimizer.Services;

/// <summary>Quanto do app o plano libera: cada nível inclui tudo do anterior.</summary>
public enum PlanTier { Base = 1, Intermediate = 2, Full = 3 }

/// <summary>Planos vendidos. O nome vai dentro da chave assinada (campo Plan); chaves antigas não têm o campo.</summary>
public static class LicensePlans
{
    public const string Base = "Base";
    public const string Intermediate = "Intermediário";
    public const string Advanced = "Avançado";
    public const string Lifetime = "Vitalício";
    public const string Custom = "Personalizado";
    /// <summary>Plano único antigo (antes da 1.9.0): liberava o app completo, então continua liberando.</summary>
    public const string Monthly = "Mensal";
    public const string DiscordUrl = "https://discord.gg/pHJ4Waxft";
    public const string SiteUrl = "https://qrztwk.vercel.app/#comprar";

    /// <summary>Planos à venda, do menor para o maior, com o preço exibido no app e no site.</summary>
    public static readonly IReadOnlyList<(string Name, PlanTier Tier, string Price)> ForSale =
    [
        (Base, PlanTier.Base, "R$ 15/mês"),
        (Intermediate, PlanTier.Intermediate, "R$ 25/mês"),
        (Advanced, PlanTier.Full, "R$ 29,99/mês"),
        (Lifetime, PlanTier.Full, "R$ 59,99 único"),
    ];

    public static bool IsKnown(string? plan) => plan is Base or Intermediate or Advanced or Lifetime or Custom or Monthly;

    /// <summary>Nível de um plano conhecido. Mensal, Personalizado e os planos completos liberam tudo.</summary>
    public static PlanTier TierOf(string plan) => plan switch
    {
        Base => PlanTier.Base,
        Intermediate => PlanTier.Intermediate,
        _ => PlanTier.Full,
    };

    public static string TierName(PlanTier tier) => tier switch
    {
        PlanTier.Base => Base,
        PlanTier.Intermediate => Intermediate,
        _ => Advanced,
    };
}

/// <summary>
/// O que cada nível libera, por página. Páginas fora da lista (Visão geral, Atividade e reversão, Pontos de restauração,
/// Configurações, Patch notes, Sobre) ficam em todos os planos: ninguém pode ficar sem desfazer o que já aplicou.
/// </summary>
public static class PlanAccess
{
    private static readonly Dictionary<string, PlanTier> PageTier = new(StringComparer.Ordinal)
    {
        ["optimization"] = PlanTier.Base, ["startup"] = PlanTier.Base, ["fixes"] = PlanTier.Base, ["tools"] = PlanTier.Base,
        ["services"] = PlanTier.Intermediate, ["apps"] = PlanTier.Intermediate, ["drivers"] = PlanTier.Intermediate,
        ["network"] = PlanTier.Intermediate, ["resources"] = PlanTier.Intermediate, ["diagnostics"] = PlanTier.Intermediate,
        ["gaming"] = PlanTier.Full, ["customize"] = PlanTier.Full, ["bios"] = PlanTier.Full, ["biosadvisor"] = PlanTier.Full,
    };

    /// <summary>Modo de energia (atalho e janela própria): faz parte do Modo Jogo, então só nos planos completos.</summary>
    public const string PowerMode = "powermode";

    public static PlanTier Required(string page) => page == PowerMode ? PlanTier.Full : PageTier.GetValueOrDefault(page, PlanTier.Base);

    /// <summary>
    /// Operações em lote (página Otimizações, Ctrl+K, Apps, Resolver tudo) passam pelo mesmo nível das páginas
    /// cujos ajustes aplicam: o Debloat desliga serviços e remove apps; a Versão Avançada (com ou sem serviços) e a Inteligente aplicam o Modo Jogo.
    /// Devolve a página equivalente para conferir o acesso e montar a tela de upgrade.
    /// </summary>
    public static string OperationPage(string operation) => operation switch
    {
        "debloat" => "services",
        "gamer" or "gamerservicos" or "inteligente" => "gaming",
        _ => "optimization",
    };

    public static bool Allows(LicenseInfo? license, string page) => license is not null && license.Tier >= Required(page);
}

public sealed record LicenseInfo(string Licensee, DateTime? ExpiresAtUtc, string MachineId, string Role, string? Plan = null)
{
    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.Ordinal);
    public bool IsLifetime => ExpiresAtUtc is null;
    /// <summary>Plano para exibir: o da chave ou, nas chaves antigas, deduzido da validade.</summary>
    public string PlanName => LicensePlans.IsKnown(Plan) ? Plan! : IsLifetime ? LicensePlans.Lifetime : LicensePlans.Custom;
    /// <summary>Nível liberado. Admin sempre tem tudo; um plano desconhecido (chave de uma versão futura) fica no Base até atualizar.</summary>
    public PlanTier Tier => IsAdmin ? PlanTier.Full
        : Plan is null ? PlanTier.Full
        : LicensePlans.IsKnown(Plan) ? LicensePlans.TierOf(Plan) : PlanTier.Base;
    /// <summary>Planos maiores que o atual, para os botões de upgrade.</summary>
    public IEnumerable<(string Name, PlanTier Tier, string Price)> Upgrades =>
        LicensePlans.ForSale.Where(p => !IsLifetime && (p.Tier > Tier || p.Name == LicensePlans.Lifetime) && p.Name != PlanName);
    /// <summary>Dias de calendário até o vencimento (0 = vence hoje); null para licença vitalícia.</summary>
    public int? DaysLeft => ExpiresAtUtc is { } expires ? (expires.ToLocalTime().Date - DateTime.Today).Days : null;
    /// <summary>Mesma janela de aviso do License Manager ("vencem em 7 dias").</summary>
    public bool IsExpiringSoon => DaysLeft is >= 0 and <= 7;
}

public sealed class LicenseService
{
    // Public verification key. The private signing key is kept outside the application.
    // Também confere a lista de revogação, assinada pelo mesmo emissor.
    internal const string PublicKeyBlob ="BgIAAACkAABSU0ExAAgAAAEAAQB9+XduNkAH/W9GiugLLAh4G7CWFx5go0gQbke9prfzxnQXkuzKf4689pZq02aWwUtSwAIt1zel+Pq90cGT2QT8rxXE4mflu8t9Om2DrFZGO1/anX+FzNunbEW/2BOhZFqUY/lpF0ueZL59XS7hUhbksXJyAH4pbgYHKW5RDo4WLtoEjxLpxdX3R8yhDYDo+FrWeVkVZwf8lvYULAQJdUbjaiUOLVVu5VkE3i2WW0NbfS1Mhjq1KkpHbrC7QVgmYHE11RtrsEo75zCAM5ccBN4UUZW4yT04n0iVcX0tUrWdHerhyyMQqpEdcBOmywgkmc4bAp+351KSe6b5OjyEkqrC";
    private static string LicensePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "license.key");
    public string MachineId { get; } = CreateMachineId();
    // Mesmo ID em blocos de 5 (XXXXX-XXXXX-...), mais fácil de ler e ditar. O emissor aceita os dois formatos.
    public string DisplayMachineId => string.Join("-", Enumerable.Range(0, MachineId.Length / 5).Select(i => MachineId.Substring(i * 5, 5)));
    // ID antigo (nome do PC + usuário). Só é aceito para manter válidas as chaves já emitidas.
    private readonly string _legacyMachineId = CreateLegacyMachineId();
    private readonly LicenseClock _clock;
    private readonly RevocationService _revocations;

    public LicenseService() : this(LicenseClock.Default, RevocationService.Default) { }
    public LicenseService(LicenseClock clock, RevocationService revocations) { _clock = clock; _revocations = revocations; }

    /// <summary>Chave legítima deste computador que já venceu (última validada), para a tela de ativação pedir a renovação.</summary>
    public LicenseInfo? ExpiredLicense { get; private set; }
    /// <summary>A última validação recusou a chave porque o relógio do Windows voltou no tempo.</summary>
    public bool ClockRolledBack { get; private set; }
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
    // Com o plano desejado (ex.: upgrade para o Vitalício), o emissor já abre o formulário com ele escolhido.
    public string BuildActivationRequest(string? renewalLicensee = null, string? desiredPlan = null)
    {
        var title = desiredPlan is not null && renewalLicensee is not null ? "Pedido de upgrade" : renewalLicensee is null ? "Pedido de ativação" : "Pedido de renovação";
        var lines = new List<string> { $"{title} - Qrztweaks" };
        if (renewalLicensee is not null) lines.Add($"Titular: {renewalLicensee}");
        if (desiredPlan is not null) lines.Add($"Plano desejado: {desiredPlan}");
        lines.Add($"ID do computador: {DisplayMachineId}");
        lines.Add($"Computador: {Environment.MachineName}");
        return string.Join("\n", lines);
    }

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
        license = null; error = "Chave de acesso inválida."; ClockRolledBack = false;
        var parts = accessKey.Trim().Split('.', 2);
        if (parts.Length != 2 || !parts[0].StartsWith("PQO1-", StringComparison.Ordinal)) return false;
        try
        {
            var data = FromBase64Url(parts[0][5..]);
            using var rsa = new RSACryptoServiceProvider(); rsa.ImportCspBlob(Convert.FromBase64String(PublicKeyBlob));
            if (!rsa.VerifyData(data, CryptoConfig.MapNameToOID("SHA256")!, FromBase64Url(parts[1]))) { error = "A assinatura da chave não é válida."; return false; }
            var payload = JsonSerializer.Deserialize<LicensePayload>(data);
            if (payload is null || payload.Product != "PQueirozOptimizer") return false;
            if (string.IsNullOrWhiteSpace(payload.Licensee)) { error = "A chave não informa o titular."; return false; }
            if (string.IsNullOrWhiteSpace(payload.MachineId)) { error = "Esta chave não está vinculada a um computador."; return false; }
            if (!string.Equals(payload.MachineId, MachineId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(payload.MachineId, _legacyMachineId, StringComparison.OrdinalIgnoreCase)) { error = "Esta chave foi emitida para outro computador."; return false; }
            var role = string.Equals(payload.Role, "Admin", StringComparison.Ordinal) ? "Admin" : "Standard";
            var info = new LicenseInfo(payload.Licensee, payload.ExpiresAtUtc, payload.MachineId, role, payload.Plan);
            if (_revocations.IsRevoked(RevocationService.KeyId(data))) { error = "Esta chave foi revogada. Fale com o suporte para mais informações."; return false; }
            // O computador é conferido antes da validade: só a chave deste PC conta como "expirada, renove".
            // Só licenças com validade dependem do relógio; as vitalícias nunca são travadas por ele.
            var now = DateTime.UtcNow;
            if (payload.ExpiresAtUtc is { } expires)
            {
                if (_clock.IsRolledBack(now)) { ClockRolledBack = true; error = "A data e a hora do Windows estão atrasadas em relação ao último uso do Optimizer. Acerte o relógio para continuar."; return false; }
                if (expires < _clock.EffectiveNowUtc(now)) { ExpiredLicense = info; error = "Esta chave de acesso expirou."; return false; }
            }
            _clock.Observe(now);
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
    internal static byte[] FromBase64Url(string value) { value = value.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=')); }
    private sealed class LicensePayload { public string Product { get; set; } = ""; public string Licensee { get; set; } = ""; public string? MachineId { get; set; } public string? Role { get; set; } public DateTime? ExpiresAtUtc { get; set; } public string? Plan { get; set; } }
}
