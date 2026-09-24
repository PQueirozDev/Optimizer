using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace PQueirozOptimizer;

/// <summary>Códigos dos ícones da fonte Segoe Fluent Icons / Segoe MDL2 Assets.</summary>
public static class Glyphs
{
    public static readonly string Home = G(0xE80F);
    public static readonly string Lightning = G(0xE945);
    public static readonly string Monitor = G(0xE7F4);
    public static readonly string Disc = G(0xE958);
    public static readonly string Repair = G(0xE90F);
    public static readonly string History = G(0xE81C);
    public static readonly string Settings = G(0xE713);
    public static readonly string Info = G(0xE946);
    public static readonly string Document = G(0xE8A5);
    public static readonly string Chip = G(0xE950);
    public static readonly string Key = G(0xE8D7);
    public static readonly string Diagnostic = G(0xE9D9);
    public static readonly string Broom = G(0xEA99);
    public static readonly string Shield = G(0xEA18);
    public static readonly string Speed = G(0xEC4A);
    public static readonly string Memory = G(0xE964);
    public static readonly string Drive = G(0xEDA2);
    public static readonly string Clock = G(0xE823);
    public static readonly string Refresh = G(0xE72C);
    public static readonly string Undo = G(0xE7A7);
    public static readonly string Check = G(0xE73E);
    public static readonly string Warning = G(0xE7BA);
    public static readonly string Error = G(0xEA39);
    public static readonly string Download = G(0xE896);
    public static readonly string Folder = G(0xE8B7);
    public static readonly string Delete = G(0xE74D);
    public static readonly string Game = G(0xE7FC);
    public static readonly string Sun = G(0xE706);
    public static readonly string Moon = G(0xE708);
    public static readonly string Maximize = G(0xE922);
    public static readonly string Restore = G(0xE923);
    public static readonly string ChevronRight = G(0xE76C);
    public static readonly string Power = G(0xE7E8);

    private static string G(int code) => char.ConvertFromUtf32(code);
}

public partial class MainWindow
{
    /// <summary>MessageBox com texto e título traduzidos para o idioma ativo.</summary>
    private static MessageBoxResult Msg(string text, string caption = "PQueiroz Optimizer", MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
        => MessageBox.Show(Services.Translator.Tr(text), Services.Translator.Tr(caption), button, icon, defaultResult);

    private TextBlock Label(string text, double size = 13, bool muted = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
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

    /// <summary>Quadrado arredondado com ícone colorido sobre fundo translúcido da mesma cor.</summary>
    private static Border IconChip(string glyph, string tone = "Accent", double size = 40)
    {
        var chip = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.3), Child = GlyphIcon(glyph, size * 0.42, tone + "Brush") };
        chip.SetResourceReference(Border.BackgroundProperty, tone + "SoftBrush");
        return chip;
    }

    private static Border Pill(string text, string tone = "Accent")
    {
        var label = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold };
        label.SetResourceReference(TextBlock.ForegroundProperty, tone + "Brush");
        var pill = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 3), Child = label, VerticalAlignment = VerticalAlignment.Center };
        pill.SetResourceReference(Border.BackgroundProperty, tone + "SoftBrush");
        return pill;
    }

    private static DropShadowEffect CardShadow()
    {
        var shadow = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 3, Direction = 270, Opacity = 0.16 };
        // Efeitos não aceitam recursos dinâmicos; as páginas são recriadas ao trocar o tema
        if (Application.Current?.TryFindResource("ShadowColor") is Color color) shadow.Color = color;
        return shadow;
    }

    private Border Surface(UIElement content)
    {
        var border = new Border { Child = content, Padding = new Thickness(22), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 16), Effect = CardShadow() };
        border.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return border;
    }

    private void Primary(Button button)
    {
        button.SetResourceReference(StyleProperty, "PrimaryButton");
        button.SetResourceReference(Button.BackgroundProperty, "AccentGradientBrush");
        button.BorderBrush = Brushes.Transparent;
        button.SetResourceReference(Button.ForegroundProperty, "OnAccentBrush");
    }

    /// <summary>Botão com ícone à esquerda do texto.</summary>
    private static Button IconButton(string glyph, string text, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = GlyphIcon(glyph, 13, primary ? "OnAccentBrush" : "AccentBrush");
        icon.Margin = new Thickness(0, 0, 8, 0);
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Foreground = null });
        var button = new Button { Content = content };
        ((TextBlock)content.Children[1]).SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Button.Foreground)) { Source = button });
        if (primary)
        {
            button.SetResourceReference(StyleProperty, "PrimaryButton");
        }
        return button;
    }

    /// <summary>Cartão de estatística: ícone, rótulo discreto e valor em destaque.</summary>
    private Border Card(string title, string value, string? glyph = null, string brushKey = "AccentBrush")
    {
        var tone = brushKey.Replace("Brush", "");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var chip = IconChip(glyph ?? Glyphs.Info, tone, 42);
        chip.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(chip);

        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var v = new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
        v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        text.Children.Add(t); text.Children.Add(v);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var card = new Border { Padding = new Thickness(18), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 14, 14), Child = grid, Effect = CardShadow() };
        card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return card;
    }

    /// <summary>Anel de progresso (0–100) com o valor no centro.</summary>
    private static Grid ScoreRing(double value, string caption, double size = 128)
    {
        value = Math.Clamp(value, 0, 100);
        var grid = new Grid { Width = size, Height = size };
        var track = new Ellipse { StrokeThickness = 10 };
        track.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
        grid.Children.Add(track);

        var radius = (size - 10) / 2;
        var angle = value / 100 * 359.99;
        var center = new Point(size / 2, size / 2);
        Point At(double degrees)
        {
            var rad = (degrees - 90) * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
        }
        var figure = new PathFigure { StartPoint = At(0), IsClosed = false };
        figure.Segments.Add(new ArcSegment(At(angle), new Size(radius, radius), 0, angle > 180, SweepDirection.Clockwise, true));
        var arc = new Path { Data = new PathGeometry(new[] { figure }), StrokeThickness = 10, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        arc.SetResourceReference(Shape.StrokeProperty, "AccentGradientBrush");
        grid.Children.Add(arc);

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var number = new TextBlock { Text = ((int)Math.Round(value)).ToString(), FontSize = size * 0.26, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        number.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        var small = new TextBlock { Text = caption, FontSize = 10.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        small.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        labels.Children.Add(number); labels.Children.Add(small);
        grid.Children.Add(labels);
        return grid;
    }

    /// <summary>Cabeçalho de seção com ícone e subtítulo opcional.</summary>
    private StackPanel SectionHeader(string title, string? subtitle = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 12) };
        var t = new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        panel.Children.Add(t);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var s = new TextBlock { Text = subtitle, FontSize = 12.5, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
            s.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            panel.Children.Add(s);
        }
        return panel;
    }
}
