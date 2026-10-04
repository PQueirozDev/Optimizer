namespace PQueirozOptimizer.Services;

public class LocalizationService
{
    /// <summary>Idiomas disponíveis: português (original) e inglês.</summary>
    public static readonly string[] Languages = { "pt", "en" };

    public string CurrentLanguage { get; private set; } = "pt";

    public bool IsEnglish => CurrentLanguage == "en";

    public static string Normalize(string? language) => Languages.FirstOrDefault(l => l.Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "pt";

    public void SetLanguage(string language)
    {
        CurrentLanguage = Normalize(language);
        Translator.Language = CurrentLanguage;
    }

    /// <summary>O outro idioma (PT ↔ EN).</summary>
    public string NextLanguage => Languages[(Array.IndexOf(Languages, CurrentLanguage) + 1) % Languages.Length];

    public string T(string pt, string en) => IsEnglish ? en : pt;
}
