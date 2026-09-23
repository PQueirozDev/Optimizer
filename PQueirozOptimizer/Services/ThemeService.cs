using System.Windows;
using System.Windows.Media;

namespace PQueirozOptimizer.Services;

/// <summary>Paletas claro/escuro aplicadas aos recursos dinâmicos da aplicação.</summary>
public static class ThemeService
{
    private static readonly Dictionary<string, string> Dark = new()
    {
        ["BackgroundBrush"] = "#0A0B10",
        ["HeaderBrush"] = "#0A0B10",
        ["SidebarBrush"] = "#0E1017",
        ["PanelBrush"] = "#12141C",
        ["PanelHoverBrush"] = "#1B1F2D",
        ["CardBgBrush"] = "#141722",
        ["BorderBrush"] = "#242A3A",
        ["BorderSubtleBrush"] = "#1A1E2A",
        ["TextBrush"] = "#EEF0F6",
        ["MutedBrush"] = "#8A91A6",
        ["AccentBrush"] = "#7B61FF",
        ["AccentHoverBrush"] = "#927CFF",
        ["AccentSoftBrush"] = "#267B61FF",
        ["OnAccentBrush"] = "#FFFFFF",
        ["SuccessBrush"] = "#22C55E",
        ["SuccessSoftBrush"] = "#2622C55E",
        ["WarningBrush"] = "#F5A524",
        ["WarningSoftBrush"] = "#26F5A524",
        ["DangerBrush"] = "#F04438",
        ["DangerSoftBrush"] = "#26F04438",
        ["InfoBrush"] = "#3FA9F5",
        ["InfoSoftBrush"] = "#263FA9F5",
        ["ShadowColor"] = "#000000",
    };

    private static readonly Dictionary<string, string> Light = new()
    {
        ["BackgroundBrush"] = "#F4F5FA",
        ["HeaderBrush"] = "#F4F5FA",
        ["SidebarBrush"] = "#FFFFFF",
        ["PanelBrush"] = "#FFFFFF",
        ["PanelHoverBrush"] = "#EEF0F7",
        ["CardBgBrush"] = "#FFFFFF",
        ["BorderBrush"] = "#E1E4EE",
        ["BorderSubtleBrush"] = "#ECEEF5",
        ["TextBrush"] = "#0E1220",
        ["MutedBrush"] = "#5F6780",
        ["AccentBrush"] = "#5B3FF0",
        ["AccentHoverBrush"] = "#4B30DD",
        ["AccentSoftBrush"] = "#1A5B3FF0",
        ["OnAccentBrush"] = "#FFFFFF",
        ["SuccessBrush"] = "#16A34A",
        ["SuccessSoftBrush"] = "#1F16A34A",
        ["WarningBrush"] = "#D97706",
        ["WarningSoftBrush"] = "#1FD97706",
        ["DangerBrush"] = "#DC2626",
        ["DangerSoftBrush"] = "#1FDC2626",
        ["InfoBrush"] = "#1F8FE0",
        ["InfoSoftBrush"] = "#1F1F8FE0",
        ["ShadowColor"] = "#5B6488",
    };

    public static void Apply(ResourceDictionary resources, bool dark)
    {
        var palette = dark ? Dark : Light;
        foreach (var (key, hex) in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            if (key == "ShadowColor") { resources[key] = color; continue; }
            var brush = new SolidColorBrush(color); brush.Freeze();
            resources[key] = brush;
        }

        var start = (Color)ColorConverter.ConvertFromString(palette["AccentBrush"]);
        var end = (Color)ColorConverter.ConvertFromString(palette["InfoBrush"]);
        resources["AccentGradientBrush"] = Frozen(new LinearGradientBrush(start, end, new Point(0, 0), new Point(1, 1)));

        // Brilho sutil atrás do conteúdo (dá profundidade sem distrair)
        var glow = new RadialGradientBrush { Center = new Point(0.85, 0), GradientOrigin = new Point(0.85, 0), RadiusX = 0.7, RadiusY = 0.9 };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x2A : (byte)0x18, start.R, start.G, start.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x10 : (byte)0x0A, end.R, end.G, end.B), 0.45));
        glow.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        resources["GlowBrush"] = Frozen(glow);

        var hero = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        hero.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x40 : (byte)0x22, start.R, start.G, start.B), 0));
        hero.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x18 : (byte)0x10, end.R, end.G, end.B), 1));
        resources["HeroBrush"] = Frozen(hero);
    }

    private static T Frozen<T>(T freezable) where T : Freezable { freezable.Freeze(); return freezable; }
}
