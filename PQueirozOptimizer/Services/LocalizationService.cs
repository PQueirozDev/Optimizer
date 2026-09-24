namespace PQueirozOptimizer.Services;

public class LocalizationService
{
    public string CurrentLanguage { get; private set; } = "pt";

    public bool IsEnglish => CurrentLanguage.Equals("en", StringComparison.OrdinalIgnoreCase);

    public void SetLanguage(string language)
    {
        CurrentLanguage = language.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "pt";
        Translator.IsEnglish = IsEnglish;
    }

    public string ToggleLanguage()
    {
        SetLanguage(IsEnglish ? "pt" : "en");
        return CurrentLanguage;
    }

    public string T(string pt, string en) => IsEnglish ? en : pt;
}
