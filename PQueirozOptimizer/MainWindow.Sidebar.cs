using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Barra lateral: categorias recolhíveis (PRINCIPAL, OTIMIZAÇÃO, DESEMPENHO, FERRAMENTAS, SISTEMA) e modo compacto
/// só com ícones (86 px, com a margem do painel flutuante) com o nome nas dicas, por preferência, Ctrl+B ou automaticamente em janelas estreitas.
/// A navegação por teclado (Tab e setas) funciona nos dois modos. A barra superior também se adapta à largura.
/// </summary>
public partial class MainWindow
{
    private const double SidebarWidth = 250, SidebarCompactWidth = 86;
    private bool _sidebarCompactApplied;
    private bool? _sidebarForcedByWidth;

    private bool SidebarShouldBeCompact => AppearanceService.Current.CompactSidebar || (_sidebarForcedByWidth ?? false);

    /// <summary>Todos os itens de navegação, em ordem (dentro das categorias).</summary>
    private IEnumerable<RadioButton> NavItems()
    {
        foreach (var child in NavStack.Children)
        {
            if (child is RadioButton direct) yield return direct;
            if (child is Panel group) foreach (var nav in group.Children.OfType<RadioButton>()) yield return nav;
        }
    }

    private IEnumerable<(ToggleButton Header, Panel Group)> NavCategories()
    {
        ToggleButton? header = null;
        foreach (var child in NavStack.Children)
        {
            if (child is ToggleButton toggle) header = toggle;
            else if (child is Panel group && header != null) { yield return (header, group); header = null; }
        }
    }

