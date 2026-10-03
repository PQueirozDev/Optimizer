using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Design system: único lugar que transforma as preferências de aparência em tokens globais (recursos
/// dinâmicos). Os componentes só leem os tokens — nenhuma página decide cores, espaços ou animações sozinha.
///
/// Tokens de cor (nomes do app → equivalentes do design system):
///   BackgroundBrush (--background) · CardBgBrush/SurfaceBrush (--surface) · PanelBrush/SurfaceSecondaryBrush
///   (--surface-secondary) · PanelHoverBrush/SurfaceHoverBrush (--surface-hover) · TextBrush (--text-primary) ·
///   TextSecondaryBrush (--text-secondary) · MutedBrush (--text-muted) · AccentBrush/AccentHoverBrush/
///   AccentSoftBrush · SuccessBrush · WarningBrush · DangerBrush · BorderSubtleBrush (--border) ·
///   BorderHoverBrush (--border-hover).
/// Tokens de forma: CardRadius, CardPadding, ContentGap, ControlRadius; de movimento: FastDuration, NormalDuration.
/// </summary>
public static class ThemeService
{
    private sealed record Palette(string Background, string Sidebar, string Panel, string PanelHover, string Card, string Border, string BorderSubtle,
        string Text, string TextSecondary, string Muted, string Success, string Warning, string Danger, string Shadow);

    private static readonly Palette Dark = new("#07080C", "#F20A0B11", "#10121A", "#181B25", "#F20E1017", "#262B3A", "#181C27",
        "#F2F4FA", "#BFC5D4", "#8A91A6", "#34D399", "#FBBF24", "#F87171", "#000000");

    // OLED: preto puro no fundo; superfícies quase pretas para continuar havendo separação entre os blocos
    private static readonly Palette Oled = new("#000000", "#FF000000", "#0A0A0E", "#14141B", "#FF050507", "#22222C", "#131319",
        "#F2F4FA", "#BFC5D4", "#8A91A6", "#34D399", "#FBBF24", "#F87171", "#000000");

    private static readonly Palette Light = new("#F3F4F9", "#F7FFFFFF", "#F6F6FB", "#ECEDF5", "#FFFFFFFF", "#D8DBE8", "#E7E9F2",
        "#0D1120", "#3B4258", "#5D6580", "#059669", "#C26A05", "#DC2626", "#5B6488");

    /// <summary>Cor de destaque (roxo) e secundária (ciano) para cada intensidade.</summary>
    private static (Color Accent, Color AccentHover, Color Info, byte SoftAlpha, double Glow) AccentFor(AccentIntensity intensity, bool light) => (intensity, light) switch
    {
        (AccentIntensity.Soft, false) => (C("#8C82D2"), C("#A39AE0"), C("#5FB8CC"), 0x20, 0.15),
        (AccentIntensity.Vibrant, false) => (C("#9B5CFF"), C("#B488FF"), C("#22E1FF"), 0x38, 0.55),
        (AccentIntensity.Soft, true) => (C("#6E62B8"), C("#5D52A3"), C("#2A8597"), 0x18, 0.1),
        (AccentIntensity.Vibrant, true) => (C("#7A22F0"), C("#6514D6"), C("#0596B8"), 0x24, 0.35),
        (_, true) => (C("#7C3AED"), C("#6D28D9"), C("#0891B2"), 0x1C, 0.25),
        _ => (C("#8B5CF6"), C("#A78BFA"), C("#22D3EE"), 0x2B, 0.35),
    };

    public static bool IsDark { get; private set; } = true;

    /// <summary>Tema efetivo: Automático segue a configuração "modo de aplicativo" do Windows.</summary>
    public static bool ResolveDark(ThemeMode mode)
    {
        if (mode != ThemeMode.Auto) return true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not 1;
        }
        catch (System.Security.SecurityException) { return true; }
    }

    /// <summary>Aplica as preferências. <paramref name="forceLight"/> só é usado pela renderização de teste.</summary>
    public static void Apply(ResourceDictionary resources, AppearanceSettings settings, bool forceLight = false)
    {
        var dark = !forceLight && ResolveDark(settings.Theme);
        IsDark = dark;
        var palette = !dark ? Light : settings.Theme == ThemeMode.Oled ? Oled : Dark;
        var (accent, accentHover, info, softAlpha, glow) = AccentFor(settings.Accent, !dark);

        void Brush(string key, Color color) { var b = new SolidColorBrush(color); b.Freeze(); resources[key] = b; }
        Brush("BackgroundBrush", C(palette.Background)); Brush("HeaderBrush", C(palette.Background));
        Brush("SidebarBrush", C(palette.Sidebar));
        Brush("PanelBrush", C(palette.Panel)); Brush("SurfaceSecondaryBrush", C(palette.Panel));
        Brush("PanelHoverBrush", C(palette.PanelHover)); Brush("SurfaceHoverBrush", C(palette.PanelHover));
        Brush("CardBgBrush", C(palette.Card)); Brush("SurfaceBrush", C(palette.Card));
        Brush("BorderBrush", C(palette.Border)); Brush("BorderSubtleBrush", C(palette.BorderSubtle));
        Brush("TextBrush", C(palette.Text)); Brush("TextSecondaryBrush", C(palette.TextSecondary)); Brush("MutedBrush", C(palette.Muted));
        Brush("AccentBrush", accent); Brush("AccentHoverBrush", accentHover); Brush("AccentSoftBrush", WithAlpha(accent, softAlpha));
        Brush("OnAccentBrush", Colors.White);
        Brush("InfoBrush", info); Brush("InfoSoftBrush", WithAlpha(info, (byte)(dark ? 0x24 : 0x1F)));
        foreach (var (key, hex) in new[] { ("Success", palette.Success), ("Warning", palette.Warning), ("Danger", palette.Danger) })
        {
            Brush(key + "Brush", C(hex)); Brush(key + "SoftBrush", WithAlpha(C(hex), (byte)(dark ? 0x24 : 0x1F)));
        }
        // Borda ao passar o mouse: destaque discreto, sem gradiente chamativo
        Brush("BorderHoverBrush", WithAlpha(accent, (byte)(dark ? 0x80 : 0x70)));
        resources["ShadowColor"] = C(palette.Shadow);
        resources["AccentColor"] = accent;
        resources["GlowOpacity"] = glow;

        resources["AccentGradientBrush"] = Frozen(new LinearGradientBrush(accent, Blend(accent, info, 0.55), new Point(0, 0), new Point(1, 1)));

        // Borda dos cartões: a borda comum com um leve brilho do destaque no canto superior esquerdo
        var border = C(palette.BorderSubtle);
        var cardBorder = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        cardBorder.GradientStops.Add(new GradientStop(Blend(border, accent, dark ? 0.35 : 0.25), 0));
        cardBorder.GradientStops.Add(new GradientStop(border, 0.3));
        cardBorder.GradientStops.Add(new GradientStop(border, 1));
        resources["CardBorderBrush"] = Frozen(cardBorder);
        resources["CardHoverBorderBrush"] = resources["BorderHoverBrush"];

        var navSelected = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        navSelected.GradientStops.Add(new GradientStop(WithAlpha(accent, (byte)(softAlpha + (dark ? 0x10 : 0x08))), 0));
        navSelected.GradientStops.Add(new GradientStop(WithAlpha(accent, (byte)(softAlpha / 4)), 1));
        resources["NavSelectedBrush"] = Frozen(navSelected);

        var baseColor = C(palette.Card) with { A = 255 };
        var hero = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        hero.GradientStops.Add(new GradientStop(Blend(baseColor, accent, dark ? 0.2 : 0.08), 0));
        hero.GradientStops.Add(new GradientStop(Blend(baseColor, accent, dark ? 0.05 : 0.02), 0.6));
        hero.GradientStops.Add(new GradientStop(Blend(baseColor, info, dark ? 0.1 : 0.06), 1));
        resources["HeroBrush"] = Frozen(hero);

        var glowBrush = new RadialGradientBrush { Center = new Point(0.85, 0), GradientOrigin = new Point(0.85, 0), RadiusX = 0.7, RadiusY = 0.9 };
        glowBrush.GradientStops.Add(new GradientStop(WithAlpha(accent, (byte)(dark ? 0x22 : 0x14)), 0));
        glowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        resources["GlowBrush"] = Frozen(glowBrush);

        // Fundo aurora bem discreto (no OLED, quase nada, para o preto continuar preto)
        var aurora = settings.Theme == ThemeMode.Oled && dark ? 0.35 : dark ? 0.65 : 0.5;
        resources["AuroraA"] = WithAlpha(accent, (byte)(0x48 * aurora));
        resources["AuroraB"] = WithAlpha(info, (byte)(0x30 * aurora));
        resources["AuroraC"] = WithAlpha(C("#EC4899"), (byte)(0x22 * aurora));

        // Forma e espaço
        var density = settings.Density switch { Density.Compact => 0.78, Density.Comfortable => 1.22, _ => 1.0 };
        resources["CardRadius"] = new CornerRadius(16);
        resources["ControlRadius"] = new CornerRadius(10);
        resources["CardPadding"] = new Thickness(Math.Round(22 * density));
        resources["ContentGap"] = Math.Round(16 * density);
        resources["FastDuration"] = new Duration(TimeSpan.FromMilliseconds(settings.Animations ? 140 : 0));
        resources["NormalDuration"] = new Duration(TimeSpan.FromMilliseconds(settings.Animations ? 240 : 0));
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static T Frozen<T>(T freezable) where T : Freezable { freezable.Freeze(); return freezable; }
}
