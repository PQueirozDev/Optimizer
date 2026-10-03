using System.Windows;
using System.Windows.Media;

namespace PQueirozOptimizer.Services;

/// <summary>Paletas claro/escuro aplicadas aos recursos dinâmicos da aplicação.</summary>
public static class ThemeService
{
    // Cartões e barra lateral são levemente translúcidos para o fundo aurora aparecer por trás
    private static readonly Dictionary<string, string> Dark = new()
    {
        ["BackgroundBrush"] = "#06070C",
        ["HeaderBrush"] = "#06070C",
        ["SidebarBrush"] = "#D90A0B12",
        ["PanelBrush"] = "#11131C",
        ["PanelHoverBrush"] = "#1A1D2A",
        ["CardBgBrush"] = "#E60F111A",
        ["BorderBrush"] = "#2A3044",
        ["BorderSubtleBrush"] = "#1A1E2B",
        ["TextBrush"] = "#F2F4FA",
        ["MutedBrush"] = "#8F97AD",
        ["AccentBrush"] = "#8B5CF6",
        ["AccentHoverBrush"] = "#A78BFA",
        ["AccentSoftBrush"] = "#2B8B5CF6",
        ["OnAccentBrush"] = "#FFFFFF",
        ["SuccessBrush"] = "#34D399",
        ["SuccessSoftBrush"] = "#2434D399",
        ["WarningBrush"] = "#FBBF24",
        ["WarningSoftBrush"] = "#24FBBF24",
        ["DangerBrush"] = "#F87171",
        ["DangerSoftBrush"] = "#24F87171",
        ["InfoBrush"] = "#22D3EE",
        ["InfoSoftBrush"] = "#2422D3EE",
        ["ShadowColor"] = "#000000",
    };

    private static readonly Dictionary<string, string> Light = new()
    {
        ["BackgroundBrush"] = "#F3F4FA",
        ["HeaderBrush"] = "#F3F4FA",
        ["SidebarBrush"] = "#E6FFFFFF",
        ["PanelBrush"] = "#F7F7FC",
        ["PanelHoverBrush"] = "#ECEDF6",
        ["CardBgBrush"] = "#F2FFFFFF",
        ["BorderBrush"] = "#DADDEA",
        ["BorderSubtleBrush"] = "#E8EAF3",
        ["TextBrush"] = "#0D1120",
        ["MutedBrush"] = "#5D6580",
        ["AccentBrush"] = "#7C3AED",
        ["AccentHoverBrush"] = "#6D28D9",
        ["AccentSoftBrush"] = "#1C7C3AED",
        ["OnAccentBrush"] = "#FFFFFF",
        ["SuccessBrush"] = "#059669",
        ["SuccessSoftBrush"] = "#1F059669",
        ["WarningBrush"] = "#D97706",
        ["WarningSoftBrush"] = "#1FD97706",
        ["DangerBrush"] = "#DC2626",
        ["DangerSoftBrush"] = "#1FDC2626",
        ["InfoBrush"] = "#0891B2",
        ["InfoSoftBrush"] = "#1F0891B2",
        ["ShadowColor"] = "#5B6488",
    };

    public static bool IsDark { get; private set; } = true;

    public static void Apply(ResourceDictionary resources, bool dark)
    {
        IsDark = dark;
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
        resources["AccentColor"] = start;
        resources["AccentGradientBrush"] = Frozen(new LinearGradientBrush(start, end, new Point(0, 0), new Point(1, 1)));

        // Borda dos cartões: um brilho da cor de destaque no canto superior que se dissolve na borda comum
        var border = (Color)ColorConverter.ConvertFromString(palette["BorderSubtleBrush"]);
        var cardBorder = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        cardBorder.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x70 : (byte)0x45, start.R, start.G, start.B), 0));
        cardBorder.GradientStops.Add(new GradientStop(border, 0.35));
        cardBorder.GradientStops.Add(new GradientStop(border, 0.8));
        cardBorder.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x40 : (byte)0x30, end.R, end.G, end.B), 1));
        resources["CardBorderBrush"] = Frozen(cardBorder);

        var cardHover = new LinearGradientBrush(Color.FromArgb(0xC0, start.R, start.G, start.B), Color.FromArgb(0x90, end.R, end.G, end.B), new Point(0, 0), new Point(1, 1));
        resources["CardHoverBorderBrush"] = Frozen(cardHover);

        var navSelected = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        navSelected.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x48 : (byte)0x26, start.R, start.G, start.B), 0));
        navSelected.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x10 : (byte)0x08, end.R, end.G, end.B), 1));
        resources["NavSelectedBrush"] = Frozen(navSelected);

        // Mantidos por compatibilidade com telas que ainda usam o brilho estático
        var glow = new RadialGradientBrush { Center = new Point(0.85, 0), GradientOrigin = new Point(0.85, 0), RadiusX = 0.7, RadiusY = 0.9 };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(dark ? (byte)0x2A : (byte)0x18, start.R, start.G, start.B), 0));
        glow.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        resources["GlowBrush"] = Frozen(glow);

        // Destaque: cor de destaque misturada ao cartão nos cantos, com a mesma opacidade em toda a área
        // (paradas com transparências diferentes criavam uma faixa no meio)
        var baseColor = dark ? Color.FromRgb(0x10, 0x12, 0x1C) : Colors.White;
        static Color Mix(Color a, Color b, double t) => Color.FromArgb(0xF2, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        var hero = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        hero.GradientStops.Add(new GradientStop(Mix(baseColor, start, dark ? 0.33 : 0.13), 0));
        hero.GradientStops.Add(new GradientStop(Mix(baseColor, start, dark ? 0.08 : 0.03), 0.55));
        hero.GradientStops.Add(new GradientStop(Mix(baseColor, end, dark ? 0.25 : 0.12), 1));
        resources["HeroBrush"] = Frozen(hero);

        // Cores das manchas do fundo aurora
        resources["AuroraA"] = Color.FromArgb(dark ? (byte)0x55 : (byte)0x30, start.R, start.G, start.B);
        resources["AuroraB"] = Color.FromArgb(dark ? (byte)0x3A : (byte)0x24, end.R, end.G, end.B);
        resources["AuroraC"] = Color.FromArgb(dark ? (byte)0x2E : (byte)0x1C, 0xEC, 0x48, 0x99);
    }

    private static T Frozen<T>(T freezable) where T : Freezable { freezable.Freeze(); return freezable; }
}
