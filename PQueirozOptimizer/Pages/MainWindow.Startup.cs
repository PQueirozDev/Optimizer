using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;
public partial class MainWindow
{
    private string _startupFilter = "";

    private void ShowStartup()
    {
        PageTitle.Text = "Inicialização do Windows";
        var service = new StartupService(_log);
        IReadOnlyList<StartupItem> items;
        try { items = service.GetItems(); }
        catch (Exception ex)
        {
            ContentHost.Children.Clear();
            ContentHost.Children.Add(Card("NÃO FOI POSSÍVEL LER A INICIALIZAÇÃO", ex.Message, Glyphs.Warning, "WarningBrush"));
            return;
        }
        var enabledCount = items.Count(i => i.Enabled);
        PageBadge.Visibility = Visibility.Visible;
        PageBadgeText.Text = $"{enabledCount} de {items.Count} ativos";

        var root = new StackPanel();
        var hero = new DockPanel();
        var chip = IconChip(Glyphs.Power, enabledCount >= 8 ? "Warning" : "Success", 52); DockPanel.SetDock(chip, Dock.Left); hero.Children.Add(chip);
        var heroText = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = Label(enabledCount >= 8 ? "Muitos programas abrindo com o Windows" : "Inicialização sob controle", 18); title.Margin = new Thickness(0, 0, 0, 4);
        heroText.Children.Add(title);
        var sub = Label("Desative o que você não precisa logo ao ligar o PC. O programa continua instalado e pode ser reativado aqui a qualquer momento — é o mesmo mecanismo do Gerenciador de Tarefas.", 12.5, true); sub.Margin = new Thickness(0);
        heroText.Children.Add(sub);
        hero.Children.Add(heroText);
        var heroCard = Surface(hero); heroCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        root.Children.Add(heroCard);

        // Busca
        var searchField = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        var search = new TextBox { Text = _startupFilter, Height = 40 };
        var placeholder = new TextBlock { Text = "Filtrar por nome ou fabricante…", IsHitTestVisible = false, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Visibility = string.IsNullOrEmpty(_startupFilter) ? Visibility.Visible : Visibility.Collapsed };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        searchField.Children.Add(search); searchField.Children.Add(placeholder);
        root.Children.Add(searchField);

        var list = new StackPanel();
        void Render()
        {
            list.Children.Clear();
            var filtered = items.Where(i => string.IsNullOrWhiteSpace(_startupFilter)
                || i.Name.Contains(_startupFilter, StringComparison.CurrentCultureIgnoreCase)
                || i.Publisher.Contains(_startupFilter, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (filtered.Count == 0) { list.Children.Add(Label("Nenhum programa de inicialização encontrado.", 13, true)); return; }
            foreach (var item in filtered)
            {
                var row = new DockPanel();
                var toggle = new CheckBox { IsChecked = item.Enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Ativo ao iniciar o Windows" };
                var state = Pill(item.Enabled ? "Ativo" : "Desativado", item.Enabled ? "Success" : "Accent");
                state.Margin = new Thickness(12, 0, 16, 0);
                var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                right.Children.Add(state); right.Children.Add(toggle);
                DockPanel.SetDock(right, Dock.Right); row.Children.Add(right);
                var icon = IconChip(Glyphs.Power, item.Enabled ? "Accent" : "Info", 38); icon.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
                var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
                var name = Label(item.Name, 14); name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0, 0, 0, 2);
                text.Children.Add(name);
                var details = string.IsNullOrWhiteSpace(item.Publisher) ? item.Location : $"{item.Publisher} · {item.Location}";
                var detailText = Label(details, 12, true); detailText.Margin = new Thickness(0, 0, 0, 2);
                text.Children.Add(detailText);
                var command = Label(item.Command, 11, true); command.Margin = new Thickness(0); command.TextTrimming = TextTrimming.CharacterEllipsis; command.TextWrapping = TextWrapping.NoWrap; command.ToolTip = item.Command;
                command.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                text.Children.Add(command);
                row.Children.Add(text);

                toggle.Checked += (_, _) => Change(item, true);
                toggle.Unchecked += (_, _) => Change(item, false);
                var card = Surface(row); card.Padding = new Thickness(18, 14, 18, 14); card.Margin = new Thickness(0, 0, 0, 10);
                if (!item.Enabled) card.Opacity = 0.75;
                list.Children.Add(card);
            }
        }
        void Change(StartupItem item, bool enabled)
        {
            try
            {
                service.SetEnabled(item, enabled);
                OperationStatus.Text = $"{item.Name}: {(enabled ? "ativado" : "desativado")} na inicialização.";
                ShowStartup();
            }
            catch (Exception ex)
            {
                _log.Write("ERROR", $"Inicialização: {item.Name}: {ex.Message}");
                Msg("Não foi possível alterar este item: " + ex.Message, "Inicialização do Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowStartup();
            }
        }
        search.TextChanged += (_, _) =>
        {
            _startupFilter = search.Text;
            placeholder.Visibility = string.IsNullOrEmpty(search.Text) ? Visibility.Visible : Visibility.Collapsed;
            Render();
        };
        Render();
        root.Children.Add(list);
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }
}
