using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;
public partial class MainWindow
{
    #region Optimization Page
    private static string CategoryTone(string category) => category switch
    {
        "Desempenho" => "Accent",
        "Limpeza" => "Info",
        "Diagnóstico" => "Success",
        "Manutenção" => "Warning",
        "Segurança" => "Danger",
        _ => "Accent",
    };

    private void ShowOptimization()
    {
        PageTitle.Text = "Otimização";
        var activeProfile = _configService.GetActiveProfile();
        var allowedIds = new HashSet<string>(activeProfile.EnabledOptimizations, StringComparer.OrdinalIgnoreCase);
        // Perfis salvos antes da otimização sem parar serviços: ela aparece junto com a Versão Avançada
        if (allowedIds.Contains("gamer")) allowedIds.Add("gamerservicos");

        var visibleOptimizations = ConfigService.AllOptimizations
            .Where(o => allowedIds.Contains(o.Id))
            .ToList();

        PageBadge.Visibility = Visibility.Visible;
        PageBadgeText.Text = $"{activeProfile.Name} · {visibleOptimizations.Count} ativas";

        var root = new StackPanel();

        // 1. Perfil ativo
        var profileGrid = new DockPanel();
        var customizeBtn = IconButton(Glyphs.Settings, "Personalizar");
        customizeBtn.Margin = new Thickness(16, 0, 0, 0); customizeBtn.VerticalAlignment = VerticalAlignment.Center;
        customizeBtn.Click += (_, _) => NavigateTo("settings");
        DockPanel.SetDock(customizeBtn, Dock.Right); profileGrid.Children.Add(customizeBtn);
        var profileChip = OutlineChip(OptimizationIcons.For("padrao"), 44, primary: true); DockPanel.SetDock(profileChip, Dock.Left); profileGrid.Children.Add(profileChip);
        var profileText = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var profileTitle = Label("Perfil ativo: " + activeProfile.Name, 15); profileTitle.FontWeight = FontWeights.SemiBold; profileTitle.Margin = new Thickness(0);
        profileText.Children.Add(profileTitle);
        var profileDesc = Label(string.IsNullOrWhiteSpace(activeProfile.Description)
            ? $"Exibindo {visibleOptimizations.Count} de {ConfigService.AllOptimizations.Count} otimizações configuradas."
            : activeProfile.Description, 12, true);
        profileDesc.Margin = new Thickness(0, 3, 0, 0);
        profileText.Children.Add(profileDesc);
        profileGrid.Children.Add(profileText);
        var profileCard = Surface(profileGrid);
        profileCard.Padding = new Thickness(20, 16, 20, 16);
        profileCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        root.Children.Add(profileCard);

        // 2. Filtro por categoria
        var categoryRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var categories = new[] { "todas", "Desempenho", "Limpeza", "Manutenção", "Diagnóstico", "Segurança" };
        foreach (var cat in categories)
        {
            var count = cat == "todas" ? visibleOptimizations.Count : visibleOptimizations.Count(o => o.Category.Equals(cat, StringComparison.OrdinalIgnoreCase));
            var isSelected = _currentOptCategory.Equals(cat, StringComparison.OrdinalIgnoreCase);
            var pill = new Button
            {
                Content = $"{(cat == "todas" ? "Todas" : cat)}  {count}",
                Tag = cat,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 8),
                FontSize = 12.5,
            };
            if (isSelected) Primary(pill);
            else
            {
                pill.SetResourceReference(Button.BackgroundProperty, "CardBgBrush");
                pill.SetResourceReference(Button.ForegroundProperty, "MutedBrush");
                pill.SetResourceReference(Button.BorderBrushProperty, "BorderSubtleBrush");
            }
            pill.Click += (s, _) =>
            {
                _currentOptCategory = (s as Button)?.Tag?.ToString() ?? "todas";
                ShowOptimization();
            };
            categoryRow.Children.Add(pill);
        }
        root.Children.Add(categoryRow);

        var filteredList = _currentOptCategory.Equals("todas", StringComparison.OrdinalIgnoreCase)
            ? visibleOptimizations
            : visibleOptimizations.Where(o => o.Category.Equals(_currentOptCategory, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filteredList.Count == 0)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
            var chip = IconChip(Glyphs.Info, "Accent", 52); chip.Margin = new Thickness(0, 0, 0, 14);
            empty.Children.Add(chip);
            var emptyTitle = Label("Nenhuma otimização nesta categoria", 15); emptyTitle.FontWeight = FontWeights.SemiBold; emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(emptyTitle);
            var emptySub = Label("Ative mais otimizações em Configurações ou veja todas as categorias.", 12.5, true); emptySub.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(emptySub);
            var btn = IconButton(Glyphs.ChevronRight, "Ver todas"); btn.HorizontalAlignment = HorizontalAlignment.Center; btn.Margin = new Thickness(0, 8, 0, 0);
            btn.Click += (_, _) => { _currentOptCategory = "todas"; ShowOptimization(); };
            empty.Children.Add(btn);
            root.Children.Add(Surface(empty));
        }
        else
        {
            // 3. Cartões em duas colunas
            var grid = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -14, 0) }, 360, 2);
            foreach (var opt in filteredList)
            {
                var body = new DockPanel();

                var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
                var execBtn = IconButton(Glyphs.ChevronRight, opt.Operation == "reverter" ? "Abrir" : "Revisar", primary: opt.Id is "padrao" or "gamer" or "quickclean");
                execBtn.Tag = opt.Operation; execBtn.Click += RunOperation_Click; execBtn.Margin = new Thickness(0);
                DockPanel.SetDock(execBtn, Dock.Right); footer.Children.Add(execBtn);
                var categoryPill = Pill(opt.Category, CategoryTone(opt.Category));
                categoryPill.HorizontalAlignment = HorizontalAlignment.Left;
                footer.Children.Add(categoryPill);
                DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);

                var head = new DockPanel();
                var chip = OutlineChip(OptimizationIcons.For(opt.Id), 42, primary: opt.Id is "padrao" or "gamer" or "quickclean"); chip.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(chip, Dock.Left); head.Children.Add(chip);
                var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
                var title = Label(opt.Name, 14.5); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 1, 0, 4);
                text.Children.Add(title);
                var desc = Label(opt.Description, 12, true); desc.Margin = new Thickness(0); desc.LineHeight = 18;
                text.Children.Add(desc);
                head.Children.Add(text);
                body.Children.Add(head);

                var card = Surface(body);
                card.Margin = new Thickness(0, 0, 14, 14);
                card.Padding = new Thickness(20, 18, 20, 18);
                grid.Children.Add(card);
            }
            root.Children.Add(grid);
        }

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }
    #endregion
}
