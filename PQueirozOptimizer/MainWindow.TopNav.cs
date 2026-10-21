using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Navegação superior (visual Aurora): cinco abas de categoria no alto, as páginas da categoria ativa em sub-abas
/// acima do título e o menu da conta (Sistema) no avatar. Os itens continuam declarados como RadioButtons escondidos
/// no XAML (NavStack): bloqueio por plano, busca, tutoriais e estados leem deles, e as abas são montadas a partir deles.
/// </summary>
public partial class MainWindow
{
    // Rótulo e ícone de cada categoria (Tag do ToggleButton no NavStack). "system" fica no menu da conta.
    private static readonly (string Tag, string Title, string Glyph)[] TopCategories =
    {
        ("main", "Início", Glyphs.Home), ("optimize", "Otimizar", Glyphs.Lightning), ("performance", "Desempenho", Glyphs.Diagnostic),
        ("games", "Jogos", Glyphs.Game), ("tools", "Ferramentas", Glyphs.Repair),
    };

    private readonly Dictionary<string, string> _lastPageByCategory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _topTabButtons = new(StringComparer.Ordinal);
    private string _topNavPage = "dashboard";

    /// <summary>Categoria (Tag do grupo) que contém a página; null para páginas fora do menu.</summary>
    private string? CategoryOf(string page) =>
        NavCategories().Where(c => c.Group.Children.OfType<RadioButton>().Any(n => n.Tag as string == page)).Select(c => c.Header.Tag as string).FirstOrDefault();

    /// <summary>Remonta as abas e as sub-abas para a página ativa (chamado a cada navegação e mudança de plano).</summary>
    private void RefreshTopNav(string page)
    {
        _topNavPage = page;
        var category = CategoryOf(page) ?? "main";
        _lastPageByCategory[category] = page;

        TopTabs.Children.Clear();
        _topTabButtons.Clear();
        foreach (var (tag, title, glyph) in TopCategories)
        {
            var active = tag == category;
            var button = NavPill(glyph, title, active, primary: true);
            // Janela estreita: só o ícone, com o nome na dica
            if (_topTabsIconOnly && button.Content is StackPanel { Children: [TextBlock icon, TextBlock label] }) { label.Visibility = Visibility.Collapsed; icon.Margin = new Thickness(0); button.ToolTip = title; }
            button.Click += (_, _) => NavigateTo(_lastPageByCategory.GetValueOrDefault(tag) ?? FirstPage(tag));
            System.Windows.Automation.AutomationProperties.SetName(button, Translator.Tr(title));
            _topTabButtons[tag] = button;
            TopTabs.Children.Add(button);
        }

        SubTabs.Children.Clear();
        var group = NavCategories().FirstOrDefault(c => c.Header.Tag as string == category).Group;
        if (group != null)
            foreach (var nav in group.Children.OfType<RadioButton>().Where(n => n.Visibility == Visibility.Visible))
            {
                var target = nav.Tag?.ToString() ?? "";
                var selected = target == page;
                var pill = NavPill(NavGlyph(nav), NavName(nav), selected, primary: false);
                pill.Margin = new Thickness(0, 0, 6, 6);
                if (nav.Opacity < 1) { pill.Opacity = 0.5; pill.ToolTip = nav.ToolTip; }
                // Modo Jogo ativo: o mesmo selo da barra lateral antiga, como um ponto verde
                if (target == "gaming" && NavGamingBadge.Visibility == Visibility.Visible && pill.Content is StackPanel content)
                {
                    var dot = StatusDot("Success", 7); dot.Margin = new Thickness(8, 0, 0, 0);
                    content.Children.Add(dot);
                }
                pill.Click += (_, _) => NavigateTo(target);
                System.Windows.Automation.AutomationProperties.SetName(pill, Translator.Tr(NavName(nav)));
                SubTabs.Children.Add(pill);
            }
        // No Início o cartão de boas-vindas faz o papel do título
        PageHeading.Visibility = page == "dashboard" ? Visibility.Collapsed : Visibility.Visible;
    }

    private string FirstPage(string category) =>
        NavCategories().Where(c => c.Header.Tag as string == category).SelectMany(c => c.Group.Children.OfType<RadioButton>())
            .Where(n => n.Visibility == Visibility.Visible).Select(n => n.Tag?.ToString()).FirstOrDefault() ?? "dashboard";

    private static string NavGlyph(RadioButton nav) =>
        nav.Content is Panel panel ? panel.Children.OfType<TextBlock>().FirstOrDefault(t => t.Style == (Style)Application.Current.FindResource("NavIcon"))?.Text ?? Glyphs.Info : Glyphs.Info;

