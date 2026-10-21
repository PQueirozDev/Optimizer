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

    // Aurora (padrão): preto azulado no fundo com um brilho azul no alto, cards de "vidro" em azul-ardósia bem escuro,
    // texto quase branco e o azul como destaque (ciano na segunda série dos gráficos e no fim dos degradês).
    private static readonly Palette Dark = new("#03060C", "#F2070B14", "#0E1420", "#162033", "#FF0A0F19", "#1F2A3C", "#162031",
        "#E6EDF7", "#B4C2D6", "#8090AA", "#34D399", "#FBBF24", "#F87171", "#000000");

    // OLED: preto puro no fundo; superfícies quase pretas para continuar havendo separação entre os blocos
    private static readonly Palette Oled = new("#000000", "#FF000000", "#0A0A0E", "#14141B", "#FF050507", "#22222C", "#131319",
        "#F2F4FA", "#BFC5D4", "#8A91A6", "#34D399", "#FBBF24", "#F87171", "#000000");

    // Claro: branco, texto quase preto e o mesmo azul
    private static readonly Palette Light = new("#F3F5F9", "#F7FFFFFF", "#F4F6FA", "#EAEDF3", "#FFFFFFFF", "#D9DEE7", "#E6E9F0",
        "#0A0D14", "#353C4A", "#5C6576", "#059669", "#C26A05", "#DC2626", "#5B6488");

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

    // Roxo meia-noite: a paleta roxa do Qrztweaks 2.0 (fundo #101016, cards #1C1A25, texto #F5F3FF, verde #22C55E, amarelo #F59E0B)
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
    /// Sem escolha, usa as cores originais do app (o azul e o azul-céu calibrados à mão abaixo).
    /// </summary>
    /// <summary>O tema padrão (Escuro, ou Automático no modo escuro) é o Aurora: azul e ciano sobre preto azulado.</summary>
    private static bool IsAurora(AppearanceSettings settings, bool light) => !light && PaletteFor(settings.Theme, dark: true) == Dark;

    private static (Color Accent, Color AccentHover, Color Info, byte SoftAlpha, double Glow) AccentFor(AppearanceSettings settings, bool light)
    {
        // Roxo meia-noite usa o roxo #7C3AED e o lilás #A78BFA do Qrztweaks 2.0 quando o usuário não escolheu outra cor
        (Color Accent, Color AccentHover, Color Info, byte SoftAlpha, double Glow) standard = settings.Theme == ThemeMode.Midnight && !light && settings.Accent == AccentIntensity.Default
            ? (C("#7C3AED"), C("#A78BFA"), C("#A78BFA"), (byte)0x2B, 0.35)
            : IsAurora(settings, light) ? AuroraAccent(settings.Accent)
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

    /// <summary>
    /// Cor de destaque (ciano) e secundária (azul, para a segunda série dos gráficos) em cada intensidade. O ciano é
    /// claro, então botões e selos preenchidos com ele usam texto escuro (ver <see cref="BestAccentText"/>).
    /// </summary>
    private static (Color Accent, Color AccentHover, Color Info, byte SoftAlpha, double Glow) AccentFor(AccentIntensity intensity, bool light) => (intensity, light) switch
    {
        (AccentIntensity.Soft, false) => (C("#5FB8C8"), C("#7DCAD7"), C("#7F9FD6"), 0x1E, 0.15),
        (AccentIntensity.Vibrant, false) => (C("#00DDF7"), C("#5AEBFB"), C("#4D8DFF"), 0x30, 0.55),
        (AccentIntensity.Soft, true) => (C("#3D7F8C"), C("#336E79"), C("#4A69A8"), 0x18, 0.1),
        (AccentIntensity.Vibrant, true) => (C("#0087A3"), C("#00748C"), C("#1D5BFF"), 0x24, 0.35),
        (_, true) => (C("#0E7F96"), C("#0B6C80"), C("#2563EB"), 0x1C, 0.25),
        _ => (C("#1FC8E3"), C("#5ADAEE"), C("#4C8DFF"), 0x26, 0.4),
    };

    /// <summary>Destaque do Aurora: azul (Padrão), azul mais vivo (Vibrante) ou acinzentado (Suave); a segunda série é ciano.</summary>
    private static (Color Accent, Color AccentHover, Color Info, byte SoftAlpha, double Glow) AuroraAccent(AccentIntensity intensity) => intensity switch
    {
        AccentIntensity.Soft => (C("#5B8BD9"), C("#7BA3E3"), C("#5FC9DA"), 0x24, 0.3),
        AccentIntensity.Vibrant => (C("#2F7BFF"), C("#5C9BFF"), C("#06D6F5"), 0x38, 0.6),
        _ => (C("#3B82F6"), C("#60A5FA"), C("#22D3EE"), 0x2E, 0.5),
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
        // Aurora: cards de vidro (fio claro no alto), brilho azul grande no topo e degradê azul → ciano
        var aurora = IsAurora(settings, !dark);

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
            // Texto/ícone sobre o tom preenchido (selos numerados, marcas de seleção): escuro ou branco pelo contraste
            Brush("On" + key + "Brush", BestAccentText(C(hex)) == Colors.Black ? C("#0B1210") : Colors.White);
        }
        Brush("OnInfoBrush", BestAccentText(info) == Colors.Black ? C("#04151A") : Colors.White);
        // Borda ao passar o mouse: destaque discreto, sem gradiente chamativo
        Brush("BorderHoverBrush", WithAlpha(accent, (byte)(dark ? 0x80 : 0x70)));
        // Borda neutra mais forte (hover de botões e cards sem destaque)
        Brush("BorderStrongBrush", Blend(C(palette.Border), dark ? Colors.White : Colors.Black, dark ? 0.12 : 0.14));
        resources["ShadowColor"] = C(palette.Shadow);
        resources["AccentColor"] = accent;
        resources["InfoColor"] = info;
        resources["GlowOpacity"] = glow;

        // Do destaque para a secundária, só um passo (não um arco-íris): anel de saúde, splash e capas sem imagem
        resources["AccentGradientBrush"] = Frozen(aurora
            ? new LinearGradientBrush(PrimaryPair(accent).Fill, info, new Point(0, 0), new Point(1, 1))
            : new LinearGradientBrush(accent, Blend(accent, info, 0.6), new Point(0, 0), new Point(1, 1)));

        // Borda dos cards: um fio mais claro no alto que some para baixo (a luz "de cima" dos apps de hardware),
        // em vez de uma caixa com contorno igual nos quatro lados
        var edge = C(palette.BorderSubtle);
        resources["CardBorderBrush"] = Frozen(new LinearGradientBrush(dark ? Blend(edge, Colors.White, 0.07) : edge, dark ? WithAlpha(edge, 0x90) : edge, new Point(0, 0), new Point(0, 1)));
        resources["CardHoverBorderBrush"] = resources["BorderBrush"];

        // Navegação: item ativo com um véu da cor de destaque, ícone e barrinha no destaque
        Brush("NavSelectedBrush", WithAlpha(accent, (byte)(dark ? 0x17 : 0x14)));
        Brush("NavSelectedBorderBrush", WithAlpha(accent, (byte)(dark ? 0x26 : 0x22)));
        Brush("NavHoverBrush", dark ? WithAlpha(Colors.White, 0x08) : WithAlpha(Colors.Black, 0x06));

        // Botões: o principal é preenchido com o destaque nos dois temas (texto escuro ou claro conforme o contraste);
        // o secundário é o destaque translúcido
        // O texto do botão é pequeno (12,5 px): exige 4,5:1, e não os 3:1 de ícones. Sem contraste suficiente com
        // branco nem com o escuro, o preenchimento escurece até alcançar (ex.: ciano Vibrante no tema claro).
        var (primaryFill, onPrimary) = PrimaryPair(accent);
        Brush("PrimaryFillBrush", primaryFill);
        Brush("OnPrimaryBrush", onPrimary);
        Brush("SecondaryFillBrush", WithAlpha(accent, (byte)(dark ? 0x24 : 0x16)));
        Brush("SecondaryBorderBrush", WithAlpha(accent, (byte)(dark ? 0x4D : 0x3A)));
        Brush("OnSecondaryBrush", dark ? Blend(accent, Colors.White, 0.55) : Blend(accent, Colors.Black, 0.18));
        // Azul para números e textos de destaque (mais claro no escuro, mais fechado no claro)
        Brush("AccentTextBrush", dark ? Blend(accent, Colors.White, 0.2) : Blend(accent, Colors.Black, 0.1));

        // Superfícies dos cards: leve gradiente (mais claro no alto), caixa interna mais escura e card elevado
        var baseColor = C(palette.Card) with { A = 255 };
        Color Surf(Color c) => translucent ? WithAlpha(c, 0xA8) : c;
        // Vidro do Aurora: mais claro no alto (ardósia) e fechando para baixo
        resources["CardBgBrush"] = Frozen(new LinearGradientBrush(Surf(aurora ? Blend(baseColor, C("#94A3B8"), 0.07) : dark ? Blend(baseColor, Colors.White, 0.028) : baseColor), Surf(baseColor), new Point(0, 0), new Point(0.2, 1)));
        Brush("CardInsetBrush", Surf(dark ? Blend(baseColor, Colors.Black, 0.32) : Blend(baseColor, C("#EEF1F6"), 0.7)));
        Brush("CardElevatedBrush", Surf(dark ? Blend(baseColor, Colors.White, 0.045) : Colors.White));

        // Gráficos: linha azul, área em gradiente que some embaixo e grade quase invisível
        Brush("ChartLineBrush", accent);
        Brush("ChartSecondaryBrush", info);
        Brush("ChartGridBrush", WithAlpha(C(palette.Text), (byte)(dark ? 0x0C : 0x12)));
        resources["ChartFillBrush"] = Frozen(new LinearGradientBrush(WithAlpha(accent, (byte)(dark ? 0x55 : 0x40)), WithAlpha(accent, 0), new Point(0, 0), new Point(0, 1)));

        // Destaque de cartões principais: o destaque vem do canto superior direito (onde fica o anel de saúde) e some
        // antes do meio, deixando o texto sobre o grafite
        var hero = new LinearGradientBrush { StartPoint = new Point(1, 0), EndPoint = new Point(0, 1) };
        hero.GradientStops.Add(new GradientStop(Surf(Blend(baseColor, accent, aurora ? 0.14 : dark ? 0.10 : 0.05)), 0));
        hero.GradientStops.Add(new GradientStop(Surf(baseColor), 0.6));
        resources["HeroBrush"] = Frozen(hero);
        // Brilho azul bem discreto no alto da área de conteúdo (pincel estático: não custa nada parado)
        // Aurora: o brilho fica centralizado no alto, maior e mais forte (o "céu" azul da referência)
        var glowBrush = aurora
            ? new RadialGradientBrush { Center = new Point(0.5, -0.08), GradientOrigin = new Point(0.5, -0.08), RadiusX = 0.75, RadiusY = 0.62 }
            : new RadialGradientBrush { Center = new Point(0.78, 0), GradientOrigin = new Point(0.78, 0), RadiusX = 0.7, RadiusY = 0.85 };
        glowBrush.GradientStops.Add(new GradientStop(WithAlpha(aurora ? PrimaryPair(accent).Fill : accent, (byte)(aurora ? 0x3A : dark ? 0x16 : 0x0A)), 0));
        glowBrush.GradientStops.Add(new GradientStop(WithAlpha(accent, 0), 1));
        resources["GlowBrush"] = Frozen(glowBrush);

        // Forma e espaço
        var density = settings.Density switch { Density.Compact => 0.78, Density.Comfortable => 1.22, _ => 1.0 };
        // Raios em degraus, bem arredondados: card 20 · caixa interna 12 · controle 10 · selo 8
        resources["CardRadius"] = new CornerRadius(20);
        resources["InsetRadius"] = new CornerRadius(12);
        resources["ControlRadius"] = new CornerRadius(10);
        resources["BadgeRadius"] = new CornerRadius(8);
        resources["CardPadding"] = new Thickness(Math.Round(20 * density));
        resources["ContentGap"] = Math.Round(16 * density);
        resources["FastDuration"] = new Duration(TimeSpan.FromMilliseconds(settings.Animations ? 140 : 0));
        resources["NormalDuration"] = new Duration(TimeSpan.FromMilliseconds(settings.Animations ? 200 : 0));
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
        // Ícones e textos curtos sobre o destaque: branco sempre que tiver contraste de elemento gráfico (3:1)
        return whiteContrast >= 3 || whiteContrast >= blackContrast ? Colors.White : Colors.Black;
    }
    /// <summary>
    /// Realce dos botões (Motion.cs): a cor do texto por cima do fundo a 7% no hover e 14% pressionado. O par do botão
    /// principal precisa manter o contraste nos três estados, não só em repouso.
    /// </summary>
    internal static readonly double[] ButtonOverlays = { 0, 0.07, 0.14 };

    /// <summary>Preenchimento do botão principal e a cor do texto sobre ele, com pelo menos 4,5:1 em repouso, hover e pressionado.</summary>
    internal static (Color Fill, Color Text) PrimaryPair(Color accent)
    {
        var dark = C("#04151A");
        double Worst(Color fill, Color text) => ButtonOverlays.Min(o => ContrastRatio(Blend(fill, text, o), text));
        for (var i = 0; i <= 50; i++)
        {
            var fill = Blend(accent, Colors.Black, i / 100.0);
            var onWhite = Worst(fill, Colors.White);
            var onDark = Worst(fill, dark);
            if (Math.Max(onWhite, onDark) >= 4.5) return (fill, onDark >= onWhite ? dark : Colors.White);
        }
        return (Blend(accent, Colors.Black, 0.5), Colors.White);
    }

    internal static double ContrastRatio(Color a, Color b)
    {
        static double Channel(byte value)
        {
            var v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        static double L(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var (x, y) = (L(a), L(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
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
