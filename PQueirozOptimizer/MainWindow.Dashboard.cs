using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PQueirozOptimizer.Controls;
using PQueirozOptimizer.Engine;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Início: visão geral do desempenho e da saúde do PC, no layout da referência — cards de processador, memória
/// e placa de vídeo com número grande e atividade ao vivo, o card Meu PC com os indicadores, armazenamento,
/// rede, atividade, acesso rápido, configurações rápidas e os jogos. Avisos de licença e atualização no topo.
/// </summary>
public partial class MainWindow
{
    /// <summary>Pontuação simples de 0 a 100 a partir do espaço livre e do tempo ligado.</summary>
    private static (int Score, string Headline, string Tone) HealthScore(SystemSnapshot snapshot)
    {
        var score = 100.0;
        var free = snapshot.FreePercent;
        if (snapshot.StorageGb > 0) { if (free < 10) score -= 35; else if (free < 20) score -= 20; else if (free < 30) score -= 8; }
        var hours = snapshot.Uptime.TotalHours;
        if (hours > 24 * 7) score -= 20; else if (hours > 24 * 3) score -= 10;
        if (!snapshot.IsAdministrator) score -= 5;
        var value = (int)Math.Round(Math.Clamp(score, 0, 100));
        return value switch
        {
            >= 85 => (value, "Seu PC está em ótima forma", "Success"),
            >= 65 => (value, "Seu PC está bem, com alguns pontos de atenção", "Warning"),
            _ => (value, "Seu PC precisa de atenção", "Danger"),
        };
    }

    private Task<SystemSnapshot>? _snapshotRead;
    private DateTime _snapshotReadAt;
    private SystemSnapshot? FreshSnapshot => _snapshot is { } s && DateTime.Now - _snapshotReadAt < TimeSpan.FromMinutes(5) ? s : null;

    /// <summary>Lê o sistema e reaproveita a leitura por alguns minutos; cada operação concluída a descarta.</summary>
    private async Task<SystemSnapshot> ReadSnapshotAsync()
    {
        if (FreshSnapshot is { } cached) return cached;
        _snapshotRead ??= Task.Run(_system.Read);
        try { _snapshot = await _snapshotRead; _snapshotReadAt = DateTime.Now; return _snapshot; }
        finally { _snapshotRead = null; }
    }

    /// <summary>Botão Atualizar do Início: lê tudo de novo (sistema, unidades, indicadores).</summary>
    private void RefreshHome()
    {
        HomeData.Invalidate();
        _snapshotReadAt = default;
        _ = RenderDashboardAsync();
    }

    /// <summary>Grade responsiva do Início: 4 colunas em telas largas, 2 em médias, 1 em estreitas.</summary>
    private static QrzGrid HomeGrid(double minColumn = 290) => new()
    {
        MinColumnWidth = minColumn * AppearanceService.CardScale, MaxColumns = 4, AllowedColumns = new[] { 1, 2, 4 },
        ColumnGap = AppearanceService.Space(16), RowGap = AppearanceService.Space(16), Margin = new Thickness(0, 0, 0, AppearanceService.Space(16)),
    };

    private static T Span<T>(T element, int span) where T : UIElement { QrzGrid.SetSpan(element, span); return element; }

