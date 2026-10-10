using System.Text.Json.Serialization;

namespace PQueirozOptimizer.Models;

/// <summary>Temas (gravados pelo nome, então a lista pode crescer sem quebrar preferências salvas).</summary>
public enum ThemeMode { Dark, Oled, Auto, Light, Graphite, Ocean, Forest, Sand, Plum, Midnight }
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
    /// <summary>Fundo translúcido (Acrylic do Windows 11) atrás da janela.</summary>
    [JsonPropertyName("translucent")] public bool Translucent { get; set; }
    /// <summary>Cor principal (#RRGGBB); vazio = roxo padrão do app.</summary>
    [JsonPropertyName("accentColor")] public string? AccentColor { get; set; }
    /// <summary>Cor secundária, usada nos gradientes e indicadores (#RRGGBB); vazio = ciano padrão.</summary>
    [JsonPropertyName("secondaryColor")] public string? SecondaryColor { get; set; }
    /// <summary>Desliga as animações quando "Mostrar animações no Windows" estiver desligado (redução de movimento).</summary>
    [JsonPropertyName("followSystemMotion")] public bool FollowSystemMotion { get; set; }
    /// <summary>Barra lateral só com ícones (os nomes aparecem nas dicas).</summary>
    [JsonPropertyName("compactSidebar")] public bool CompactSidebar { get; set; }
    // Seções do Command Center
    [JsonPropertyName("dashStatus")] public bool DashboardStatus { get; set; } = true;
    [JsonPropertyName("dashQuickActions")] public bool DashboardQuickActions { get; set; } = true;
    [JsonPropertyName("dashLive")] public bool DashboardLive { get; set; } = true;
    [JsonPropertyName("dashHardware")] public bool DashboardHardware { get; set; } = true;
    [JsonPropertyName("dashActivity")] public bool DashboardActivity { get; set; } = true;

    public AppearanceSettings Clone() => (AppearanceSettings)MemberwiseClone();
}