    private void InitSidebar()
    {
        SizeChanged += (_, e) =>
        {
            ApplyTopBarMode(e.NewSize.Width);
            // Abaixo de 1000 px a barra completa toma espaço demais do conteúdo
            var narrow = e.NewSize.Width < 1000;
            if (_sidebarForcedByWidth == narrow) return;
            _sidebarForcedByWidth = narrow;
            ApplySidebarMode();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.B || Keyboard.Modifiers != ModifierKeys.Control) return;
            var next = AppearanceService.Current.Clone();
            next.CompactSidebar = !next.CompactSidebar;
            SaveAppearance(next);
            e.Handled = true;
        };
        // Categorias recolhidas na última vez
        var collapsed = AppearanceService.Current.CollapsedNavGroups ?? new List<string>();
        foreach (var (header, group) in NavCategories())
        {
            var isCollapsed = collapsed.Contains(header.Tag as string ?? "");
            header.IsChecked = !isCollapsed;
            group.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }
        ApplySidebarMode();
        ApplyTopBarMode(ActualWidth > 0 ? ActualWidth : Width);
    }

    private void NavCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton header) return;
        var group = NavCategories().FirstOrDefault(c => c.Header == header).Group;
        if (group is null) return;
        var expanded = header.IsChecked == true;
        group.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (expanded && AppearanceService.AnimationsEnabled)
        {
            // Os itens aparecem descendo um pouco (160 ms); recolher é imediato
            var slide = new System.Windows.Media.TranslateTransform(0, -6);
            group.RenderTransform = slide;
            group.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        var next = AppearanceService.Current.Clone();
        var collapsed = NavCategories().Where(c => c.Header.IsChecked != true).Select(c => c.Header.Tag as string ?? "").ToList();
        next.CollapsedNavGroups = collapsed.Count == 0 ? null : collapsed;
        // Só guarda a preferência: não precisa redesenhar a página por isso
        AppearanceService.SetQuietly(next);
        try { _configService.SaveAppearance(next); }
        catch (System.IO.IOException ex) { _log.Write("WARN", "Preferências não foram salvas: " + (ex.InnerException?.Message ?? ex.Message)); }
    }

    /// <summary>Ao abrir uma página por busca ou atalho, a categoria dela abre se estiver recolhida.</summary>
    private void RevealNavItem(string page)
    {
        foreach (var (header, group) in NavCategories())
        {
            if (!group.Children.OfType<RadioButton>().Any(n => n.Tag as string == page) || header.IsChecked == true) continue;
            header.IsChecked = true;
            group.Visibility = Visibility.Visible;
        }
    }

    private void ApplySidebarMode()
    {
        // Visual Aurora: a navegação fica na barra superior; a coluna da barra lateral antiga não ocupa espaço
        RootGrid.ColumnDefinitions[0].Width = new GridLength(0);
        RefreshNavTooltips(false);
        return;
#pragma warning disable CS0162 // código mantido para quem quiser voltar a barra lateral
        var compact = SidebarShouldBeCompact;
        if (compact == _sidebarCompactApplied && RootGrid.ColumnDefinitions[0].Width.Value is SidebarCompactWidth or SidebarWidth) { RefreshNavTooltips(compact); return; }
        _sidebarCompactApplied = compact;
        RootGrid.ColumnDefinitions[0].Width = new GridLength(compact ? SidebarCompactWidth : SidebarWidth);
        SidebarLogoText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarLogo.Margin = compact ? new Thickness(20, 16, 0, 10) : new Thickness(20, 16, 14, 10);
        HudPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        // Rodapé compacto: só o avatar da licença (o resto vai para a dica)
        LicenseInfoText.Visibility = ThemeButton.Visibility = SidebarSettingsButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        LicenseCard.Padding = compact ? new Thickness(7) : new Thickness(10, 9, 10, 9);
        LicenseCard.ToolTip = compact ? LicenseLabel.Text + (string.IsNullOrEmpty(LicensePlanText.Text) ? "" : " · " + LicensePlanText.Text) : null;
        SidebarScroll.Padding = compact ? new Thickness(8, 0, 8, 0) : new Thickness(12, 0, 12, 0);
        foreach (var (header, group) in NavCategories())
        {
            // No modo compacto não há nomes de categoria: todos os ícones ficam visíveis, separados por um respiro
            header.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            group.Visibility = compact || header.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            group.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        }
        foreach (var nav in NavItems())
        {
            nav.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            foreach (var text in NavTexts(nav)) text.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }
        RefreshNavTooltips(compact);
#pragma warning restore CS0162
    }

    private bool _showOperationChip, _showAdminWarning;
    private double _topBarWidth;
    private bool _topTabsIconOnly;

    /// <summary>
    /// Barra superior limpa: o estado da operação e o aviso de administrador só aparecem quando informam algo
    /// (e somem em janelas estreitas); a busca encolhe abaixo de 900 px.
    /// </summary>
    private void ApplyTopBarMode(double windowWidth)
    {
        if (windowWidth > 0 && !double.IsNaN(windowWidth)) _topBarWidth = windowWidth;
        var width = _topBarWidth;
        // As abas de categoria dividem a barra com a busca: os extras somem antes de as abas perderem o nome
        TitleOptChipHost.Visibility = _showOperationChip && width >= 1600 ? Visibility.Visible : Visibility.Collapsed;
        TitleAdminChipHost.Visibility = _showAdminWarning && width >= 1500 ? Visibility.Visible : Visibility.Collapsed;
        SearchHost.Width = width is > 0 and < 1200 ? 150 : width is > 0 and < 1440 ? 190 : 250;
        TopLogoText.Visibility = width is > 0 and < 1360 ? Visibility.Collapsed : Visibility.Visible;
        var iconOnly = width is > 0 and < 1240;
        if (iconOnly != _topTabsIconOnly) { _topTabsIconOnly = iconOnly; RefreshTopNav(_topNavPage); }
    }

    private void ShowOperationChip(string text)
    {
        TitleOptChip.Text = text;
        _showOperationChip = true;
        ApplyTopBarMode(0);
    }

    private void SetAdminState(bool isAdministrator)
    {
        TitleAdminChip.Text = isAdministrator ? "Administrador" : "Sem administrador";
        TitleAdminDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, isAdministrator ? "SuccessBrush" : "WarningBrush");
        _showAdminWarning = !isAdministrator;
        ApplyTopBarMode(0);
    }

    /// <summary>Textos e selos do item (tudo menos o ícone).</summary>
    private static IEnumerable<FrameworkElement> NavTexts(RadioButton nav)
    {
        if (nav.Content is not Panel panel) yield break;
        foreach (UIElement e in panel.Children)
            if (e is FrameworkElement fe && !(fe is TextBlock tb && tb.Style == (Style)Application.Current.FindResource("NavIcon"))) yield return fe;
    }

    private static string NavName(RadioButton nav) =>
        NavTexts(nav).OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? nav.Tag?.ToString() ?? "";

    /// <summary>No modo compacto o nome vai na dica (e para leitores de tela em qualquer modo).</summary>
    private void RefreshNavTooltips(bool compact)
    {
        foreach (var nav in NavItems())
        {
            var name = Translator.Tr(NavName(nav));
            System.Windows.Automation.AutomationProperties.SetName(nav, name);
            var page = nav.Tag?.ToString() ?? "";
            var locked = PlanAccess.Required(page) > (CurrentLicense?.Tier ?? PlanTier.Base) && page is not ("dashboard" or "mypc" or "history" or "restore" or "settings" or "about" or "patchnotes" or "admin" or "updates" or "isos");
            if (locked) continue; // a dica de plano bloqueado continua (UpdateNavLocks)
            nav.ToolTip = compact ? name : null;
        }
    }
}
