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

    // Grafite: cinza neutro, sem o tom azulado do Escuro
    private static readonly Palette Graphite = new("#0E0F11", "#F2131417", "#17181B", "#202226", "#F2151619", "#2C2E33", "#1F2125",
        "#F3F4F6", "#C4C7CE", "#8E929B", "#34D399", "#FBBF24", "#F87171", "#000000");

    // Oceano: azul-marinho profundo
    private static readonly Palette Ocean = new("#060B14", "#F2081120", "#0D1626", "#142036", "#F20B1424", "#1F2E4A", "#16233A",
        "#EEF4FF", "#B7C6E0", "#7F92B4", "#34D399", "#FBBF24", "#F87171", "#000000");

    // Floresta: verde quase preto
    private static readonly Palette Forest = new("#060D0A", "#F2091410", "#0E1A15", "#15251E", "#F20B1712", "#1F3329", "#172820",
        "#EEF7F1", "#BCD3C5", "#81A08E", "#4ADE80", "#FBBF24", "#F87171", "#000000");

    /// <summary>Cores de cada tema para as miniaturas da galeria: fundo, superfície e texto.</summary>
    public static (Color Background, Color Surface, Color Text) Swatch(ThemeMode mode)
    {
        var p = PaletteFor(mode, dark: mode != ThemeMode.Light);
        return (C(p.Background), C(p.Card) with { A = 255 }, C(p.Text));
    }

    private static Palette PaletteFor(ThemeMode mode, bool dark) => !dark ? Light : mode switch
    {
        ThemeMode.Oled => Oled,
        ThemeMode.Graphite => Graphite,
        ThemeMode.Ocean => Ocean,
        ThemeMode.Forest => Forest,
        _ => Dark,
    };

    /// <summary>Lê "#RRGGBB" (ou "RRGGBB"); null quando vazio ou inválido.</summary>
    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var text = hex.Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value)) return null;
        return Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>
    /// Cor principal e secundária escolhidas pelo usuário, ajustadas à intensidade e ao tema: Suave tira
    /// saturação, Vibrante deixa mais viva; no tema claro a cor escurece para manter o contraste.
    /// Sem escolha, usa as cores originais do app (o roxo e o ciano calibrados à mão abaixo).
    /// </summary>
    private static (Color Accent, Color AccentHover, Color Info, byte SoftAlpha, double Glow) AccentFor(AppearanceSettings settings, bool light)
    {
        var standard = AccentFor(settings.Accent, light);
        var customAccent = ParseColor(settings.AccentColor);
        var customInfo = ParseColor(settings.SecondaryColor);
        if (customAccent is null && customInfo is null) return standard;

        Color Tune(Color c)
        {
            var (h, s, l) = ToHsl(c);
            (s, l) = settings.Accent switch
            {
                AccentIntensity.Soft => (s * 0.62, l + (0.62 - l) * 0.35),
                AccentIntensity.Vibrant => (Math.Min(1, s * 1.18 + 0.05), l + (0.6 - l) * 0.2),
                _ => (s, l),
            };
            l = light ? Math.Min(l, 0.46) : Math.Max(l, 0.5);
            return FromHsl(h, s, l);
        }
        var accent = customAccent is { } a ? Tune(a) : standard.Accent;
        var info = customInfo is { } i ? Tune(i) : standard.Info;
        var (ah, asat, al) = ToHsl(accent);
        var hover = FromHsl(ah, asat, light ? al - 0.07 : Math.Min(0.85, al + 0.08));
        return (accent, hover, info, standard.SoftAlpha, standard.Glow);
    }

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
        if (mode == ThemeMode.Light) return false;
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
        var palette = PaletteFor(settings.Theme, dark);
        var (accent, accentHover, info, softAlpha, glow) = AccentFor(settings, !dark);

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

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d < 1e-6) return (0, 0, l);
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        s = Math.Clamp(s, 0, 1); l = Math.Clamp(l, 0, 1);
        if (s < 1e-6) { var v = (byte)Math.Round(l * 255); return Color.FromRgb(v, v, v); }
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
        var q2 = l < 0.5 ? l * (1 + s) : l + s - l * s; var p2 = 2 * l - q2;
        return Color.FromRgb((byte)Math.Round(Hue(p2, q2, h + 1.0 / 3) * 255), (byte)Math.Round(Hue(p2, q2, h) * 255), (byte)Math.Round(Hue(p2, q2, h - 1.0 / 3) * 255));
    }
}
