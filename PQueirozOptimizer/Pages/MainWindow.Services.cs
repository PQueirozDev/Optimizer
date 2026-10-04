using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Serviços: grupos de serviços e estado dos serviços (verificação da 2ª etapa).</summary>
public partial class MainWindow
{
    private int _servicesTab;

    // ================= Serviços =================
    private void ShowServices()
    {
        PageTitle.Text = "Serviços";
        var disabledCount = ServiceGroupsService.Groups.Count(ServiceGroupsService.IsDisabled);
        PageBadge.Visibility = Visibility.Visible;
        PageBadgeText.Text = $"{disabledCount} de {ServiceGroupsService.Groups.Length} desligados";
        var root = new StackPanel();
        root.Children.Add(Tabs(new[] { (Glyphs.Services, "Grupos"), (Glyphs.Diagnostic, "Estado dos serviços") }, _servicesTab, tab => { _servicesTab = tab; ShowServices(); AnimatePageIn(); }));
        if (_servicesTab == 1)
        {
            root.Children.Add(ServiceStatusCard());
            ContentHost.Children.Clear();
            ContentHost.Children.Add(root);
            return;
        }
        var service = new ServiceGroupsService(_log);
        var host = new StackPanel();
        foreach (var category in ServiceGroupsService.Groups.Select(g => g.Category).Distinct())
        {
            var section = new StackPanel();
            section.Children.Add(SectionHeader(category));
            foreach (var group in ServiceGroupsService.Groups.Where(g => g.Category == category))
            {
                var existing = ServiceGroupsService.Existing(group);
                var disabled = ServiceGroupsService.IsDisabled(group);
                var check = new CheckBox { IsChecked = disabled, IsEnabled = existing.Length > 0 };
                var detail = group.Description + (group.Warning != null ? "\n⚠ " + group.Warning : "") + $"\nServiços: {string.Join(", ", existing.DefaultIfEmpty("nenhum neste Windows"))}";
                // A etiqueta descreve o interruptor (o grupo está aplicado ou não), nunca o contrário dele
                var row = ChoiceRow(check, group.Name, detail, disabled ? "Aplicado" : "Padrão", disabled ? "Success" : "Info");
                BindActionToggle(check,
                    off => off
                        ? ExecuteTrackedAsync("Desligando: " + group.Name, async _ => await service.DisableAsync(group))
                        : ExecuteTrackedAsync("Restaurando: " + group.Name, async _ => await service.RestoreAsync(group)),
                    // Atualiza só a etiqueta: redesenhar a página trocava o interruptor no meio da animação e voltava a rolagem ao topo
                    () => { var now = ServiceGroupsService.IsDisabled(group); SetChoiceRowPill(check, now ? "Aplicado" : "Padrão", now ? "Success" : "Info"); });
                section.Children.Add(row);
            }
            host.Children.Add(Surface(section));
        }
        root.Children.Add(Mark(host, "services.list"));

        var defender = new DockPanel();
        var open = IconButton(Glyphs.ChevronRight, "Abrir");
        open.Click += (_, _) => OpenGamingTab(3);
        DockPanel.SetDock(open, Dock.Right); defender.Children.Add(open);
        defender.Children.Add(FeatureHeader(Glyphs.Shield, "Warning", "Windows Defender", "Exclusões de pastas de jogos e liga/desliga da proteção em tempo real ficam em Modo Jogo → Sistema.", "Em Modo Jogo", "Info"));
        root.Children.Add(Surface(defender));
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    /// <summary>Serviços → Estado dos serviços: o que está rodando, o que está parado e botões para iniciar ou parar.</summary>
    private UIElement ServiceStatusCard()
    {
        var states = new ServiceStateService(_log);
        var root = new StackPanel();

        // Verificação de anti-cheat (2ª etapa): os cinco serviços conferidos
        var checkedStates = ServiceStateService.CheckedServices.Select(ServiceStateService.Read).ToList();
        var stopped = checkedStates.Where(s => s.Exists && !s.Running).ToList();
        var head = new DockPanel();
        if (stopped.Count > 0)
        {
            var startAll = IconButton(Glyphs.Play, stopped.Count == 1 ? "Iniciar o serviço parado" : $"Iniciar os {stopped.Count} parados", primary: true);
            startAll.VerticalAlignment = VerticalAlignment.Top;
            startAll.Click += async (_, _) =>
            {
                await ExecuteTrackedAsync("Iniciando serviços da verificação", async token => { foreach (var s in stopped) await states.StartAsync(s.Name, token); });
                if (_currentPage == "services") ShowServices();
            };
            DockPanel.SetDock(startAll, Dock.Right); head.Children.Add(startAll);
        }
        var running = checkedStates.Count(s => s.Running);
        head.Children.Add(SectionHeader("Verificação de serviços (2ª etapa)",
            $"{running} de {checkedStates.Count} rodando. Verificações de anti-cheat e de campeonatos conferem se estes serviços estão ativos; parados, a verificação pode reprovar o PC."));
        var check = new StackPanel();
        check.Children.Add(head);
        check.Children.Add(Notice(stopped.Count == 0
            ? "Todos os serviços da verificação estão rodando."
            : $"Parados: {string.Join(", ", stopped.Select(s => s.Name))}. Use \"Iniciar\" antes da verificação. Para não pará-los de novo, use a otimização \"Avançada sem parar serviços\" e desmarque SysMain e Telemetria no Modo Jogo.", stopped.Count == 0 ? "Success" : "Warning"));
        foreach (var state in checkedStates) check.Children.Add(ServiceStateRow(state, states));
        root.Children.Add(Surface(check));

        var others = new StackPanel();
        others.Children.Add(SectionHeader("Outros serviços que o app pode parar", "Grupos de Serviços, otimizações e Modo Jogo. Parar aqui vale até o Windows precisar do serviço ou reiniciar; para desligar de vez, use a aba Grupos."));
        foreach (var state in ServiceStateService.OtherServices().Select(ServiceStateService.Read).Where(s => s.Exists).OrderBy(s => s.Running).ThenBy(s => s.DisplayName))
            others.Children.Add(ServiceStateRow(state, states));
        root.Children.Add(Surface(others));
        return root;
    }

    private Border ServiceStateRow(ServiceState state, ServiceStateService states)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Label(state.DisplayName, 13.5); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0); title.Tag = Translator.SystemDataTag;
        var startLabel = state.StartType switch { "auto" => "Automático", "delayed-auto" => "Automático (atrasado)", "disabled" => "Desativado", "demand" => "Manual", _ => state.StartType };
        var detail = Label(state.Exists ? $"{state.Name} · Início: {startLabel}" : $"{state.Name} · Não existe neste Windows", 12, true); detail.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(title); text.Children.Add(detail);
        grid.Children.Add(text);

