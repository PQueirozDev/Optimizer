using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;
public partial class MainWindow
{
    private GamingService? _gaming;
    private GamingService Gaming => _gaming ??= new GamingService(_log);

    #region Gaming Page
    private void ShowGaming()
    {
        PageTitle.Text = "Modo Jogo";
        var session = GamingService.ActiveSession();
        PageBadge.Visibility = session is null ? Visibility.Collapsed : Visibility.Visible;
        PageBadgeText.Text = "Modo Jogo ativo";
        UpdateGameModeBadge();
        var root = new StackPanel();
        root.Children.Add(Mark(Tabs(new[] { (Glyphs.Game, "Modo Jogo"), (Glyphs.Settings, "Jogos"), (Glyphs.Monitor, "NVIDIA"), (Glyphs.Lightning, "Sistema") }, _gamingTab, tab =>
        {
            _gamingTab = tab;
            ShowGaming();
            AnimatePageIn();
        }), "gaming.tabs"));

        switch (_gamingTab)
        {
            case 1:
                root.Children.Add(GameConfigsCard());
                root.Children.Add(TwoColumns(GameProfilesCard(), RedistributablesCard()));
                break;
            case 2:
                root.Children.Add(NvidiaCard());
                break;
            case 3:
                root.Children.Add(TwoColumns(QuickBoostsCard(), DefenderCard()));
                root.Children.Add(Win32PriorityCard());
                break;
            default:
                root.Children.Add(session is null ? GameModeSetupCard() : GameModeActiveCard(session));
                if (GamingService.DetectX3d(GamingService.ProcessorName()) is { } x3d) root.Children.Add(X3dCard(x3d));
                break;
        }

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private static Grid TwoColumns(UIElement left, UIElement right)
    {
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.Children.Add(left);
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);
        return columns;
    }

    private Border GameModeSetupCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(FeatureHeader(Glyphs.Game, "Accent", "Modo Jogo temporário",
            "Fecha programas em segundo plano, pausa serviços que disputam disco e internet e ativa o plano Alto desempenho enquanto você joga. Ao desativar, tudo volta como estava — mesmo que o PC reinicie no meio.",
            "Desativado", "Accent"));