    private async Task RenderDashboardAsync()
    {
        PageTitle.Text = "Início";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        _homeLive.Clear();
        _homeRoot = root;
        PageActions.Children.Clear();
        AddLiveBadge();
        AddPageAction(Glyphs.Refresh, "Atualizar", RefreshHome).ToolTip = Tr("Ler o sistema, as unidades e os indicadores de novo");
        try
        {
            // As leituras mais pesadas (WMI) rodam em paralelo com a leitura do sistema, fora da tela
            var extras = Task.Run(() => (Cpu: HomeData.Cpu(), Memory: HomeData.Memory(), Drives: HomeData.Drives(), Network: HomeData.Network()));
            var snapshot = FreshSnapshot;
            if (snapshot is null || !extras.IsCompleted)
            {
                var read = snapshot is null ? ReadSnapshotAsync() : Task.FromResult(snapshot);
                // A leitura costuma ser rápida: o esqueleto só aparece se ela demorar
                var both = Task.WhenAll(read, extras);
                if (await Task.WhenAny(both, Task.Delay(150)) != both) root.Children.Add(HomeSkeleton());
                snapshot = await read;
                StartupProfiler.Mark("snapshot-read");
            }
            var data = await extras;
            // O usuário pode ter trocado de página (ou reaberto o Início) durante a leitura
            if (_currentPage != "dashboard" || !ContentHost.Children.Contains(root)) return;
            root.Children.Clear();
            TitleAdminChip.Text = snapshot.IsAdministrator ? "Administrador" : "Usuário";
            TitleAdminDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, snapshot.IsAdministrator ? "SuccessBrush" : "WarningBrush");
            // No Início, a linha acima do título é o nome do computador (como "PC Name" da referência)
            PageEyebrow.Text = Environment.MachineName;
            PageEyebrow.Tag = Translator.SystemDataTag;
            RefreshAlertsDot();

            _updateSlot = new StackPanel();
            root.Children.Add(_updateSlot);
            ShowUpdateBanner();
            if ((Application.Current as App)?.ActiveLicense is { IsExpiringSoon: true } expiring) root.Children.Add(LicenseRenewalBanner(expiring));
            if (PlanAccess.DemoMode && CurrentLicense is null) root.Children.Add(DemoBanner());
            if (NewLookBanner() is { } offer) root.Children.Add(offer);

            var prefs = AppearanceService.Current;
            var (score, _, tone) = HealthScore(snapshot);
            if (prefs.DashboardStatus) root.Children.Add(RecommendationLine(snapshot));

            // 1. Hardware ao vivo + Meu PC
            var top = HomeGrid();
            if (prefs.DashboardHardware)
            {
                top.Children.Add(CpuCard(snapshot, data.Cpu, prefs.DashboardLive));
                top.Children.Add(MemoryCard(snapshot, data.Memory, prefs.DashboardLive));
                top.Children.Add(GpuCard(snapshot, prefs.DashboardLive));
            }
            top.Children.Add(MyPcCard(snapshot, score, tone));
            root.Children.Add(top);

            // 2. Armazenamento, rede e atividade
            if (prefs.DashboardHardware || prefs.DashboardActivity)
            {
                var middle = HomeGrid();
                if (prefs.DashboardHardware)
                {
                    middle.Children.Add(StorageCard(data.Drives));
                    middle.Children.Add(Span(NetworkCard(data.Network, prefs.DashboardLive), 2));
                }
                if (prefs.DashboardActivity) middle.Children.Add(ActivityCard());
                root.Children.Add(middle);
            }

            // 3. Acesso rápido
            if (prefs.DashboardQuickActions)
            {
                root.Children.Add(HomeSectionTitle("Acesso rápido", "Os recursos mais usados, com o estado de cada um."));
                var quick = HomeGrid(240);
                var lastTest = PerfLabStore.Default.List().FirstOrDefault();
                quick.Children.Add(QuickCard(Tr("Analisador de desempenho"), Tr("Ferramenta de análise de FPS"),
                    lastTest is null ? "Muted" : "Success", lastTest is null ? Tr("Nenhum teste ainda") : $"{Tr("Último teste")}: {lastTest.StartedAt:dd/MM HH:mm}",
                    Tr("Analisar"), ArtKind.DotWave, () => NavigateTo("perflab")));
                var analysis = SmartHistory.Default.LastAnalysis;
                quick.Children.Add(QuickCard(Tr("Otimização inteligente"), Tr("Ajustes recomendados em um clique"),
                    analysis is null ? "Muted" : "Success", analysis is null ? Tr("Ainda não analisado") : string.Format(Tr("{0} ajustes aplicados"), analysis.Applied),
                    Tr("Otimizar"), ArtKind.Waves, () => NavigateTo("smart")));
                quick.Children.Add(QuickCard(Tr("Central de saúde"), Tr("Diagnóstico com evidências"),
                    tone, string.Format(Tr("Saúde {0} de 100"), score), Tr("Diagnosticar"), ArtKind.Rings, () => NavigateTo("diagnostics")));
                var session = GamingService.ActiveSession();
                quick.Children.Add(QuickCard(Tr("Central de jogos"), Tr("Modo Jogo, perfis e presets"),
                    session is null ? "Muted" : session.RecoveryPending ? "Warning" : "Success",
                    session is null ? Tr("Modo Jogo desligado") : session.RecoveryPending ? Tr("Restauração pendente") : Tr("Modo Jogo ativo"),
                    Tr("Configurar"), ArtKind.Bars, () => NavigateTo("gaming")));
                root.Children.Add(quick);
            }

            // 4. Configurações rápidas e jogos
            if (prefs.DashboardActivity)
            {
                var bottom = HomeGrid();
                bottom.Children.Add(Span(QuickSettingsCard(), 2));
                bottom.Children.Add(Span(GamesCard(), 2));
                root.Children.Add(bottom);
            }

            // 5. O passo a passo completo continua disponível
            root.Children.Add(FixAllCard());
            var profile = _configService.GetActiveProfile();
            var profileLine = new Grid { Margin = new Thickness(4, 0, 0, 8) };
            profileLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            profileLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            profileLine.Children.Add(GlyphIcon(Glyphs.Settings, 12, "MutedBrush"));
            var pl = Label($"Perfil selecionado: {ProfileDisplayName(profile.Name)}. Você sempre revisa os ajustes antes de aplicar.", 12, true); pl.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(pl, 1);
            profileLine.Children.Add(pl);
            root.Children.Add(profileLine);

            // Primeira leitura já desenhada (o monitor atualiza a cada segundo depois)
            if (HardwareMonitorService.Shared.Latest is { } latest) foreach (var update in _homeLive) update(latest);
            AnimatePageIn();
        }
        catch (Exception ex)
        {
            root.Children.Clear();
            root.Children.Add(Card("NÃO FOI POSSÍVEL CONSULTAR O SISTEMA", ex.Message, Glyphs.Warning, "WarningBrush"));
            _log.Write("ERROR", ex.Message);
        }
    }

    private StackPanel HomeSectionTitle(string title, string subtitle)
    {
        var header = SectionHeader(title, subtitle);
        header.Margin = new Thickness(0, AppearanceService.Space(8), 0, AppearanceService.Space(14));
        return header;
    }

    /// <summary>Esqueleto estático enquanto o sistema é lido (sem brilho animado: nada fica redesenhando).</summary>
    private UIElement HomeSkeleton()
    {
        var grid = HomeGrid();
        for (var i = 0; i < 4; i++)
        {
            var stack = new StackPanel();
            foreach (var (width, height, top) in new[] { (0.45, 12.0, 0.0), (0.7, 14.0, 8.0), (0.35, 30.0, 26.0), (1.0, 8.0, 22.0), (0.9, 8.0, 12.0), (0.8, 8.0, 12.0), (1.0, 64.0, 22.0) })
            {
                var bar = new Border { Height = height, CornerRadius = new CornerRadius(Math.Min(6, height / 2)), Margin = new Thickness(0, top, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                bar.SetResourceReference(Border.BackgroundProperty, "PanelHoverBrush");
                stack.Children.Add(bar);
                var fraction = width;
                stack.SizeChanged += (_, e) => bar.Width = Math.Max(0, e.NewSize.Width * fraction);
            }
            grid.Children.Add(HomeCard(stack));
        }
        return grid;
    }

    /// <summary>
    /// Linha de recomendações acima dos cards (como "Recommended tweaks" da referência): quantos ajustes a última
    /// análise recomendou e os alertas atuais, cada um levando à página certa.
    /// </summary>
    private UIElement RecommendationLine(SystemSnapshot snapshot)
    {
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, AppearanceService.Space(16)) };
        var alerts = CommandAlerts(snapshot);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (text, alertTone, page) in alerts.Take(2))
        {
            var pill = Pill("● " + Tr(text), alertTone);
            ((TextBlock)pill.Child).Tag = Translator.SystemDataTag;
            pill.Margin = new Thickness(8, 0, 0, 0);
            if (page is { } target && target != "dashboard") { pill.Cursor = Cursors.Hand; pill.MouseLeftButtonUp += (_, _) => NavigateTo(target); }
            right.Children.Add(pill);
        }
        if (alerts.Count > 2)
        {
            var more = Pill($"+{alerts.Count - 2}", "Info"); more.Margin = new Thickness(8, 0, 0, 0); more.Cursor = Cursors.Hand;
            more.MouseLeftButtonUp += (_, _) => AlertsButton_Click(AlertsButton, new RoutedEventArgs());
            right.Children.Add(more);
        }
        DockPanel.SetDock(right, Dock.Right); line.Children.Add(right);

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var analysis = SmartHistory.Default.LastAnalysis;
        var text1 = new TextBlock { Text = analysis is null ? Tr("Analise o PC para receber recomendações") : Tr("Recomendações da última análise"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        text1.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        left.Children.Add(text1);
        var info = GlyphIcon(Glyphs.Info, 12, "MutedBrush"); info.Margin = new Thickness(7, 1, 9, 0);
        info.ToolTip = analysis is null ? Tr("A Otimização inteligente lê o hardware e o estado de cada ajuste e recomenda só o que se aplica.")
            : string.Format(Tr("Análise de {0:dd/MM HH:mm} para o objetivo {1}."), analysis.At, Tr(GoalTitle(analysis.Goal)));
        left.Children.Add(info);
        var count = Pill(analysis is null ? Tr("Analisar") : string.Format(Tr("{0} ajustes"), analysis.Recommended), "Accent");
        ((TextBlock)count.Child).Tag = Translator.SystemDataTag;
        count.Cursor = Cursors.Hand;
        count.ToolTip = Tr("Abrir a Otimização inteligente");
        count.MouseLeftButtonUp += (_, _) => NavigateTo("smart");
        left.Children.Add(count);
        line.Children.Add(left);
        return line;
    }

    /// <summary>
    /// Convite único para o tema novo: quem tinha escolhido outro tema continua com ele até aceitar.
    /// A escolha fica guardada (não aparece de novo depois de aplicar ou dispensar).
    /// </summary>
    private Border? NewLookBanner()
    {
        const string offerId = "redesign-tema-azul";
        if (AppearanceService.Current.Theme == ThemeMode.Dark || _configService.IsTutorialCompleted(offerId)) return null;
        var dock = new DockPanel();
        Border? card = null;
        void Done()
        {
            try { _configService.SetTutorialCompleted(offerId, true); }
            catch (System.IO.IOException ex) { _log.Write("WARN", "Preferências não foram salvas: " + ex.Message); }
        }
        var apply = IconButton(Glyphs.Check, "Aplicar o tema", primary: true);
        apply.Margin = new Thickness(12, 0, 0, 0);
        apply.Click += (_, _) => { Done(); var next = AppearanceService.Current.Clone(); next.Theme = ThemeMode.Dark; SaveAppearance(next); };
        var later = new Button { Content = "Agora não", Margin = new Thickness(8, 0, 0, 0) };
        later.SetResourceReference(StyleProperty, "GhostButton");
        later.Click += (_, _) => { Done(); if (card?.Parent is Panel parent) parent.Children.Remove(card); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { later, apply } };
        DockPanel.SetDock(buttons, Dock.Right); dock.Children.Add(buttons);
        var chip = IconChip(Glyphs.Palette, "Accent", 40); DockPanel.SetDock(chip, Dock.Left); dock.Children.Add(chip);
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = Label("Novo visual do Qrztweaks", 14); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0);
        text.Children.Add(title);
        var sub = Label("O tema Azul meia-noite (preto, branco e azul) foi feito para o novo design. Seu tema atual continua disponível em Configurações → Aparência.", 12, true);
        sub.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(sub);
        dock.Children.Add(text);
        card = Surface(dock);
        card.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        return card;
    }

    /// <summary>Atalho de reparo (usado no Meu PC): ícone, nome e seta, levando à página ou correção.</summary>
    private void AddHealthLink(Panel panel, string title, string glyph, string page, string hint, string? fixId = null)
    {
        var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8), Margin = new Thickness(0, 3, 0, 0), ToolTip = hint };
        button.SetResourceReference(StyleProperty, "GhostButton");
        var row = new DockPanel();
        row.Children.Add(IconChip(glyph, "Accent", 28));
        var text = Label(title, 12.5); text.Margin = new Thickness(10, 0, 0, 0); text.VerticalAlignment = VerticalAlignment.Center;
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var arrow = GlyphIcon(Glyphs.ChevronRight, 10, "MutedBrush"); DockPanel.SetDock(arrow, Dock.Right);
        row.Children.Add(arrow); row.Children.Add(text);
        button.Content = row;
        button.Click += (_, _) => { if (fixId != null) OpenFixById(fixId); else NavigateTo(page); };
        panel.Children.Add(button);
    }

    /// <summary>
    /// A renovação é feita por ticket no Discord, com pagamento via Pix: copia o pedido (titular + ID)
    /// para o cliente colar no ticket e abre o servidor.
    /// </summary>
    private void RenewViaDiscord(LicenseInfo license)
    {
        try { Clipboard.SetText(new LicenseService().BuildActivationRequest(license.Licensee)); }
        catch (System.Runtime.InteropServices.COMException) { OperationStatus.Text = "A área de transferência está ocupada. Tente novamente."; return; }
        OperationStatus.Text = "Pedido de renovação copiado. No Discord, abra um ticket de renovação, cole o pedido e pague via Pix.";
        OpenUrl(LicensePlans.DiscordUrl);
    }

    /// <summary>Aviso de vencimento próximo: o cliente já copia o pedido de renovação e ativa a chave nova daqui.</summary>
    private Border LicenseRenewalBanner(LicenseInfo license)
    {
        var copy = IconButton(Glyphs.OpenInNew, "Renovar pelo Discord", primary: true);
        copy.Click += (_, _) => RenewViaDiscord(license);
        var activate = IconButton(Glyphs.Key, "Ativar nova chave");
        activate.Click += (_, _) => ActivateAdminLicense();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Children = { activate, copy } };

        var banner = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right); banner.Children.Add(buttons);
        var chip = IconChip(Glyphs.Warning, "Warning", 40); DockPanel.SetDock(chip, Dock.Left); banner.Children.Add(chip);
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = Label(license.DaysLeft switch { 0 => "Sua licença vence hoje", 1 => "Sua licença vence amanhã", var d => $"Sua licença vence em {d} dias" }, 14);
        title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0);
        text.Children.Add(title);
        var sub = Label($"A chave atual vale até {license.ExpiresAtUtc!.Value.ToLocalTime():dd/MM/yyyy}. Para renovar, abra um ticket de renovação no Discord e pague via Pix.", 12, true);
        sub.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(sub);
        banner.Children.Add(text);
        var card = Surface(banner); card.Padding = new Thickness(18, 14, 18, 14);
        card.SetResourceReference(Border.BorderBrushProperty, "WarningBrush");
        return card;
    }
}
