namespace PQueirozOptimizer.Services;

public class LocalizationService
{
    /// <summary>Idiomas na ordem do botão do topo: português (original), inglês e espanhol.</summary>
    public static readonly string[] Languages = { "pt", "en", "es" };

    public string CurrentLanguage { get; private set; } = "pt";

    public bool IsEnglish => CurrentLanguage == "en";

    public static string Normalize(string? language) => Languages.FirstOrDefault(l => l.Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "pt";

    public void SetLanguage(string language)
    {
        CurrentLanguage = Normalize(language);
        Translator.Language = CurrentLanguage;
    }

    /// <summary>Próximo idioma da lista (PT → EN → ES → PT).</summary>
    public string NextLanguage => Languages[(Array.IndexOf(Languages, CurrentLanguage) + 1) % Languages.Length];

    /// <summary>Texto com versão própria em inglês; em espanhol, usa o dicionário de tradução (ou o português).</summary>
    public string T(string pt, string en) => CurrentLanguage switch { "en" => en, "es" => Translator.Tr(pt), _ => pt };
}
