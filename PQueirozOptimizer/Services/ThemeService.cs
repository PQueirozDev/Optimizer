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

    // Areia: claro quente e de menos contraste que o Claro, para quem acha o branco ofuscante
    private static readonly Palette Sand = new("#EFEBE4", "#F7F4EFE8", "#EAE5DC", "#E2DCD1", "#FFFAF8F4", "#D6CEC0", "#E3DDD2",
        "#1E1A15", "#4A4339", "#71685B", "#15803D", "#B45309", "#C2410C", "#6B5E4B");

    // Ameixa: escuro com fundo vinho/roxo bem fechado
    private static readonly Palette Plum = new("#140A16", "#F21A0E1D", "#22132A", "#2D1A36", "#F21D1023", "#45294F", "#301C38",
        "#F7F1F8", "#D2C3D6", "#9C8BA3", "#34D399", "#FBBF24", "#FB7185", "#000000");

    // Meia-noite: a paleta do Qrztweaks 2.0 (fundo #101016, cards #1C1A25, texto #F5F3FF, verde #22C55E, amarelo #F59E0B)
    private static readonly Palette Midnight = new("#101016", "#F213121A", "#17151F", "#211E2C", "#F21C1A25", "#2E2A3B", "#24212F",
        "#F5F3FF", "#CFC8E6", "#9A92B5", "#22C55E", "#F59E0B", "#F87171", "#000000");

    /// <summary>Temas de fundo claro; os demais (fora o Automático) são escuros.</summary>
    public static bool IsLightTheme(ThemeMode mode) => mode is ThemeMode.Light or ThemeMode.Sand;

    /// <summary>Cores de cada tema para as miniaturas da galeria: fundo, superfície e texto.</summary>
    public static (Color Background, Color Surface, Color Text) Swatch(ThemeMode mode)
    {
        var p = PaletteFor(mode, dark: !IsLightTheme(mode));
        return (C(p.Background), C(p.Card) with { A = 255 }, C(p.Text));
    }

    private static Palette PaletteFor(ThemeMode mode, bool dark) => !dark ? (mode == ThemeMode.Sand ? Sand : Light) : mode switch
    {
        ThemeMode.Plum => Plum,
        ThemeMode.Midnight => Midnight,
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
        // Meia-noite usa o roxo #7C3AED e o lilás #A78BFA do Qrztweaks 2.0 quando o usuário não escolheu outra cor
        (Color Accent, Color AccentHover, Color Info, byte SoftAlpha, double Glow) standard = settings.Theme == ThemeMode.Midnight && !light && settings.Accent == AccentIntensity.Default
            ? (C("#7C3AED"), C("#A78BFA"), C("#A78BFA"), (byte)0x2B, 0.35)
            : AccentFor(settings.Accent, light);
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

    /// <summary>Se o modo translúcido está ativo agora (ligado nas preferências e suportado pelo Windows).</summary>
    public static bool IsTranslucent { get; private set; }

    /// <summary>O fundo Acrylic da janela (DWMWA_SYSTEMBACKDROP_TYPE) só existe no Windows 11 22H2 em diante.</summary>
    public static bool TranslucencySupported => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// "Efeitos de transparência" do Windows (Personalização → Cores). Desligado, o Windows pinta o Acrylic
    /// como cor sólida em todos os apps, então o modo translúcido não tem como aparecer.
    /// </summary>
    public static bool SystemTransparencyEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("EnableTransparency") is not 0;
            }
            catch (System.Security.SecurityException) { return true; }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

    /// <summary>
    /// Liga os efeitos de transparência do Windows (a mesma chave da tela Personalização → Cores) e avisa
    /// os programas abertos, como o próprio Windows faz; o DWM passa a desenhar o Acrylic na hora.
    /// </summary>
    public static bool EnableSystemTransparency()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            key.SetValue("EnableTransparency", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { return false; }
        // HWND_BROADCAST + WM_SETTINGCHANGE "ImmersiveColorSet"; SMTO_ABORTIFHUNG para não travar num programa parado
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A, UIntPtr.Zero, "ImmersiveColorSet", 0x0002, 1000, out _);
        return true;
    }

    /// <summary>Tema efetivo: Automático segue a configuração "modo de aplicativo" do Windows.</summary>
    public static bool ResolveDark(ThemeMode mode)
    {
        if (IsLightTheme(mode)) return false;
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
        accent = EnsureAccentContrast(accent);

        void Brush(string key, Color color) { var b = new SolidColorBrush(color); b.Freeze(); resources[key] = b; }
        // Translúcido: as superfícies ficam parcialmente transparentes e o Acrylic do Windows aparece por trás
        var translucent = !forceLight && settings.Translucent && TranslucencySupported && SystemTransparencyEnabled;
        IsTranslucent = translucent;
        Color Surface(string hex, byte alpha) => translucent ? WithAlpha(C(hex), alpha) : C(hex);
        Brush("BackgroundBrush", Surface(palette.Background, 0x55)); Brush("HeaderBrush", Surface(palette.Background, 0x55));
        Brush("SidebarBrush", Surface(palette.Sidebar, 0x70));
        Brush("PanelBrush", Surface(palette.Panel, 0xB0)); Brush("SurfaceSecondaryBrush", Surface(palette.Panel, 0xB0));
        Brush("PanelHoverBrush", Surface(palette.PanelHover, 0xC8)); Brush("SurfaceHoverBrush", Surface(palette.PanelHover, 0xC8));
        Brush("CardBgBrush", Surface(palette.Card, 0xA8)); Brush("SurfaceBrush", Surface(palette.Card, 0xA8));
        Brush("BorderBrush", C(palette.Border)); Brush("BorderSubtleBrush", C(palette.BorderSubtle));
        Brush("TextBrush", C(palette.Text)); Brush("TextSecondaryBrush", C(palette.TextSecondary)); Brush("MutedBrush", C(palette.Muted));
        Brush("AccentBrush", accent); Brush("AccentHoverBrush", accentHover); Brush("AccentSoftBrush", WithAlpha(accent, softAlpha));
        Brush("OnAccentBrush", BestAccentText(accent));
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

        // Cor sólida: o nome "Gradient" ficou pelos estilos que já usam a chave (o splash lê as duas paradas)
        resources["AccentGradientBrush"] = Frozen(new LinearGradientBrush(accent, accent, new Point(0, 0), new Point(1, 1)));

        resources["CardBorderBrush"] = resources["BorderSubtleBrush"];
        resources["CardHoverBorderBrush"] = resources["BorderBrush"];
        Brush("NavSelectedBrush", WithAlpha(accent, softAlpha));

        // Destaque de cartões principais: só um leve tom do roxo sobre o fundo do cartão, sem gradiente
        var baseColor = C(palette.Card) with { A = 255 };
        Brush("HeroBrush", translucent ? WithAlpha(Blend(baseColor, accent, dark ? 0.08 : 0.04), 0xB8) : Blend(baseColor, accent, dark ? 0.08 : 0.04));
        Brush("GlowBrush", Colors.Transparent);

        // Forma e espaço
        var density = settings.Density switch { Density.Compact => 0.78, Density.Comfortable => 1.22, _ => 1.0 };
        resources["CardRadius"] = new CornerRadius(14);
        resources["ControlRadius"] = new CornerRadius(10);
        resources["CardPadding"] = new Thickness(Math.Round(22 * density));
        resources["ContentGap"] = Math.Round(16 * density);
        resources["FastDuration"] = new Duration(TimeSpan.FromMilliseconds(settings.Animations ? 140 : 0));
        resources["NormalDuration"] = new Duration(TimeSpan.FromMilliseconds(settings.Animations ? 240 : 0));
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static Color BestAccentText(Color accent)
    {
        static double Channel(byte value)
        {
            var v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Channel(accent.R) + 0.7152 * Channel(accent.G) + 0.0722 * Channel(accent.B);
        var whiteContrast = 1.05 / (luminance + 0.05);
        var blackContrast = (luminance + 0.05) / 0.05;
        return blackContrast >= whiteContrast ? Colors.Black : Colors.White;
    }
    private static Color EnsureAccentContrast(Color color)
    {
        static double L(Color c)
        {
            static double Channel(byte value)
            {
                var v = value / 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }
        static double Ratio(Color a, Color b)
        {
            var x = L(a); var y = L(b);
            return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }
        if (Math.Max(Ratio(color, Colors.Black), Ratio(color, Colors.White)) >= 4.5) return color;
        for (var i = 1; i <= 100; i++)
        {
            var amount = i / 100.0;
            var dark = Blend(color, Colors.Black, amount);
            var light = Blend(color, Colors.White, amount);
            if (Math.Max(Ratio(dark, Colors.Black), Ratio(dark, Colors.White)) >= 4.5) return dark;
            if (Math.Max(Ratio(light, Colors.Black), Ratio(light, Colors.White)) >= 4.5) return light;
        }
        return color;
    }
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
