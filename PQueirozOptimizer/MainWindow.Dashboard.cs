using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PQueirozOptimizer.Services;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer;

/// <summary>Visão geral: pontuação de saúde, leitura do sistema, painel e avisos de licença.</summary>
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

    private async Task RenderDashboardAsync()
    {
        PageTitle.Text = "Visão geral";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        try
        {
            // Voltar ao painel reaproveita a última leitura
            var snapshot = FreshSnapshot;
            if (snapshot is null)
            {
                // A leitura costuma levar milissegundos: o aviso de carregamento só aparece se ela demorar
                var read = ReadSnapshotAsync();
                if (await Task.WhenAny(read, Task.Delay(150)) != read)
                {
                    var loading = new StackPanel();
                    loading.Children.Add(SectionHeader("Consultando seu computador...", "Lendo processador, memória, armazenamento e sistema."));
                    loading.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 5 });
                    root.Children.Add(Surface(loading));
                }
                snapshot = await read;
                StartupProfiler.Mark("snapshot-read");
                // O usuário pode ter trocado de página (ou reaberto o painel) durante a leitura
                if (_currentPage != "dashboard" || !ContentHost.Children.Contains(root)) return;
            }
            root.Children.Clear();
            TitleAdminChip.Text = snapshot.IsAdministrator ? "Administrador" : "Usuário";
            TitleAdminDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, snapshot.IsAdministrator ? "SuccessBrush" : "WarningBrush");

            _updateSlot = new StackPanel();
            root.Children.Add(_updateSlot);
            ShowUpdateBanner();
            if ((Application.Current as App)?.ActiveLicense is { IsExpiringSoon: true } expiring) root.Children.Add(LicenseRenewalBanner(expiring));

            // Destaque: pontuação de saúde + ações principais
            var (score, headline, tone) = HealthScore(snapshot);
            var hero = new Grid();
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            // 1. Estado geral: o status não depende só da cor (texto + ícone)
            var statusText = tone == "Success" ? "Saudável" : tone == "Warning" ? "Atenção" : "Crítico";
            var status = Pill((tone == "Success" ? "● " : "▲ ") + statusText, tone);
            status.HorizontalAlignment = HorizontalAlignment.Left; status.Margin = new Thickness(0, 0, 0, 10);
            heroText.Children.Add(status);
            var headlineText = new TextBlock { Text = headline, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            headlineText.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            heroText.Children.Add(headlineText);
            var facts = new WrapPanel { Margin = new Thickness(0, 10, 0, AppearanceService.Space(18)) };
            foreach (var (glyph, label, value) in new[] { (Glyphs.Monitor, "Sistema", $"{snapshot.OperatingSystem} · Build {snapshot.Build}"), (Glyphs.Clock, "Tempo ligado", snapshot.UptimeText) })
            {
                var fact = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 22, 4) };
                var icon = GlyphIcon(glyph, 12, "MutedBrush"); icon.Margin = new Thickness(0, 0, 7, 0);
                fact.Children.Add(icon);
                var l = new TextBlock { Text = label + ":", FontSize = 12.5, Margin = new Thickness(0, 0, 5, 0) }; l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                var v = new TextBlock { Text = value, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Tag = Translator.SystemDataTag }; v.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                fact.Children.Add(l); fact.Children.Add(v);
                facts.Children.Add(fact);
            }
            heroText.Children.Add(facts);
            // 2. Ação principal em destaque; as outras ficam como secundárias
            var actions = new WrapPanel();
            var clean = IconButton(Glyphs.Broom, "Analisar limpeza", primary: true); clean.Tag = "quickclean"; clean.Click += RunOperation_Click;
            clean.ToolTip = "Mostra quanto espaço dá para liberar antes de apagar qualquer coisa";
            var tune = IconButton(Glyphs.Lightning, "Revisar ajustes"); tune.Tag = "padrao"; tune.Click += RunOperation_Click;
            var diag = IconButton(Glyphs.Diagnostic, "Diagnóstico"); diag.Tag = "analisar"; diag.Click += RunOperation_Click;
            foreach (var b in new[] { clean, tune, diag }) { b.IsEnabled = !_operationRunning; actions.Children.Add(b); }
            heroText.Children.Add(actions);
            hero.Children.Add(heroText);
            var ring = ScoreRing(score, "SAÚDE", 112 * AppearanceService.CardScale); ring.Margin = new Thickness(24, 0, 4, 0);
            ring.ToolTip = $"Pontuação de saúde: {score:0} de 100";
            Grid.SetColumn(ring, 1); hero.Children.Add(ring);
            var heroCard = Surface(hero);
            heroCard.Padding = new Thickness(AppearanceService.Space(26), AppearanceService.Space(22), AppearanceService.Space(26), AppearanceService.Space(22));
            heroCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
            root.Children.Add(heroCard);
            // 3. Monitor em tempo real
            root.Children.Add(BuildLivePanel());

            // 4. Hardware
            var stats = Responsive(new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -14, 2) }, 210, 4);
            stats.Children.Add(Card("PROCESSADOR", snapshot.Processor, Glyphs.Chip, "AccentBrush"));
            stats.Children.Add(Card("MEMÓRIA", snapshot.Memory, Glyphs.Memory, "InfoBrush"));
            stats.Children.Add(Card("ESPAÇO LIVRE", $"{snapshot.FreeSpace} de {snapshot.Storage}", Glyphs.Drive, "SuccessBrush"));
            stats.Children.Add(Card("PLACA DE VÍDEO", snapshot.Graphics, Glyphs.Monitor, "WarningBrush"));
            root.Children.Add(stats);

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var health = BuildHealthPanel(snapshot);
            columns.Children.Add(health);

            var activityPanel = new StackPanel();
            var activityHead = new DockPanel();
            var seeAll = new Button { Content = "Ver tudo", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Top };
            seeAll.SetResourceReference(StyleProperty, "GhostButton");
            seeAll.Click += History_Click;
            DockPanel.SetDock(seeAll, Dock.Right); activityHead.Children.Add(seeAll);
            activityHead.Children.Add(SectionHeader("Atividade recente"));
            activityPanel.Children.Add(activityHead);
            foreach (var line in _activity.TakeLast(5).Reverse())
            {
                var isError = line.Contains("[ERROR]"); var isSuccess = line.Contains("[SUCCESS]");
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 5, 10, 0), VerticalAlignment = VerticalAlignment.Top };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, isError ? "DangerBrush" : isSuccess ? "SuccessBrush" : "AccentBrush");
                row.Children.Add(dot);
                var text = Label(line, 11.5, true); text.Margin = new Thickness(0); text.TextTrimming = TextTrimming.CharacterEllipsis; text.TextWrapping = TextWrapping.NoWrap;
                text.ToolTip = line;
                row.Children.Add(text);
                activityPanel.Children.Add(row);
            }
            if (_activity.Count == 0) activityPanel.Children.Add(Label("Suas próximas execuções aparecerão aqui.", 13, true));
            var activityCard = Surface(activityPanel);
            Grid.SetColumn(activityCard, 2);
            columns.Children.Add(activityCard);
            root.Children.Add(columns);
            // 6. Recursos secundários: o passo a passo completo
            root.Children.Add(FixAllCard());

            var profile = _configService.GetActiveProfile();
            var profileLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 8) };
            profileLine.Children.Add(GlyphIcon(Glyphs.Settings, 12, "MutedBrush"));
            var pl = Label($"Perfil selecionado: {profile.Name}. Você sempre revisa os ajustes antes de aplicar.", 12, true); pl.Margin = new Thickness(8, 0, 0, 0);
            profileLine.Children.Add(pl);
            root.Children.Add(profileLine);
            AnimatePageIn();
        }
        catch (Exception ex)
        {
            root.Children.Clear();
            root.Children.Add(Card("NÃO FOI POSSÍVEL CONSULTAR O SISTEMA", ex.Message, Glyphs.Warning, "WarningBrush"));
            _log.Write("ERROR", ex.Message);
        }
    }

    private Border BuildHealthPanel(SystemSnapshot snapshot)
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Leitura rápida", "Indicadores que mais influenciam o desempenho do dia a dia."));
        var free = snapshot.FreePercent;
        AddMeter(panel, Glyphs.Drive, "Espaço livre no disco", free, $"{free:N0}% livre", free >= 20 ? "Success" : free >= 10 ? "Warning" : "Danger");
        var hours = snapshot.Uptime.TotalHours;
        AddMeter(panel, Glyphs.Clock, "Tempo desde o último reinício", Math.Min(100, hours / 168d * 100), hours < 72 ? "Recente" : "Reinicie em breve", hours < 72 ? "Success" : "Warning");
        var memory = snapshot.MemoryGb;
        AddMeter(panel, Glyphs.Memory, "Memória instalada", Math.Min(100, memory / 32d * 100), memory >= 16 ? "Ideal para jogos" : memory >= 8 ? "Suficiente" : "Limitada", memory >= 16 ? "Success" : memory >= 8 ? "Warning" : "Danger");
        panel.Children.Add(Label("Acesso rápido", 12, true));
        AddHealthLink(panel, "Drivers", Glyphs.Monitor, "drivers", "Verificar dispositivos e drivers");
        AddHealthLink(panel, "Serviços", Glyphs.Services, "services", "Abrir grupos de serviços");
        AddHealthLink(panel, "Windows Update", Glyphs.Refresh, "fixes", "Reparar o Windows Update", "windows-update");
        AddHealthLink(panel, "Bluetooth", Glyphs.Bluetooth, "fixes", "Reparar o Bluetooth", "bluetooth");
        return Surface(panel);
    }

    private void AddHealthLink(Panel panel, string title, string glyph, string page, string hint, string? fixId = null)
    {
        var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(7), Margin = new Thickness(0, 3, 0, 0), ToolTip = hint };
        button.SetResourceReference(StyleProperty, "GhostButton");
        var row = new DockPanel();
        row.Children.Add(IconChip(glyph, "Info", 26));
        var text = Label(title, 11.5); text.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(text);
        var arrow = Label("›", 18, true); DockPanel.SetDock(arrow, Dock.Right); row.Children.Add(arrow);
        button.Content = row;
        button.Click += (_, _) => { if (fixId != null) OpenFixById(fixId); else NavigateTo(page); };
        panel.Children.Add(button);
    }

    private void AddMeter(Panel panel, string glyph, string title, double value, string status, string tone)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(IconChip(glyph, tone, 34));
        var body = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel();
        var s = new TextBlock { Text = status, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        s.SetResourceReference(TextBlock.ForegroundProperty, tone + "Brush");
        DockPanel.SetDock(s, Dock.Right); head.Children.Add(s);
        var t = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.Medium };
        head.Children.Add(t);
        body.Children.Add(head);
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(value, 0, 100), Height = 6, Margin = new Thickness(0, 7, 0, 0), IsIndeterminate = false };
        body.Children.Add(bar);
        Grid.SetColumn(body, 1); row.Children.Add(body);
        panel.Children.Add(row);
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
