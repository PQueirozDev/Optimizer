using System.Text.Json.Serialization;

namespace PQueirozOptimizer.Models;

public enum ThemeMode { Dark, Oled, Auto }
public enum AccentIntensity { Soft, Default, Vibrant }
public enum Density { Compact, Default, Comfortable }
public enum CardSize { Compact, Medium, Large }

/// <summary>Preferências visuais. Só mudam a aparência, nunca o comportamento do app.</summary>
public sealed class AppearanceSettings
{
    [JsonPropertyName("theme"), JsonConverter(typeof(JsonStringEnumConverter))] public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    [JsonPropertyName("accent"), JsonConverter(typeof(JsonStringEnumConverter))] public AccentIntensity Accent { get; set; } = AccentIntensity.Default;
    [JsonPropertyName("density"), JsonConverter(typeof(JsonStringEnumConverter))] public Density Density { get; set; } = Density.Default;
    [JsonPropertyName("cardSize"), JsonConverter(typeof(JsonStringEnumConverter))] public CardSize CardSize { get; set; } = CardSize.Medium;
    [JsonPropertyName("animations")] public bool Animations { get; set; } = true;

    public AppearanceSettings Clone() => (AppearanceSettings)MemberwiseClone();
}
