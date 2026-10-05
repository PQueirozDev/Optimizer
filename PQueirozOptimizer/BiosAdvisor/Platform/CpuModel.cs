using System.Text.RegularExpressions;

namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>
/// Modelo comercial do processador lido do nome ("Intel(R) Core(TM) i7-10700F", "AMD Ryzen 7 5800X3D").
/// A geração vem do número do modelo, não do campo Family do WMI (que não é a geração de marketing).
/// </summary>
public sealed record CpuModel(CpuVendor Vendor, string Brand, int? Tier, int? Generation, string Number, string Suffix, bool IsMobile)
{
    /// <summary>Multiplicador desbloqueado: Intel com "K"; Ryzen de desktop (exceto mobile).</summary>
    public bool Unlocked => Vendor == CpuVendor.Intel ? Suffix.Contains('K') : Vendor == CpuVendor.Amd && Brand == "Ryzen" && !IsMobile;
    public bool IsX3D => Suffix.Contains("X3D", StringComparison.OrdinalIgnoreCase);
    public bool HasIntegratedGraphics => Vendor == CpuVendor.Intel ? !Suffix.Contains('F') : Suffix.Contains('G') || (Generation >= 7 && !Suffix.Contains('F'));
    /// <summary>Ex.: "i7-10700F", "Ryzen 7 5800X3D".</summary>
    public string ShortName => Vendor == CpuVendor.Intel && Brand == "Core" ? $"i{Tier}-{Number}{Suffix}" : Brand == "Core Ultra" ? $"Core Ultra {Tier} {Number}{Suffix}" : Brand == "Ryzen" ? $"Ryzen {Tier} {Number}{Suffix}" : Number;

    public static CpuModel Unknown { get; } = new(CpuVendor.Unknown, "", null, null, "", "", false);

    private static readonly Regex IntelCore = new(@"Core\s*(?:\(TM\))?\s*i([3579])[- ](\d{4,5})([A-Z]{0,3})\b", RegexOptions.IgnoreCase);
    private static readonly Regex IntelUltra = new(@"Core\s*(?:\(TM\))?\s*Ultra\s*([3579])\s*(\d{3})([A-Z]{0,2})\b", RegexOptions.IgnoreCase);
    private static readonly Regex Ryzen = new(@"Ryzen\s*(?:Threadripper\s*)?(\d)\s*(?:PRO\s*)?(\d{4})(X3D|XT|X|GE|G|F|HX|HS|H|U)?\b", RegexOptions.IgnoreCase);

    public static CpuModel Parse(string name)
    {
        name ??= "";
        if (IntelCore.Match(name) is { Success: true } i)
        {
            var number = i.Groups[2].Value;
            // 10700 → 10ª geração; 9700 → 9ª; 12400 → 12ª
            var generation = number.Length == 5 ? int.Parse(number[..2]) : int.Parse(number[..1]);
            var suffix = i.Groups[3].Value.ToUpperInvariant();
            return new CpuModel(CpuVendor.Intel, "Core", int.Parse(i.Groups[1].Value), generation, number, suffix, IsIntelMobile(suffix));
        }
        if (IntelUltra.Match(name) is { Success: true } u)
        {
            var number = u.Groups[2].Value;
            var suffix = u.Groups[3].Value.ToUpperInvariant();
            // Série 1 (Meteor Lake, só notebook) e série 2 (Arrow Lake / Lunar Lake)
            return new CpuModel(CpuVendor.Intel, "Core Ultra", int.Parse(u.Groups[1].Value), number[0] - '0', number, suffix, suffix is "H" or "U" or "V" or "HX");
        }
        if (Ryzen.Match(name) is { Success: true } r)
        {
            var number = r.Groups[2].Value;
            var suffix = r.Groups[3].Value.ToUpperInvariant();
            // 5800X3D → série 5000; Threadripper usa o mesmo esquema
            return new CpuModel(CpuVendor.Amd, "Ryzen", int.Parse(r.Groups[1].Value), number[0] - '0', number, suffix, suffix is "U" or "H" or "HS" or "HX");
        }
        var vendor = name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? CpuVendor.Intel : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? CpuVendor.Amd : CpuVendor.Unknown;
        return Unknown with { Vendor = vendor };
    }

    private static bool IsIntelMobile(string suffix) => suffix.StartsWith('H') || suffix.StartsWith('U') || suffix.StartsWith('Y');
}