        var (pillText, tone) = !state.Exists ? ("Ausente", "Warning") : state.Running ? ("Rodando", "Success") : state.Disabled ? ("Desativado", "Danger") : ("Parado", "Warning");
        var pill = Pill(pillText, tone); pill.Margin = new Thickness(12, 0, 12, 0); pill.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(pill, 1); grid.Children.Add(pill);

        if (state.Exists)
        {
            var action = state.Running ? IconButton(Glyphs.Pause, "Parar") : IconButton(Glyphs.Play, "Iniciar", primary: true);
            action.Margin = new Thickness(0); action.MinWidth = 104;
            action.IsEnabled = !state.Running || state.CanStop;
            if (state.Running && !state.CanStop) action.ToolTip = "O Windows não permite parar este serviço.";
            action.Click += async (_, _) =>
            {
                var title2 = (state.Running ? "Parando: " : "Iniciando: ") + state.DisplayName;
                await ExecuteTrackedAsync(title2, token => state.Running ? states.StopAsync(state.Name, token) : states.StartAsync(state.Name, token));
                if (_currentPage == "services") ShowServices();
            };
            Grid.SetColumn(action, 2); grid.Children.Add(action);
        }
        var row = new Border { Child = grid, Padding = new Thickness(18, 12, 18, 12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
        row.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        row.SetResourceReference(Border.BorderBrushProperty, state.Exists && !state.Running && ServiceStateService.CheckedServices.Contains(state.Name) ? "WarningBrush" : "BorderSubtleBrush");
        return row;
    }
}
