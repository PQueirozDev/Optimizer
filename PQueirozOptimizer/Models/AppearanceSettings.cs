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
    [JsonPropertyName("theme"), JsonConverter(typeof(LenientEnumConverter<ThemeMode>))] public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    [JsonPropertyName("accent"), JsonConverter(typeof(LenientEnumConverter<AccentIntensity>))] public AccentIntensity Accent { get; set; } = AccentIntensity.Default;
    [JsonPropertyName("density"), JsonConverter(typeof(LenientEnumConverter<Density>))] public Density Density { get; set; } = Density.Default;
    [JsonPropertyName("cardSize"), JsonConverter(typeof(LenientEnumConverter<CardSize>))] public CardSize CardSize { get; set; } = CardSize.Medium;
    [JsonPropertyName("animations")] public bool Animations { get; set; } = true;
    /// <summary>Fundo translúcido (Acrylic do Windows 11) atrás da janela.</summary>
    [JsonPropertyName("translucent")] public bool Translucent { get; set; }
    /// <summary>Cor principal (#RRGGBB); vazio = azul padrão do app.</summary>
    [JsonPropertyName("accentColor")] public string? AccentColor { get; set; }
    /// <summary>Cor secundária, usada nos medidores e indicadores (#RRGGBB); vazio = azul-céu padrão.</summary>
    [JsonPropertyName("secondaryColor")] public string? SecondaryColor { get; set; }
    /// <summary>Desliga as animações quando "Mostrar animações no Windows" estiver desligado (redução de movimento).</summary>
    [JsonPropertyName("followSystemMotion")] public bool FollowSystemMotion { get; set; }
    /// <summary>Barra lateral só com ícones (os nomes aparecem nas dicas).</summary>
    [JsonPropertyName("compactSidebar")] public bool CompactSidebar { get; set; }
    // Seções do Início
    [JsonPropertyName("dashStatus")] public bool DashboardStatus { get; set; } = true;
    [JsonPropertyName("dashQuickActions")] public bool DashboardQuickActions { get; set; } = true;
    [JsonPropertyName("dashLive")] public bool DashboardLive { get; set; } = true;
    [JsonPropertyName("dashHardware")] public bool DashboardHardware { get; set; } = true;
    [JsonPropertyName("dashActivity")] public bool DashboardActivity { get; set; } = true;
    /// <summary>Categorias recolhidas na barra lateral (main, optimize, performance, tools, system); null = todas abertas.</summary>
    [JsonPropertyName("navCollapsed")] public List<string>? CollapsedNavGroups { get; set; }

    public AppearanceSettings Clone()
    {
        var copy = (AppearanceSettings)MemberwiseClone();
        copy.CollapsedNavGroups = CollapsedNavGroups?.ToList();
        return copy;
    }
}

/// <summary>Enum gravado pelo nome; um nome desconhecido (preferência de uma versão mais nova) vira o padrão
/// em vez de invalidar o arquivo inteiro de configurações.</summary>
public sealed class LenientEnumConverter<T> : System.Text.Json.Serialization.JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), true, out var named) && Enum.IsDefined(named)) return named;
        if (reader.TokenType == System.Text.Json.JsonTokenType.Number && reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(T), number)) return (T)Enum.ToObject(typeof(T), number);
        if (reader.TokenType is System.Text.Json.JsonTokenType.StartObject or System.Text.Json.JsonTokenType.StartArray) reader.Skip();
        return Fallback;
    }

    // O valor "do meio" de cada escolha (Default/Medium) quando existe; senão o primeiro
    private static readonly T Fallback = Enum.TryParse<T>("Default", out var d) ? d : Enum.TryParse<T>("Medium", out var m) ? m : default;

    public override void Write(System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
