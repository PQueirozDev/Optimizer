using System.Text.Json.Serialization;

namespace PQueirozOptimizer.Models;

public class AppConfig
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "Dark";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "pt";

    [JsonPropertyName("activeProfile")]
    public string ActiveProfile { get; set; } = "Padrão";

    [JsonPropertyName("voiceNotification")]
    public bool VoiceNotification { get; set; } = true;

    [JsonPropertyName("profiles")]
    public List<OptimizationProfile> Profiles { get; set; } = new();

    [JsonPropertyName("isoCatalog")]
    public List<IsoEntry> IsoCatalog { get; set; } = new();

    /// <summary>Aparência (Configurações → Aparência). Ausente no arquivo antigo: usa os padrões.</summary>
    [JsonPropertyName("appearance")]
    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>Tutoriais já vistos ("inicio", "recursos", "modo-jogo"...): cada um aparece só na primeira vez.</summary>
    [JsonPropertyName("completedTutorials")]
    public List<string> CompletedTutorials { get; set; } = new();
}

public class OptimizationProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; }

    [JsonPropertyName("enabledOptimizations")]
    public List<string> EnabledOptimizations { get; set; } = new();
}

public class IsoEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = "x64";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("localPath")]
    public string LocalPath { get; set; } = "";

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("changelog")]
    public string Changelog { get; set; } = "";

    [JsonPropertyName("requirements")]
    public string Requirements { get; set; } = "";
}

public class OptimizationDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Operation { get; set; } = "";
}
