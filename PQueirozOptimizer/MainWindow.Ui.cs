using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Códigos dos ícones da fonte Lucide embutida (Assets/Fonts/lucide.ttf; nomes em lucide.dev/icons). Sem emojis.</summary>
public static class Glyphs
{
    public static readonly string Home = G(0xE0F8);
    public static readonly string Lightning = G(0xE1B3);
    public static readonly string Monitor = G(0xE120);
    public static readonly string Disc = G(0xE498);
    public static readonly string Repair = G(0xE1B0);
    public static readonly string History = G(0xE1F4);
    public static readonly string Settings = G(0xE157);
    public static readonly string Info = G(0xE0FE);
    public static readonly string Document = G(0xE0D0);
    public static readonly string Chip = G(0xE0AD);
    public static readonly string Key = G(0xE4A7);
    public static readonly string Copy = G(0xE0A2);
    public static readonly string Diagnostic = G(0xE038);
    public static readonly string Broom = G(0xE416);
    public static readonly string Shield = G(0xE15B);
    public static readonly string Speed = G(0xE1BE);
    public static readonly string Memory = G(0xE449);
    public static readonly string Drive = G(0xE0F0);
    public static readonly string Clock = G(0xE08B);
    public static readonly string Refresh = G(0xE148);
    public static readonly string Undo = G(0xE2A0);
    public static readonly string Check = G(0xE070);
    public static readonly string Warning = G(0xE192);
    public static readonly string Error = G(0xE088);
    public static readonly string Download = G(0xE0B6);
    public static readonly string Folder = G(0xE0DC);
    public static readonly string Delete = G(0xE18D);
    public static readonly string Game = G(0xE0E2);
    public static readonly string Sun = G(0xE17B);
    public static readonly string Moon = G(0xE121);
    public static readonly string Maximize = G(0xE16A);
    public static readonly string Restore = G(0xE0A2);
    public static readonly string ChevronRight = G(0xE073);
    public static readonly string ChevronDown = G(0xE071);
    public static readonly string ChevronUp = G(0xE074);
    public static readonly string ArrowUp = G(0xE04E);
    public static readonly string Power = G(0xE143);
    public static readonly string Cancel = G(0xE1B1);
    public static readonly string Battery = G(0xE05B);
    public static readonly string BatteryCharging = G(0xE058);
    public static readonly string SpeedLow = G(0xE2DD);
    public static readonly string SpeedMedium = G(0xE211);
    public static readonly string OpenInNew = G(0xE0BD);
    public static readonly string Lock = G(0xE10E);
    public static readonly string Search = G(0xE154);
    public static readonly string Person = G(0xE19E);
    public static readonly string Globe = G(0xE0EB);
    public static readonly string Network = G(0xE1AD);
    public static readonly string Wifi = G(0xE1AD);
    public static readonly string Ethernet = G(0xE624);
    public static readonly string Play = G(0xE13F);
    public static readonly string Pause = G(0xE131);
    public static readonly string Stop = G(0xE087);
    public static readonly string Package = G(0xE12C);
    public static readonly string Add = G(0xE140);
    public static readonly string Restore2 = G(0xE2CC);
    public static readonly string Library = G(0xE105);
    public static readonly string Services = G(0xE156);
    public static readonly string Apps = G(0xE104);
    public static readonly string Speaker = G(0xE1AA);
    public static readonly string Bluetooth = G(0xE060);
    public static readonly string Print = G(0xE144);
    public static readonly string Video = G(0xE1A4);
    public static readonly string Tag = G(0xE182);
    public static readonly string Rocket2 = G(0xE1B3);
    public static readonly string Personalize = G(0xE2E6);
    public static readonly string Code = G(0xE097);
    public static readonly string Laptop = G(0xE1CC);
    public static readonly string Save = G(0xE150);
    public static readonly string Edit = G(0xE1F8);
    public static readonly string Upload = G(0xE19D);
    public static readonly string Filter = G(0xE0D5);
    public static readonly string Pin = G(0xE258);
    public static readonly string Bell = G(0xE05D);
    public static readonly string Thermometer = G(0xE185);
    public static readonly string Heart = G(0xE0F5);
    public static readonly string HeartPulse = G(0xE371);
    public static readonly string Devices = G(0xE3A5);
    public static readonly string Usb = G(0xE359);
    public static readonly string Health = G(0xE371);
    public static readonly string MyPc = G(0xE44A);
    public static readonly string Flask = G(0xE0DA);
    public static readonly string AreaChart = G(0xE4D7);
    public static readonly string Sliders = G(0xE299);
    public static readonly string Palette = G(0xE1DC);
    public static readonly string CloudDownload = G(0xE08D);
    public static readonly string Bug = G(0xE20B);
    public static readonly string Fire = G(0xE0D7);