        var running = GamingService.RunningBackgroundApps();
        var appChecks = new List<(CheckBox Check, string Name)>();
        var appsHeader = Label("Programas para fechar", 13); appsHeader.FontWeight = FontWeights.SemiBold; appsHeader.Margin = new Thickness(0, 18, 0, 8);
        panel.Children.Add(appsHeader);
        var appsGrid = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 0) }, 360, 2);
        // Primeiro os que estão abertos agora: são os que fazem diferença
        foreach (var app in GamingService.BackgroundApps.OrderByDescending(a => running.Contains(a.Name)))
        {
            var isRunning = running.Contains(app.Name);
            var check = new CheckBox { IsChecked = isRunning && !app.Description.StartsWith("Feche só", StringComparison.Ordinal) };
            var row = ChoiceRow(check, app.Name, app.Description, isRunning ? "Aberto" : "Fechado", isRunning ? "Warning" : "Success");
            row.Margin = new Thickness(0, 0, 10, 10);
            appsGrid.Children.Add(row);
            appChecks.Add((check, app.Name));
        }
        panel.Children.Add(appsGrid);

        var servicesHeader = Label("Serviços para pausar durante a sessão", 13); servicesHeader.FontWeight = FontWeights.SemiBold; servicesHeader.Margin = new Thickness(0, 10, 0, 8);
        panel.Children.Add(servicesHeader);
        var serviceGrid = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 0) }, 360, 2);
        var serviceChecks = new List<(CheckBox Check, string Name)>();
        foreach (var service in GamingService.PausableServices)
        {
            var check = new CheckBox { IsChecked = service.Default };
            var row = ChoiceRow(check, service.Title, service.Description, "Temporário", "Info");
            row.Margin = new Thickness(0, 0, 10, 10);
            serviceGrid.Children.Add(row);
            serviceChecks.Add((check, service.Name));
        }
        panel.Children.Add(serviceGrid);

        var x3d = GamingService.DetectX3d(GamingService.ProcessorName());
        var planCheck = new CheckBox { IsChecked = x3d is not { DualCcd: true } };
        var planRow = ChoiceRow(planCheck, PowerPlanService.IsQrzInstalled() ? "Plano de energia Qrz" : "Plano de energia Alto desempenho",
            x3d is { DualCcd: true } ? "Desmarcado: no seu Ryzen X3D o plano Equilibrado é o que deixa o Windows usar o CCD com cache." : "O processador mantém a frequência alta durante o jogo. O plano anterior volta ao desativar.",
            "Temporário", "Info");
        var standbyCheck = new CheckBox { IsChecked = true };
        var standbyRow = ChoiceRow(standbyCheck, "Liberar memória em espera ao iniciar", "Esvazia o cache de arquivos que o Windows guarda na RAM. Ajuda em jogos com engasgos quando a memória está quase cheia.", "Pontual", "Success");
        var options = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 0) }, 360, 2);
        planRow.Margin = new Thickness(0, 0, 10, 10); standbyRow.Margin = new Thickness(0, 0, 10, 10);
        options.Children.Add(planRow); options.Children.Add(standbyRow);
        panel.Children.Add(options);

        var start = Mark(IconButton(Glyphs.Play, "Ativar Modo Jogo", primary: true), "gaming.start");
        start.Click += async (_, _) =>
        {
            var apps = appChecks.Where(c => c.Check.IsChecked == true).Select(c => c.Name).ToList();
            var services = serviceChecks.Where(c => c.Check.IsChecked == true).Select(c => c.Name).ToList();
            var progress = new Progress<string>(line => { OperationStatus.Text = line; _log.Write("INFO", line); });
            await ExecuteTrackedAsync("Ativando Modo Jogo", async _ =>
                await Gaming.StartSessionAsync(apps, services, planCheck.IsChecked == true, standbyCheck.IsChecked == true, progress));
            if (_currentPage == "gaming") ShowGaming();
            UpdateGameModeBadge();
        };
        var summary = Label("Nada é desinstalado nem desativado de forma permanente.", 12, true); summary.Margin = new Thickness(0); summary.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(ActionBar(summary, start));

        var card = Surface(panel);
        card.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        return card;
    }

    private Border GameModeActiveCard(GamingService.GameSession session)
    {
        var panel = new StackPanel();
        var since = session.StartedAtUtc.ToLocalTime();
        panel.Children.Add(FeatureHeader(Glyphs.Game, "Success", "Modo Jogo ativo",
            $"Ativo desde {since:HH:mm} de {since:dd/MM}. Ao terminar de jogar, desative para reiniciar os serviços e voltar ao plano de energia anterior. Os programas fechados podem ser abertos de novo normalmente.",
            "Ativo", "Success"));
        var details = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var name in session.ClosedApps) { var p = Pill("Fechado: " + name, "Warning"); p.Margin = new Thickness(0, 0, 6, 6); details.Children.Add(p); }
        foreach (var name in session.StoppedServices)
        {
            var title = GamingService.PausableServices.FirstOrDefault(s => s.Name == name)?.Title ?? name;
            var p = Pill("Pausado: " + title, "Info"); p.Margin = new Thickness(0, 0, 6, 6); details.Children.Add(p);
        }
        if (session.PreviousPowerPlan != null) { var p = Pill("Plano Alto desempenho", "Accent"); p.Margin = new Thickness(0, 0, 6, 6); details.Children.Add(p); }
        if (details.Children.Count > 0) panel.Children.Add(details);

        var stop = IconButton(Glyphs.Stop, "Desativar Modo Jogo", primary: true);
        stop.Click += async (_, _) =>
        {
            var progress = new Progress<string>(line => { OperationStatus.Text = line; _log.Write("INFO", line); });
            await ExecuteTrackedAsync("Desativando Modo Jogo", async _ => await Gaming.EndSessionAsync(progress));
            if (_currentPage == "gaming") ShowGaming();
            UpdateGameModeBadge();
        };
        var standby = IconButton(Glyphs.Memory, "Liberar memória em espera");
        standby.Click += (_, _) => PurgeStandbyNow();
        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(stop); actions.Children.Add(standby);
        panel.Children.Add(actions);

        var card = Surface(panel);
        card.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "SuccessSoftBrush");
        return card;
    }

    private void PurgeStandbyNow()
    {
        var ok = GamingService.PurgeStandbyList();
        _log.Write(ok ? "SUCCESS" : "WARN", ok ? "Memória em espera liberada" : "Não foi possível liberar a memória em espera");
        OperationStatus.Text = ok ? "Memória em espera liberada. O Windows volta a usá-la como cache conforme você abre arquivos." : "Não foi possível liberar a memória em espera (é preciso executar como administrador).";
    }

    /// <summary>Recomendações para Ryzen X3D: driver de chipset, Game Bar e plano de energia.</summary>
    private Border X3dCard(X3dInfo x3d)
    {
        var panel = new StackPanel();
        var hasDriver = GamingService.HasX3dChipsetDriver();
        var gameBar = GamingService.IsGameBarAllowed();
        panel.Children.Add(FeatureHeader(Glyphs.Chip, "Warning", $"Ryzen {x3d.Model} detectado",
            x3d.DualCcd
                ? "Seu processador tem dois CCDs e só um tem o 3D V-Cache. Para o Windows mandar o jogo ao CCD certo, ele precisa do driver de chipset da AMD, da Game Bar ativa e do plano de energia Equilibrado."
                : "Processador com 3D V-Cache em um único CCD. Mantenha o driver de chipset da AMD atualizado e ajuste a BIOS (EXPO e PBO) para tirar o máximo do cache.",
            x3d.DualCcd ? "Dois CCDs" : "Um CCD", "Warning"));
        var checks = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        void Check(bool ok, string good, string bad)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(GlyphIcon(ok ? Glyphs.Check : Glyphs.Warning, 13, ok ? "SuccessBrush" : "WarningBrush"));
            var t = Label(ok ? good : bad, 12.5); t.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(t);
            checks.Children.Add(row);
        }
        if (x3d.DualCcd)
        {
            Check(hasDriver, "Driver AMD 3D V-Cache Performance Optimizer instalado", "Driver AMD 3D V-Cache não encontrado: instale o pacote de chipset da AMD");
            Check(gameBar, "Game Bar permitida (o Windows usa ela para reconhecer jogos)", "Game Bar bloqueada por política: o Windows não reconhece o jogo para usar o CCD com cache");
            var plan = GamingService.ActivePowerPlan();
            Check(plan?.Equals("381b4222-f694-41f0-9685-ff5bb260df2e", StringComparison.OrdinalIgnoreCase) == true, "Plano de energia Equilibrado ativo", "Plano diferente do Equilibrado: a AMD recomenda o Equilibrado para estacionar o CCD sem cache");
        }
        else Check(true, "A Versão Avançada do otimizador já é compatível com o seu processador", "");
        panel.Children.Add(checks);
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var drivers = IconButton(Glyphs.Download, "Driver de chipset AMD", primary: x3d.DualCcd && !hasDriver);
        drivers.Click += (_, _) => OpenUrl("https://www.amd.com/en/support/download/drivers.html");
        var bios = IconButton(Glyphs.Chip, "Ajustes de BIOS");
        bios.Click += (_, _) => NavigateTo("bios");
        actions.Children.Add(drivers); actions.Children.Add(bios);
        if (x3d.DualCcd && !gameBar)
        {
            var fix = IconButton(Glyphs.Repair, "Liberar Game Bar");
            fix.Click += async (_, _) =>
            {
                try
                {
                    await PowerShellBridge.RunScriptAsync("Remove-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR' -Name AllowGameDVR -ErrorAction SilentlyContinue");
                    _log.Write("SUCCESS", "Game Bar liberada para o agendamento do Ryzen X3D");
                    ShowGaming();
                }
                catch (Exception ex) { Msg(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            actions.Children.Add(fix);
        }
        panel.Children.Add(actions);
        return Surface(panel);
    }

    private Border GameProfilesCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Perfis de jogos", "Ajustes que o Windows aplica sempre que o jogo abrir. Removíveis a qualquer momento."));
        var gpu = new CheckBox { IsChecked = true, Content = "Usar a placa de vídeo dedicada", Margin = new Thickness(0, 0, 0, 6) };
        var fso = new CheckBox { IsChecked = false, Content = "Desativar otimizações de tela cheia", Margin = new Thickness(0, 0, 0, 6) };
        var priority = new CheckBox { IsChecked = true, Content = "Prioridade de CPU alta", Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(gpu); panel.Children.Add(fso); panel.Children.Add(priority);
        var add = IconButton(Glyphs.Add, "Adicionar jogo (.exe)", primary: true);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Jogo (*.exe)|*.exe", Title = "Escolha o executável do jogo" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                Gaming.ApplyGameProfile(dialog.FileName, gpu.IsChecked == true, fso.IsChecked == true, priority.IsChecked == true);
                OperationStatus.Text = $"Perfil aplicado: {Path.GetFileName(dialog.FileName)}. Vale na próxima vez que o jogo abrir.";
                ShowGaming();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Msg(ex.Message, "Perfis de jogos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        panel.Children.Add(add);

        var profiles = Gaming.GameProfiles();
        if (profiles.Count > 0)
        {
            var list = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            foreach (var p in profiles)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var remove = new Button { Content = GlyphIcon(Glyphs.Delete, 12, "DangerBrush"), Padding = new Thickness(8, 5, 8, 5), ToolTip = "Remover perfil" };
                remove.SetResourceReference(StyleProperty, "GhostButton");
                var path = p.Meta.TryGetValue("path", out var value) ? value : "";
                remove.Click += (_, _) =>
                {
                    try { Gaming.RevertGameProfile(path); ShowGaming(); }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { Msg(ex.Message, "Perfis de jogos", MessageBoxButton.OK, MessageBoxImage.Warning); }
                };
                DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                var thumb = GameBanner(p.Title, 40, 9, () => GameArtService.ExecutableArtAsync(path)); thumb.Width = 72;
                DockPanel.SetDock(thumb, Dock.Left); row.Children.Add(thumb);
                var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var name = Label(p.Title, 12.5); name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0); name.Tag = Translator.SystemDataTag;
                text.Children.Add(name);
                var opts = Label(p.Meta.TryGetValue("options", out var o) ? o : "", 11, true); opts.Margin = new Thickness(0);
                text.Children.Add(opts);
                row.Children.Add(text);
                list.Children.Add(row);
            }
            panel.Children.Add(list);
        }
        return Surface(panel);
    }

    private Border QuickBoostsCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Ajustes rápidos", "Ações pontuais e ajustes reversíveis com um clique."));

        var qrzActive = PowerPlanService.IsQrzActive();
        var qrzInstalled = qrzActive || PowerPlanService.IsQrzInstalled();
        var plan = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var planButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var planBtn = IconButton(qrzActive ? Glyphs.Check : Glyphs.Battery, qrzActive ? "Ativo" : qrzInstalled ? "Ativar" : "Instalar", primary: !qrzActive);
        planBtn.IsEnabled = !qrzActive;
        planBtn.Click += async (_, _) =>
        {
            if (GamingService.DetectX3d(GamingService.ProcessorName()) is { DualCcd: true } &&
                Msg("O plano Qrz desliga o estacionamento de núcleos. Em Ryzen X3D com dois CCDs isso pode fazer o jogo rodar no CCD sem 3D V-Cache. Ativar mesmo assim?", "Plano de energia Qrz", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            await ExecuteTrackedAsync("Ativando plano de energia Qrz", async _ => await Task.Run(() => new PowerPlanService(_log).InstallAndActivate()));
            if (_currentPage == "gaming") ShowGaming();
        };
        planButtons.Children.Add(planBtn);
        if (qrzInstalled)
        {
            var restore = IconButton(Glyphs.Undo, "Restaurar");
            restore.Margin = new Thickness(8, 0, 0, 0);
            restore.Click += async (_, _) =>
            {
                await ExecuteTrackedAsync("Restaurando plano de energia", async _ => await Task.Run(() => new PowerPlanService(_log).Restore()));
                if (_currentPage == "gaming") ShowGaming();
            };
            planButtons.Children.Add(restore);
        }
        DockPanel.SetDock(planButtons, Dock.Right); plan.Children.Add(planButtons);
        plan.Children.Add(BoostText("Plano de energia Qrz", qrzActive
            ? "Ativo. Plano de baixa latência: processador sempre em 100% e sem estacionamento de núcleos; PCIe, disco e Wi-Fi sem economia de energia."
            : "Plano de baixa latência: processador sempre em 100% e sem estacionamento de núcleos; PCIe, disco e Wi-Fi sem economia de energia. Restaurar volta ao plano anterior."));
        panel.Children.Add(plan);

        var standby = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var standbyBtn = IconButton(Glyphs.Memory, "Liberar");
        standbyBtn.Click += (_, _) => PurgeStandbyNow();
        DockPanel.SetDock(standbyBtn, Dock.Right); standby.Children.Add(standbyBtn);
        standby.Children.Add(BoostText("Memória em espera", "Esvazia o cache de arquivos na RAM, como o ISLC."));
        panel.Children.Add(standby);

        var applied = Gaming.IsSvchostReductionApplied;
        var svchost = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var svchostBtn = IconButton(applied ? Glyphs.Undo : Glyphs.Check, applied ? "Reverter" : "Aplicar", primary: !applied);
        svchostBtn.Click += (_, _) =>
        {
            try
            {
                if (applied) Gaming.RevertSvchostReduction(); else Gaming.ApplySvchostReduction();
                OperationStatus.Text = "Agrupamento de serviços alterado. Reinicie o PC para valer.";
                ShowGaming();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException) { Msg(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        DockPanel.SetDock(svchostBtn, Dock.Right); svchost.Children.Add(svchostBtn);
        svchost.Children.Add(BoostText("Menos processos svchost", applied ? "Aplicado. Agrupa os serviços em menos processos (vale após reiniciar)." : "Agrupa os serviços do Windows em menos processos. Vale após reiniciar."));
        panel.Children.Add(svchost);

        var gamer = new DockPanel();
        var gamerBtn = IconButton(Glyphs.ChevronRight, "Revisar");
        gamerBtn.Tag = "gamer"; gamerBtn.Click += RunOperation_Click;
        DockPanel.SetDock(gamerBtn, Dock.Right); gamer.Children.Add(gamerBtn);
        gamer.Children.Add(BoostText("Versão Avançada (jogos)", "Os ajustes permanentes de latência e desempenho, revisados item por item."));
        panel.Children.Add(gamer);
        return Surface(panel);
    }

    private StackPanel BoostText(string title, string detail)
    {
        var text = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var t = Label(title, 13); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0);
        var d = Label(detail, 11.5, true); d.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(t); text.Children.Add(d);
        return text;
    }

    private Border RedistributablesCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Runtimes para jogos", "Bibliotecas que muitos jogos exigem. Faltando uma delas, o jogo fecha ao abrir ou mostra erro de DLL (MSVCP140, d3dx9, XINPUT...)."));
        var checks = new List<(CheckBox Check, string Id)>();
        var grid = new UniformGrid { Columns = 1, Margin = new Thickness(0, 0, -10, 0) };
        foreach (var r in GamingService.Redistributables)
        {
            var check = new CheckBox { IsChecked = r.WingetId.StartsWith("Microsoft.VCRedist.2015", StringComparison.Ordinal) || r.WingetId == "Microsoft.DirectX" };
            var row = ChoiceRow(check, r.Title, r.Description, "winget", "Info");
            row.Margin = new Thickness(0, 0, 10, 10);
            grid.Children.Add(row);
            checks.Add((check, r.WingetId));
        }
        panel.Children.Add(grid);
        var install = IconButton(Glyphs.Download, "Instalar selecionados", primary: true);
        install.Click += async (_, _) =>
        {
            var ids = checks.Where(c => c.Check.IsChecked == true).Select(c => c.Id).ToList();
            if (ids.Count == 0) { OperationStatus.Text = "Selecione pelo menos um runtime."; return; }
            if (!GamingService.IsWingetAvailable())
            {
                Msg("O winget (Instalador de Aplicativos) não foi encontrado. Instale o \"Instalador de Aplicativos\" pela Microsoft Store e tente de novo.", "Runtimes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var progress = new Progress<string>(line => { OperationStatus.Text = line; _log.Write(line.StartsWith("[ERRO]") ? "ERROR" : "INFO", line); });
            await ExecuteTrackedAsync("Instalando runtimes", async token => await Gaming.InstallRedistributablesAsync(ids, progress, token));
        };
        var summary = Label("Já instalados são ignorados. Pode levar alguns minutos.", 12, true); summary.Margin = new Thickness(0); summary.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(ActionBar(summary, install));
        return Surface(panel);
    }
    #endregion
}