    /// <summary>
    /// Pílula da navegação. Aba de categoria ativa: preenchida no destaque com brilho; sub-aba ativa: véu do destaque
    /// com contorno. As inativas ficam neutras, com o texto apagado.
    /// </summary>
    private static Button NavPill(string glyph, string title, bool active, bool primary)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = GlyphIcon(glyph, primary ? 15 : 14);
        icon.Margin = new Thickness(0, 0, 8, 0);
        var text = new TextBlock { Text = title, FontSize = primary ? 13.5 : 12.5, FontWeight = active ? FontWeights.SemiBold : FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(icon); content.Children.Add(text);
        var button = new Button { Content = content, Height = primary ? 36 : 32, Padding = new Thickness(primary ? 16 : 13, 0, primary ? 16 : 13, 0), Margin = new Thickness(primary ? 2 : 0, 0, 0, 0), BorderThickness = new Thickness(1) };
        QrzUi.SetCornerRadius(button, new CornerRadius(primary ? 10 : 10));
        if (active && primary)
        {
            button.SetResourceReference(StyleProperty, "PrimaryButton");
            button.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.5, Color = Color.FromRgb(0x25, 0x63, 0xEB) };
        }
        else
        {
            button.SetResourceReference(StyleProperty, "GhostButton");
            if (active)
            {
                button.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
                button.SetResourceReference(BorderBrushProperty, "SecondaryBorderBrush");
            }
            else button.BorderBrush = Brushes.Transparent;
            button.SetResourceReference(ForegroundProperty, active ? "TextBrush" : "MutedBrush");
        }
        icon.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Button.Foreground)) { Source = button });
        if (active && !primary) icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Button.Foreground)) { Source = button });
        return button;
    }

    /// <summary>Aba de categoria da página (alvo dos tutoriais).</summary>
    private FrameworkElement? TopTabFor(string page) => CategoryOf(page) is { } category && _topTabButtons.TryGetValue(category, out var tab) ? tab : null;

    // ================= Menu da conta =================
    private void AvatarButton_Click(object sender, RoutedEventArgs e)
    {
        AvatarMenu.Children.Clear();
        var head = new DockPanel { Margin = new Thickness(8, 6, 8, 10) };
        var avatar = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(12), Child = new TextBlock { Text = LicenseAvatarText.Text, FontWeight = FontWeights.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Tag = Translator.SystemDataTag } };
        avatar.SetResourceReference(Border.BackgroundProperty, "AccentGradientBrush");
        ((TextBlock)avatar.Child).SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
        DockPanel.SetDock(avatar, Dock.Left); head.Children.Add(avatar);
        var names = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = LicenseLabel.Text, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Tag = LicenseLabel.Tag };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var plan = new TextBlock { Text = LicensePlanText.Text, FontSize = 11, FontWeight = FontWeights.SemiBold, Tag = Translator.SystemDataTag };
        plan.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        names.Children.Add(name); if (plan.Text.Length > 0) names.Children.Add(plan);
        head.Children.Add(names);
        AvatarMenu.Children.Add(head);
        AvatarMenu.Children.Add(MenuRule());
        foreach (var nav in NavCategories().Where(c => c.Header.Tag as string == "system").SelectMany(c => c.Group.Children.OfType<RadioButton>()).Where(n => n.Visibility == Visibility.Visible))
        {
            var target = nav.Tag?.ToString() ?? "";
            AvatarMenu.Children.Add(MenuEntry(NavGlyph(nav), NavName(nav), () => NavigateTo(target)));
        }
        AvatarMenu.Children.Add(MenuRule());
        AvatarMenu.Children.Add(MenuEntry(Glyphs.Palette, "Tema", () => ShowThemeMenu(AvatarButton, PlacementMode.Bottom)));
        AvatarMenu.Children.Add(MenuEntry(Glyphs.Pin, "Modo compacto (segundo monitor)", OpenCompactMonitor));
        AvatarPopup.IsOpen = true;
    }

    private Border MenuRule()
    {
        var rule = new Border { Height = 1, Margin = new Thickness(6, 4, 6, 4) };
        rule.SetResourceReference(Border.BackgroundProperty, "BorderSubtleBrush");
        return rule;
    }

    private Button MenuEntry(string glyph, string text, Action action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = GlyphIcon(glyph, 15, "MutedBrush"); icon.Margin = new Thickness(0, 0, 12, 0);
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(0, 1, 0, 1), HorizontalAlignment = HorizontalAlignment.Stretch };
        button.SetResourceReference(StyleProperty, "GhostButton");
        button.SetResourceReference(ForegroundProperty, "TextBrush");
        button.Click += (_, _) => { AvatarPopup.IsOpen = false; action(); };
        return button;
    }
}
