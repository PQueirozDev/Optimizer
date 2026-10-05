using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>Caminhos documentados de uma família de BIOS (ex.: ASUS Intel série 400), com a fonte oficial.</summary>
public sealed record BiosPathSet(string Id, string Vendor, string Source, string? SourceUrl, IReadOnlyList<string> Chipsets, Regex? Product,
    IReadOnlyDictionary<string, string[]> Paths, IReadOnlyDictionary<string, LocalizedText> Notes)
{
    public bool AppliesTo(MotherboardInfo board, PlatformInfo platform) =>
        (Chipsets.Count == 0 || platform.Chipset is { } c && Chipsets.Contains(c, StringComparer.OrdinalIgnoreCase))
        && (Product is null || SafeMatch(Product, board.Product));

    internal static bool SafeMatch(Regex regex, string? text)
    {
        try { return regex.IsMatch(text ?? ""); }
        catch (RegexMatchTimeoutException) { return false; }
    }
}

/// <summary>
/// Banco de dados do BIOS Advisor: caminhos por família de BIOS, nomes de opções por fabricante, perfis de placas e
/// os nomes das perguntas do SCEWIN para cada configuração. Só dados — nunca código. Vem embutido no app e pode ser
/// atualizado pela internet (bios-db/bios-db.json no GitHub do projeto), desde que a assinatura RSA confira e a versão seja maior.
/// </summary>
public sealed class BiosDatabase
{
    public int Version { get; }
    public string Updated { get; }
    public string Origin { get; }
    public IReadOnlyList<BiosPathSet> PathSets { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>> OptionNames { get; }
    public IReadOnlyList<BoardProfile> Boards { get; }
    public IReadOnlyDictionary<string, Regex[]> BiosQuestions { get; }

    private BiosDatabase(int version, string updated, string origin, IReadOnlyList<BiosPathSet> pathSets,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>> optionNames, IReadOnlyList<BoardProfile> boards, IReadOnlyDictionary<string, Regex[]> questions)
    {
        Version = version; Updated = updated; Origin = origin; PathSets = pathSets; OptionNames = optionNames; Boards = boards; BiosQuestions = questions;
    }

    public const int SupportedFormat = 1;
    public const string RemoteUrl = "https://raw.githubusercontent.com/PQueirozDev/Optimizer/main/bios-db/bios-db.json";
    private const int MaxBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Chave pública que confere a assinatura do banco baixado (a privada fica só no PC do desenvolvedor).</summary>
    internal const string PublicKeyBlob = "BgIAAACkAABSU0ExAAgAAAEAAQCdeN4YkV/2W1qw57zPqc+5Fc/ituBE3k62Y9lxqOVHKHgeSNLvHycxooFLF1a+U+D2DFBB/vU8ZyDm7rYR1iExnBplkaihOM7DdT/pGTvH4z7hK4s6lgpB+fcXLx8jGlqtrQOfaCXQddbQpbOe+CSBx2rzrjRIzuKWMJmENhLs/Qk5JxC6lFu2Nnsoqi5Xq3IndMacsJdtATWpr55SWVN7+NHVxKkr6zLTnfWb8SVZvBnYHHSDuIn3mnCwFKoPoobmeljCGskY9kRvOj515DRgNsCv/cCeVpmec6y2O9sPJSZymRlVsAG3ew5G1M7u/cfY5dd7X9DAdsDsW4kqR6nk";

    // ---------- Banco em uso ----------
    private static readonly object Gate = new();
    private static BiosDatabase? _current;

    /// <summary>O banco mais novo disponível: embutido, ou o cache/remoto verificado se tiver versão maior.</summary>
    public static BiosDatabase Current
    {
        get
        {
            lock (Gate)
            {
                if (_current != null) return _current;
                _current = LoadEmbedded();
                if (LoadCached() is { } cached && cached.Version > _current.Version) _current = cached;
                return _current;
            }
        }
    }

    public static BiosDatabase LoadEmbedded()
    {
        using var stream = typeof(BiosDatabase).Assembly.GetManifestResourceStream("PQueirozOptimizer.BiosAdvisor.bios-db.json")
            ?? throw new InvalidOperationException("Banco do BIOS Advisor não embutido no app.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd(), "embutido");
    }

    private static string CacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "bios-advisor");
    private static string CachePath => Path.Combine(CacheDirectory, "bios-db.json");

    private static BiosDatabase? LoadCached()
    {
        try
        {
            if (!File.Exists(CachePath) || !File.Exists(CachePath + ".sig")) return null;
            if (new FileInfo(CachePath).Length > MaxBytes || new FileInfo(CachePath + ".sig").Length > MaxBytes) return null;
            var bytes = File.ReadAllBytes(CachePath);
            // O cache também é conferido: um arquivo trocado no disco não vira "fonte oficial"
            return VerifySignature(bytes, File.ReadAllText(CachePath + ".sig")) ? Parse(Encoding.UTF8.GetString(bytes), "atualizado") : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException) { return null; }
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static DateTime _lastCheck;

    /// <summary>
    /// Busca o banco publicado. Só troca o atual se a assinatura conferir e a versão for maior. Uma vez a cada 6 h.
    /// Devolve true quando um banco novo passou a valer.
    /// </summary>
    public static async Task<bool> RefreshAsync(CancellationToken token = default)
    {
        if (DateTime.UtcNow - _lastCheck < TimeSpan.FromHours(6)) return false;
        _lastCheck = DateTime.UtcNow;
        try
        {
            var json = await DownloadAsync(RemoteUrl, token);
            var sig = Encoding.UTF8.GetString(await DownloadAsync(RemoteUrl + ".sig", token));
            if (!VerifySignature(json, sig)) return false;
            var db = Parse(Encoding.UTF8.GetString(json), "atualizado");
            lock (Gate)
            {
                if (db.Version <= Current.Version) return false;
                _current = db;
            }
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllBytesAsync(CachePath, json, token);
            await File.WriteAllTextAsync(CachePath + ".sig", sig, token);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<byte[]> DownloadAsync(string url, CancellationToken token)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidOperationException("Banco grande demais.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(), token)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw new InvalidOperationException("Banco grande demais.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Assinatura RSA-SHA256 sobre o texto normalizado (sem BOM e com quebras de linha LF), para o mesmo arquivo
    /// conferir no Windows (CRLF) e no GitHub (LF).
    /// </summary>
    public static bool VerifySignature(byte[] json, string signatureBase64, string? publicKeyBlob = null)
    {
        try
        {
            using var rsa = new RSACryptoServiceProvider();
            rsa.ImportCspBlob(Convert.FromBase64String(publicKeyBlob ?? PublicKeyBlob));
            return rsa.VerifyData(Canonical(json), CryptoConfig.MapNameToOID("SHA256")!, Convert.FromBase64String(signatureBase64.Trim()));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException) { return false; }
    }

    public static byte[] Canonical(byte[] json)
    {
        var text = Encoding.UTF8.GetString(json).TrimStart('﻿').Replace("\r\n", "\n");
        return Encoding.UTF8.GetBytes(text);
    }

    // ---------- Leitura do JSON ----------
    public static BiosDatabase Parse(string json, string origin)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (Int(root, "format") != SupportedFormat) throw new FormatException("Formato do banco do BIOS Advisor não suportado.");
        var version = Int(root, "version") ?? throw new FormatException("Banco sem versão.");
        if (version < 1) throw new FormatException("Versão do banco deve ser positiva.");

        var pathSets = new List<BiosPathSet>();
        foreach (var s in Array(root, "pathSets"))
        {
            var paths = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (s.TryGetProperty("paths", out var p) && p.ValueKind == JsonValueKind.Object)
                foreach (var prop in p.EnumerateObject()) paths[prop.Name] = Strings(prop.Value);
            pathSets.Add(new BiosPathSet(Str(s, "id"), Str(s, "vendor"), Str(s, "source"), StrOrNull(s, "sourceUrl"), Strings(s, "chipsets"),
                RegexOrNull(StrOrNull(s, "productPattern")), paths, Texts(s, "notes")));
        }

        var optionNames = new Dictionary<string, IReadOnlyDictionary<string, string[]>>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("optionNames", out var on) && on.ValueKind == JsonValueKind.Object)
            foreach (var vendor in on.EnumerateObject())
                optionNames[vendor.Name] = vendor.Value.EnumerateObject().ToDictionary(x => x.Name, x => Strings(x.Value), StringComparer.Ordinal);

        var boards = new List<BoardProfile>();
        foreach (var b in Array(root, "boards"))
        {
            var product = RegexOrNull(StrOrNull(b, "productPattern"));
            if (product is null) continue;
            var facts = Array(b, "facts").Select(f => new LocalizedText(Str(f, "pt"), Str(f, "en"))).ToList();
            var slots = Strings(b, "twoModuleSlots");
            boards.Add(new BoardProfile(Str(b, "name"), Str(b, "vendor"), product, Str(b, "chipset"), Str(b, "source"), StrOrNull(b, "sourceUrl"),
                slots.Length > 0 ? slots : null, b.TryGetProperty("twoModuleSlotsConfirmed", out var tc) && tc.ValueKind == JsonValueKind.True,
                StrOrNull(b, "primaryGpuSlot"), StrOrNull(b, "primaryGpuSlotLink"), StrOrNull(b, "biosFileName"), Strings(b, "mappedCpus"), facts));
        }

        var questions = new Dictionary<string, Regex[]>(StringComparer.Ordinal);
        if (root.TryGetProperty("biosQuestions", out var q) && q.ValueKind == JsonValueKind.Object)
            foreach (var prop in q.EnumerateObject())
                questions[prop.Name] = Strings(prop.Value).Select(RegexOrNull).OfType<Regex>().ToArray();

        return new BiosDatabase(version, StrOrNull(root, "updated") ?? "", origin, pathSets, optionNames, boards, questions);
    }

    private static int? Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    private static string Str(JsonElement e, string name) => StrOrNull(e, name) ?? "";
    private static string? StrOrNull(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static IEnumerable<JsonElement> Array(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();
    private static string[] Strings(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? Strings(v) : System.Array.Empty<string>();
    private static string[] Strings(JsonElement v) => v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : System.Array.Empty<string>();

    private static Dictionary<string, LocalizedText> Texts(JsonElement e, string name)
    {
        var map = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
        if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object)
            foreach (var prop in v.EnumerateObject()) map[prop.Name] = new LocalizedText(Str(prop.Value, "pt"), Str(prop.Value, "en"));
        return map;
    }

    /// <summary>Expressão vinda do banco: com tempo limite (evita travar com padrão malicioso) e ignorada se inválida.</summary>
    private static Regex? RegexOrNull(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        try { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout); }
        catch (ArgumentException) { return null; }
    }

    // ---------- Consultas ----------
    public BoardProfile? BoardFor(MotherboardInfo board) => Boards.FirstOrDefault(b => BiosPathSet.SafeMatch(b.Product, board.Product));

    public IEnumerable<BiosPathSet> PathSetsFor(string vendorId) => PathSets.Where(p => p.Vendor.Equals(vendorId, StringComparison.OrdinalIgnoreCase));

    public string[]? OptionNamesFor(string vendorId, string settingId) =>
        OptionNames.TryGetValue(vendorId, out var map) && map.TryGetValue(settingId, out var names) ? names : null;

    /// <summary>Usado pelos testes: substitui o banco em uso (null volta ao normal).</summary>
    public static void Override(BiosDatabase? db) { lock (Gate) _current = db; }
}
