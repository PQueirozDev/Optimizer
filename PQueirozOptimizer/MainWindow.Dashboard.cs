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
        PageActions.Children.Add(RoundIcon(Glyphs.Pin, "Modo compacto (segundo monitor)", OpenCompactMonitor));
        PageActions.Children.Add(RoundIcon(Glyphs.Refresh, "Ler o sistema e os indicadores de novo", RefreshHome));
        try
        {
            // As leituras mais pesadas (WMI) rodam em paralelo com a leitura do sistema, fora da tela
            // O Início só precisa da frequência base da CPU; discos, memória e rede detalhados ficam para o Meu PC
            var extras = Task.Run(HomeData.Cpu);
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
            // A frequência base só serve de reserva no medidor da CPU: um WMI lento na primeira abertura não segura o Início
            var cpuInfo = await Task.WhenAny(extras, Task.Delay(300)) == extras ? extras.Result : new CpuInfo(0, Environment.ProcessorCount, null);
            // O usuário pode ter trocado de página (ou reaberto o Início) durante a leitura
            if (_currentPage != "dashboard" || !ContentHost.Children.Contains(root)) return;
            root.Children.Clear();
            SetAdminState(snapshot.IsAdministrator);
            RefreshAlertsDot();

            _updateSlot = new StackPanel();
            root.Children.Add(_updateSlot);
            ShowUpdateBanner();
            if ((Application.Current as App)?.ActiveLicense is { IsExpiringSoon: true } expiring) root.Children.Add(LicenseRenewalBanner(expiring));
            if (PlanAccess.DemoMode && CurrentLicense is null) root.Children.Add(DemoBanner());
            if (NewLookBanner() is { } offer) root.Children.Add(offer);
            var notices = new StackPanel();
            root.Children.Add(notices);
            _ = FillDashboardNoticesAsync(notices);

            var prefs = AppearanceService.Current;
            var (score, headline, tone) = HealthScore(snapshot);

            // 1. Saúde (nota, indicadores e ação principal) e o HUD com os medidores ao vivo
            root.Children.Add(AuroraTopRow(snapshot, cpuInfo, score, headline, tone, prefs));
            // 2. Carga de CPU e GPU (1, 5 ou 15 min) e o acesso rápido
            if (prefs.DashboardLive || prefs.DashboardQuickActions) root.Children.Add(AuroraSecondRow(score, prefs));

            // Favoritos (Ctrl+K → estrela)
            if (FavoritesRow() is { } favorites) root.Children.Add(favorites);

            // 3. Este PC (Windows e discos) e o estado das otimizações
            if (prefs.DashboardStatus) root.Children.Add(AuroraStatusRow(snapshot));

            // 4. Atividade recente (só as últimas três linhas)
            if (prefs.DashboardActivity) root.Children.Add(RecentActivity());

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
        var root = new StackPanel();
        Border Bar(double width, double height, double top)
        {
            var bar = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(Math.Min(6, height / 2)), Margin = new Thickness(0, top, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            bar.SetResourceReference(Border.BackgroundProperty, "PanelHoverBrush");
            return bar;
        }
        var hero = new StackPanel();
        hero.Children.Add(Bar(90, 12, 0)); hero.Children.Add(Bar(360, 26, 12)); hero.Children.Add(Bar(260, 12, 12)); hero.Children.Add(Bar(220, 38, 26));
        var heroCard = HomeCard(hero, "HeroBrush");
        heroCard.Margin = new Thickness(0, 0, 0, AppearanceService.Space(16));
        root.Children.Add(heroCard);
        var grid = HomeGrid(200);
        for (var i = 0; i < 4; i++)
        {
            var card = new StackPanel();
            card.Children.Add(Bar(70, 10, 0)); card.Children.Add(Bar(80, 26, 12)); card.Children.Add(Bar(110, 10, 10)); card.Children.Add(Bar(160, 34, 18));
            grid.Children.Add(HomeCard(card));
        }
        root.Children.Add(grid);
        return root;
    }

    /// <summary>
    /// Convite único para o tema novo: quem tinha escolhido outro tema continua com ele até aceitar.
    /// A escolha fica guardada (não aparece de novo depois de aplicar ou dispensar).
    /// </summary>
    private Border? NewLookBanner()
    {
        // Identificador novo no visual Aurora: quem dispensou o convite de um tema anterior vê este uma vez
        const string offerId = "redesign-aurora";
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
        var sub = Label("O tema Aurora (preto com azul) foi feito para o novo design. Seu tema atual continua disponível em Configurações → Aparência.", 12, true);
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