    private static string G(int code) => char.ConvertFromUtf32(code);
}

public partial class MainWindow
{
    /// <summary>MessageBox com texto e título traduzidos para o idioma ativo.</summary>
    private static MessageBoxResult Msg(string text, string caption = "Qrztweaks", MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
        => MessageBox.Show(Services.Translator.Tr(text), Services.Translator.Tr(caption), button, icon, defaultResult);

    /// <summary>
    /// Abre um endereço no navegador. Aceita apenas http/https e os esquemas explícitos de configurações do Windows.
    /// usuário e o aplicativo roda como administrador, então nunca executa um caminho local daqui.
    /// </summary>
    private void OpenUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp &&
             !uri.Scheme.Equals("ms-settings", StringComparison.OrdinalIgnoreCase) &&
             !uri.Scheme.Equals("windowsdefender", StringComparison.OrdinalIgnoreCase)))
        {
            _log.Write("WARN", "Link ignorado (não é um endereço da web): " + url);
            OperationStatus.Text = "Link inválido: " + url;
            return;
        }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Write("ERROR", "Não foi possível abrir o navegador: " + ex.Message);
            Msg("Falha ao abrir navegador: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Copia um texto; outro programa pode estar com a área de transferência aberta.</summary>
    private void CopyText(string text, string confirmation)
    {
        try { Clipboard.SetText(text); OperationStatus.Text = confirmation; }
        catch (System.Runtime.InteropServices.ExternalException) { OperationStatus.Text = "A área de transferência está em uso por outro programa. Tente novamente."; }
    }

    /// <summary>Abre o Explorador de Arquivos com o arquivo selecionado.</summary>
    private void ShowInFolder(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { _log.Write("WARN", "Não foi possível abrir a pasta: " + ex.Message); }
    }

    // ===================================================================================================
    // Design system: estas funções são a API visual das páginas. Elas leem os tokens do ThemeService, então
    // mudar o visual aqui muda todas as telas de uma vez, sem cada página decidir cor, raio ou espaço.
    // ===================================================================================================

    /// <summary>
    /// Escala única de tipografia (11 · 12 · 13 · 14 · 16 · 18 · 22 · 28): qualquer tamanho pedido cai no degrau
    /// mais próximo, para as páginas não misturarem uma dúzia de tamanhos quase iguais.
    /// </summary>
    private static double TypeScale(double size) => size switch { <= 11.5 => 11, <= 12.5 => 12, <= 13 => 13, <= 15 => 14, <= 17 => 16, <= 20 => 18, <= 25 => 22, _ => 28 };

    private TextBlock Label(string text, double size = 13, bool muted = false)
    {
        var label = new TextBlock { Text = text, FontSize = TypeScale(size), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        if (size >= 18) { label.FontWeight = FontWeights.SemiBold; label.Margin = new Thickness(0, 4, 0, 12); }
        label.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush");
        return label;
    }

    private static TextBlock GlyphIcon(string glyph, double size = 16, string brushKey = "TextBrush")
    {
        var icon = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return icon;
    }

    /// <summary>
    /// Ícone na cor do tom (azul de destaque, ou a cor de um alerta) sobre um fundo quase invisível, sem borda:
    /// ancora o ícone sem virar uma caixinha em cada card.
    /// </summary>
    private static Border IconChip(string glyph, string tone = "Accent", double size = 40)
    {
        var chip = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.3), Child = GlyphIcon(glyph, size * 0.42, tone + "Brush") };
        chip.SetResourceReference(Border.BackgroundProperty, "NavHoverBrush");
        return chip;
    }

    /// <summary>Quadrado neutro com ícone vetorial na cor do texto; os cartões principais recebem o ícone no azul.</summary>
    private static Border OutlineChip(VectorIcon icon, double size = 42, bool primary = false)
    {
        var chip = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.3), Child = OptimizationIcons.Render(icon, size * 0.52, primary ? "AccentBrush" : "TextBrush") };
        chip.SetResourceReference(Border.BackgroundProperty, "NavHoverBrush");
        return chip;
    }

    /// <summary>Selo (QrzBadge): texto curto no tom pedido sobre o mesmo tom translúcido.</summary>
    private static Border Pill(string text, string tone = "Accent")
    {
        var label = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold };
        label.SetResourceReference(TextBlock.ForegroundProperty, tone == "Accent" ? "AccentTextBrush" : tone + "Brush");
        var pill = new Border { Padding = new Thickness(8, 3, 8, 3), Child = label, VerticalAlignment = VerticalAlignment.Center };
        pill.SetResourceReference(Border.CornerRadiusProperty, "BadgeRadius");
        pill.SetResourceReference(Border.BackgroundProperty, tone + "SoftBrush");
        return pill;
    }

    /// <summary>Card (QrzCard): superfície grafite com fio de luz no alto da borda e cantos do token CardRadius; sem sombra.</summary>
    private Border Surface(UIElement content)
    {
        var border = new Border { Child = content, Padding = AppearanceService.CardPadding, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, AppearanceService.Space(16)) };
        border.SetResourceReference(Border.CornerRadiusProperty, "CardRadius");
        border.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return border;
    }

    /// <summary>Caixa interna mais escura dentro de um card (gráficos "ao vivo", listas, valores agrupados).</summary>
    private static Border Inset(UIElement content, double padding = 12)
    {
        var box = new Border { Child = content, Padding = new Thickness(padding), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.CornerRadiusProperty, "InsetRadius");
        box.SetResourceReference(Border.BackgroundProperty, "CardInsetBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return box;
    }

    private void Primary(Button button)
    {
        button.SetResourceReference(StyleProperty, "PrimaryButton");
        button.ClearValue(Button.BackgroundProperty);
        button.ClearValue(Button.BorderBrushProperty);
        button.ClearValue(Button.ForegroundProperty);
    }

    /// <summary>Botão com ícone à esquerda do texto (QrzButton). Principal = preenchido no destaque; neutro = ícone no destaque.</summary>
    private static Button IconButton(string glyph, string text, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = GlyphIcon(glyph, 13, primary ? "OnPrimaryBrush" : "AccentBrush");
        icon.Margin = new Thickness(0, 0, 8, 0);
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Foreground = null });
        // Centralizado: em linhas com texto de várias linhas o botão não estica na altura
        var button = new Button { Content = content, VerticalAlignment = VerticalAlignment.Center };
        ((TextBlock)content.Children[1]).SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Button.Foreground)) { Source = button });
        if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        return button;
    }

    /// <summary>Botão secundário (azul translúcido) com ícone, como o "Run Full Optimization" da referência.</summary>
    private static Button SecondaryButton(string glyph, string text)
    {
        var button = IconButton(glyph, text);
        button.SetResourceReference(StyleProperty, "SecondaryButton");
        if (button.Content is StackPanel { Children: [TextBlock icon, ..] }) icon.SetResourceReference(TextBlock.ForegroundProperty, "OnSecondaryBrush");
        return button;
    }

    /// <summary>Botão compacto "Texto ›" dos cards de acesso rápido.</summary>
    private static Button ChevronButton(string text, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(label);
        var arrow = GlyphIcon(Glyphs.ChevronRight, 9, primary ? "OnPrimaryBrush" : "TextBrush");
        arrow.Margin = new Thickness(10, 1, 0, 0);
        content.Children.Add(arrow);
        var button = new Button { Content = content, Padding = new Thickness(16, 8, 14, 8), Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Button.Foreground)) { Source = button });
        if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        else button.SetResourceReference(Button.BackgroundProperty, "CardElevatedBrush");
        return button;
    }

    /// <summary>Bolinha de estado (verde/amarelo/vermelho/cinza). O estado sempre vem acompanhado de texto.</summary>
    private static Ellipse StatusDot(string tone, double size = 8)
    {
        var dot = new Ellipse { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, tone + "Brush");
        return dot;
    }

    /// <summary>Linha de detalhe: rótulo discreto à esquerda, valor à direita (sem ícone por linha, para não poluir).</summary>
    private static DockPanel DetailRow(string glyph, string label, string value, string valueBrush = "TextBrush", bool systemValue = true)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var v = new TextBlock { Text = value, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240, ToolTip = value };
        if (systemValue) v.Tag = Translator.SystemDataTag;
        v.SetResourceReference(TextBlock.ForegroundProperty, valueBrush);
        DockPanel.SetDock(v, Dock.Right); row.Children.Add(v);
        var l = new TextBlock { Text = label, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        row.Children.Add(l);
        return row;
    }

    /// <summary>Estado vazio (QrzEmptyState): ícone, título, explicação e uma ação opcional.</summary>
    private Border EmptyState(string glyph, string title, string text, Button? action = null)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 460, Margin = new Thickness(0, 12, 0, 12) };
        var chip = IconChip(glyph, "Accent", 46); chip.Margin = new Thickness(0, 0, 0, 14); chip.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(chip);
        var t = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(t);
        var d = new TextBlock { Text = text, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(d);
        if (action != null) { action.Margin = new Thickness(0, 16, 0, 0); action.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(action); }
        return Surface(stack);
    }

    /// <summary>Cartão de estatística: ícone, rótulo discreto e valor em destaque.</summary>
    private Border Card(string title, string value, string? glyph = null, string brushKey = "AccentBrush")
    {
        var tone = brushKey.Replace("Brush", "");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var scale = AppearanceService.CardScale;
        var chip = IconChip(glyph ?? Glyphs.Info, tone, 36 * scale);
        chip.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(chip);

        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        // No máximo duas linhas: nomes longos ("Intel(R) Core(TM)...") terminam em reticências com o nome completo na dica
        var v = new TextBlock { Text = value, FontSize = 14 * scale, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 14 * scale * 2.7, ToolTip = value };
        v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        text.Children.Add(t); text.Children.Add(v);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var card = new Border { Padding = new Thickness(AppearanceService.Space(16) * scale), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 14, AppearanceService.Space(14)), Child = grid };
        card.SetResourceReference(Border.CornerRadiusProperty, "CardRadius");
        card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return card;
    }

    /// <summary>Anel de progresso (0–100) com o valor no centro.</summary>
    private static Grid ScoreRing(double value, string caption, double size = 128)
    {
        value = Math.Clamp(value, 0, 100);
        var grid = new Grid { Width = size, Height = size };
        var thickness = Math.Max(6, size * 0.07);
        var track = new Ellipse { StrokeThickness = thickness };
        track.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
        grid.Children.Add(track);

        var radius = (size - thickness) / 2;
        var angle = value / 100 * 359.99;
        var center = new Point(size / 2, size / 2);
        Point At(double degrees)
        {
            var rad = (degrees - 90) * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
        }
        var figure = new PathFigure { StartPoint = At(0), IsClosed = false };
        figure.Segments.Add(new ArcSegment(At(angle), new Size(radius, radius), 0, angle > 180, SweepDirection.Clockwise, true));
        var arc = new Path { Data = new PathGeometry(new[] { figure }), StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        arc.SetResourceReference(Shape.StrokeProperty, "AccentGradientBrush");
        // O arco "se desenha" ao aparecer: o traço tracejado começa escondido e é revelado
        var dash = radius * angle * Math.PI / 180 / arc.StrokeThickness + 1;
        arc.StrokeDashArray = new DoubleCollection { dash, dash };
        if (Application.Current?.MainWindow is { IsVisible: true } && AppearanceService.AnimationsEnabled)
            arc.BeginAnimation(Shape.StrokeDashOffsetProperty, new System.Windows.Media.Animation.DoubleAnimation(dash, 0, TimeSpan.FromMilliseconds(700))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        grid.Children.Add(arc);

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var number = new TextBlock { Text = ((int)Math.Round(value)).ToString(), FontSize = size * 0.26, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        number.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        var small = new TextBlock { Text = caption, FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        small.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        labels.Children.Add(number); if (!string.IsNullOrEmpty(caption)) labels.Children.Add(small);
        grid.Children.Add(labels);
        return grid;
    }

    /// <summary>Cabeçalho de seção (QrzSectionHeader): título e subtítulo opcional.</summary>
    private StackPanel SectionHeader(string title, string? subtitle = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 14) };
        var t = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        panel.Children.Add(t);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var s = new TextBlock { Text = subtitle, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            s.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            panel.Children.Add(s);
        }
        return panel;
    }
}
