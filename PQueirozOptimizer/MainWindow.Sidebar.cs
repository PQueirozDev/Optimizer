using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Barra lateral compactável: só ícones (72 px) com o nome nas dicas, por preferência, Ctrl+B ou automaticamente
/// em janelas estreitas. A navegação por teclado (Tab e setas) funciona nos dois modos.
/// </summary>
public partial class MainWindow
{
    private bool _sidebarCompactApplied;
    private bool? _sidebarForcedByWidth;

    private bool SidebarShouldBeCompact => AppearanceService.Current.CompactSidebar || (_sidebarForcedByWidth ?? false);

    private void InitSidebar()
    {
        SizeChanged += (_, e) =>
        {
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
        ApplySidebarMode();
    }

    private void ApplySidebarMode()
    {
        var compact = SidebarShouldBeCompact;
        if (compact == _sidebarCompactApplied && RootGrid.ColumnDefinitions[0].Width.Value is 72 or 248) { RefreshNavTooltips(compact); return; }
        _sidebarCompactApplied = compact;
        RootGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 72 : 248);
        SidebarLogoText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarLogo.Margin = compact ? new Thickness(18, 18, 0, 14) : new Thickness(20, 18, 16, 14);
        HudPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarScroll.Padding = compact ? new Thickness(8, 0, 8, 0) : new Thickness(12, 0, 12, 0);
        foreach (var child in NavStack.Children)
        {
            if (child is TextBlock header) header.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; // títulos das seções
            if (child is not RadioButton nav) continue;
            nav.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            foreach (var text in NavTexts(nav)) text.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }
        RefreshNavTooltips(compact);
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
        foreach (var nav in NavStack.Children.OfType<RadioButton>())
        {
            var name = Translator.Tr(NavName(nav));
            System.Windows.Automation.AutomationProperties.SetName(nav, name);
            var page = nav.Tag?.ToString() ?? "";
            var locked = PlanAccess.Required(page) > (CurrentLicense?.Tier ?? PlanTier.Base) && page is not ("dashboard" or "history" or "restore" or "settings" or "about" or "patchnotes" or "admin" or "isos");
            if (locked) continue; // a dica de plano bloqueado continua (UpdateNavLocks)
            nav.ToolTip = compact ? name : null;
        }
    }
}
