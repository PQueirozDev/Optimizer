using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Correções rápidas do Windows.</summary>
public partial class MainWindow
{
    // ================= Correções =================
    private void ShowFixes()
    {
        PageTitle.Text = "Correções";
        PageBadge.Visibility = Visibility.Visible;
        PageBadgeText.Text = $"{FixesService.Fixes.Length} correções";
        var root = new StackPanel();
        var filterBar = new DockPanel { Margin = new Thickness(0, 0, 0, AppearanceService.Space(18) ) };
        var search = new TextBox { Width = 270, Height = 34, Padding = new Thickness(12, 7, 12, 7), FontSize = 12.5, ToolTip = "Filtrar correções" };
        search.SetResourceReference(Control.BackgroundProperty, "CardBgBrush");
        search.SetResourceReference(Control.BorderBrushProperty, "BorderSubtleBrush");
        search.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var category = new ComboBox { Width = 150, Height = 34, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(8, 5, 8, 5) };
        foreach (var value in new[] { "Todos", "Windows", "Hardware", "Rede", "Explorer", "Jogos" }) category.Items.Add(value);
        category.SelectedIndex = 0;
        var sort = new ComboBox { Width = 170, Height = 34, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(8, 5, 8, 5) };
        sort.Items.Add("Mais usadas"); sort.Items.Add("Mais seguras"); sort.Items.Add("Nome");
        sort.SelectedIndex = 0;
        filterBar.Children.Add(search);
        filterBar.Children.Add(category);
        filterBar.Children.Add(sort);
        root.Children.Add(filterBar);
        var grid = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -14, 0) }, 360, 2);
        void Render()
        {
            grid.Children.Clear();
            var query = search.Text.Trim();
            var selected = category.SelectedItem?.ToString() ?? "Todos";
            var filtered = FixesService.Fixes.Where(f =>
                (selected == "Todos" || f.Category == selected) &&
                (query.Length == 0 || f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || f.Description.Contains(query, StringComparison.OrdinalIgnoreCase)));
            filtered = sort.SelectedIndex switch
            {
                0 => filtered.OrderByDescending(f => _fixUse.GetValueOrDefault(f.Id)).ThenBy(f => f.Name),
                1 => filtered.OrderBy(f => f.NeedsRestart).ThenBy(f => f.Steps.Length).ThenBy(f => f.Name),
                _ => filtered.OrderBy(f => f.Name)
            };
            foreach (var fix in filtered)
            {
                var body = new DockPanel();
                var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
                var runBtn = IconButton(Glyphs.Play, "Executar correção", primary: true);
                runBtn.Click += async (_, _) =>
                {
                    _fixUse[fix.Id] = _fixUse.GetValueOrDefault(fix.Id) + 1;
                    if (fix.NeedsRestart && Msg($"\"{fix.Name}\" precisa reiniciar o PC para terminar. Executar agora?", "Correções", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    FixResult? result = null;
                    var progress = new Progress<string>(line => OperationStatus.Text = line);
                    await ExecuteTrackedAsync(fix.Name, async _ => result = await new FixesService(_log).RunAsync(fix, progress));
                    if (result is { } r && r.Succeeded < r.Total)
                        ShowToast(fix.Name, $"{r.Succeeded}/{r.Total} etapas concluídas. Erro: {r.FirstError}", r.Succeeded == 0 ? "Danger" : "Warning");
                };
                DockPanel.SetDock(runBtn, Dock.Right); footer.Children.Add(runBtn);
                var pills = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                pills.Children.Add(Pill(fix.Category, "Info"));
                if (fix.NeedsRestart) { var p = Pill("Reinicia o PC", "Warning"); p.Margin = new Thickness(6, 0, 0, 0); pills.Children.Add(p); }
                footer.Children.Add(pills);
                DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
                var head = new DockPanel();
                var chip = IconChip(fix.Glyph, "Accent", 44); chip.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(chip, Dock.Left); head.Children.Add(chip);
                var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
                var t = Label(fix.Name, 14); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 1, 0, 4); text.Children.Add(t);
                var details = new Expander { Header = "Ver detalhes", Margin = new Thickness(0, 6, 0, 0), IsExpanded = AppearanceService.Current.Density != Models.Density.Compact };
                var d = Label(fix.Description, 12, true); d.Margin = new Thickness(0); details.Content = d;
                text.Children.Add(details);
                head.Children.Add(text);
                body.Children.Add(head);
                var gap = AppearanceService.Current.Density == Models.Density.Compact ? 8 : 14;
                var card = Surface(body); card.Margin = new Thickness(0, 0, gap, gap);
                grid.Children.Add(card);
            }
        }
        search.TextChanged += (_, _) => Render();
        category.SelectionChanged += (_, _) => Render();
        sort.SelectionChanged += (_, _) => Render();
        Render();
        root.Children.Add(grid);
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }
}
