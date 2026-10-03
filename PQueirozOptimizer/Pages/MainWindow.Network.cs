using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;
public partial class MainWindow
{
    private NetworkService? _network;
    private NetworkService Network => _network ??= new NetworkService(_log);
    private SpeedResult? _lastSpeed;
    private Dictionary<string, double?>? _dnsTimes;

    #region Network Page
    private void ShowNetwork()
    {
        PageTitle.Text = "Rede";
        PageBadge.Visibility = Visibility.Visible;
        PageBadgeText.Text = "DNS: " + NetworkService.CurrentDnsLabel();
        var root = new StackPanel();
        root.Children.Add(Mark(SpeedTestCard(), "network.speed"));

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.Children.Add(Mark(DnsCard(), "network.dns"));
        var right = new StackPanel();
        right.Children.Add(LatencyTweakCard());
        right.Children.Add(AdaptersCard());
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);
        root.Children.Add(columns);

        root.Children.Add(Mark(NetworkRepairCard(), "network.repair"));
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private Border SpeedTestCard()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var pill = Pill(_lastSpeed is null ? "Teste de velocidade" : "Último resultado", "Accent"); pill.HorizontalAlignment = HorizontalAlignment.Left; pill.Margin = new Thickness(0, 0, 0, 10);
        left.Children.Add(pill);
        var title = new TextBlock { Text = _lastSpeed is null ? "Quão rápida e estável é a sua conexão?" : SpeedVerdict(_lastSpeed), FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        left.Children.Add(title);
        var sub = Label("Mede download, upload, ping, jitter e perda de pacotes nos servidores da Cloudflare. Leva cerca de 25 segundos.", 12.5, true);
        sub.Margin = new Thickness(0, 6, 0, 16);
        left.Children.Add(sub);
        var run = IconButton(Glyphs.Play, _lastSpeed is null ? "Iniciar teste" : "Testar de novo", primary: true);
        run.HorizontalAlignment = HorizontalAlignment.Left;
        run.Click += async (_, _) =>
        {
            var progress = new Progress<string>(line => OperationStatus.Text = line);
            await ExecuteTrackedAsync("Teste de velocidade", async token =>
            {
                try
                {
                    _lastSpeed = await NetworkService.RunSpeedTestAsync(progress, token);
                    _log.Write("SUCCESS", $"Teste de velocidade: ↓ {_lastSpeed.DownloadMbps:0.0} Mbps · ↑ {_lastSpeed.UploadMbps:0.0} Mbps · ping {_lastSpeed.Latency.AverageMs:0} ms");
                }
                catch (HttpRequestException ex) { throw new InvalidOperationException("Teste de velocidade falhou: " + ex.Message); }
            });
            if (_currentPage == "network") ShowNetwork();
        };
        left.Children.Add(run);
        grid.Children.Add(left);

        var results = new UniformGrid { Columns = 3, Rows = 2, Margin = new Thickness(24, 0, 0, 0), MinWidth = 420 };
        var s = _lastSpeed;
        results.Children.Add(SpeedStat("DOWNLOAD", s is null ? "--" : $"{s.DownloadMbps:0.0}", "Mbps", "Accent"));
        results.Children.Add(SpeedStat("UPLOAD", s is null ? "--" : $"{s.UploadMbps:0.0}", "Mbps", "Info"));
        results.Children.Add(SpeedStat("PING", s?.Latency.AverageMs is { } ping ? $"{ping:0}" : "--", "ms", PingTone(s?.Latency.AverageMs)));
        results.Children.Add(SpeedStat("JITTER", s?.Latency.JitterMs is { } jitter ? $"{jitter:0.0}" : "--", "ms", s?.Latency.JitterMs is > 15 ? "Warning" : "Success"));
        results.Children.Add(SpeedStat("PERDA", s is null ? "--" : $"{s.Latency.LossPercent:0}", "%", s?.Latency.LossPercent > 0 ? "Danger" : "Success"));
        results.Children.Add(SpeedStat("MÍN / MÁX", s?.Latency.MinMs is { } min && s.Latency.MaxMs is { } max ? $"{min:0}/{max:0}" : "--", "ms", "Accent"));
        Grid.SetColumn(results, 1);
        grid.Children.Add(results);

        var card = Surface(grid);
        card.Padding = new Thickness(28, 24, 28, 24);
        card.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "AccentSoftBrush");
        return card;
    }

    private static string PingTone(double? ping) => ping switch { null => "Accent", < 40 => "Success", < 90 => "Warning", _ => "Danger" };

    private static string SpeedVerdict(SpeedResult s)
    {
        if (s.Latency.LossPercent >= 5) return "Conexão instável: há perda de pacotes";
        if (s.Latency.JitterMs > 20) return "Ping oscilando bastante (jitter alto)";
        if (s.Latency.AverageMs > 90) return "Ping alto para jogos online";
        return s.DownloadMbps >= 50 ? "Conexão ótima para jogar e transmitir" : "Conexão boa para jogar";
    }

    private Border SpeedStat(string label, string value, string unit, string tone)
    {
        var stack = new StackPanel();
        var l = new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.SemiBold };
        l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(l);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var v = new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeights.Bold, Tag = Translator.SystemDataTag };
        v.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        v.SetResourceReference(TextBlock.ForegroundProperty, tone + "Brush");
        var u = new TextBlock { Text = unit, FontSize = 12, Margin = new Thickness(5, 0, 0, 5), VerticalAlignment = VerticalAlignment.Bottom, Tag = Translator.SystemDataTag };
        u.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        line.Children.Add(v); line.Children.Add(u);
        stack.Children.Add(line);
        var cell = new Border { Child = stack, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        cell.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        cell.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return cell;
    }

    private Border DnsCard()
    {
        var panel = new StackPanel();
        var head = new DockPanel();
        var bench = IconButton(Glyphs.Speed, "Medir DNS");
        bench.VerticalAlignment = VerticalAlignment.Top;
        bench.Click += async (_, _) =>
        {
            await ExecuteTrackedAsync("Medindo servidores DNS", async token => _dnsTimes = await NetworkService.BenchmarkDnsAsync(token));
            if (_currentPage == "network") ShowNetwork();
        };
        DockPanel.SetDock(bench, Dock.Right); head.Children.Add(bench);
        head.Children.Add(SectionHeader("Servidor DNS", "Traduz nomes de sites e servidores de jogos. Um DNS rápido acelera conexões e o login nos jogos."));
        panel.Children.Add(head);
        var current = NetworkService.CurrentDnsLabel();
        var fastest = _dnsTimes?.Where(t => t.Value.HasValue).OrderBy(t => t.Value).Select(t => t.Key).FirstOrDefault();
        foreach (var provider in NetworkService.DnsProviders)
        {
            var isCurrent = provider.Name == current;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var use = IconButton(isCurrent ? Glyphs.Check : Glyphs.ChevronRight, isCurrent ? "Em uso" : "Usar", primary: false);
            use.IsEnabled = !isCurrent;
            use.Click += async (_, _) =>
            {
                await ExecuteTrackedAsync("Alterando DNS para " + provider.Name, async _ => await Network.SetDnsAsync(provider));
                if (_currentPage == "network") ShowNetwork();
            };
            DockPanel.SetDock(use, Dock.Right); row.Children.Add(use);
            if (_dnsTimes != null && _dnsTimes.TryGetValue(provider.Id, out var ms))
            {
                var time = Pill(ms is { } m ? $"{m:0} ms" : "sem resposta", provider.Id == fastest ? "Success" : "Info");
                time.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(time, Dock.Right); row.Children.Add(time);
            }
            var chip = IconChip(provider.Id == "auto" ? Glyphs.Refresh : Glyphs.Globe, isCurrent ? "Success" : "Accent", 34);
            DockPanel.SetDock(chip, Dock.Left); row.Children.Add(chip);
            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = Label(provider.Name, 13); name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0);
            text.Children.Add(name);
            var detail = Label(provider.Servers.Length > 0 ? $"{string.Join(" · ", provider.Servers)} — {provider.Description}" : provider.Description, 11.5, true); detail.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(detail);
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        return Surface(panel);
    }

    private Border LatencyTweakCard()
    {
        var applied = Network.IsLatencyTweakApplied;
        var panel = new DockPanel();
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var toggle = IconButton(applied ? Glyphs.Undo : Glyphs.Check, applied ? "Reverter" : "Aplicar ajustes", primary: !applied);
        toggle.Click += (_, _) =>
        {
            try
            {
                if (applied) Network.RevertLatencyTweak(); else Network.ApplyLatencyTweak();
                OperationStatus.Text = applied ? "Ajustes de latência revertidos." : "Ajustes de latência aplicados. Reconecte a rede ou reinicie para valer em todos os programas.";
                ShowNetwork();
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { Msg(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        buttons.Children.Add(toggle);
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(FeatureHeader(Glyphs.Lightning, "Accent", "Latência de rede para jogos",
            "Desliga o algoritmo de Nagle e o ACK atrasado nos adaptadores conectados: pacotes pequenos saem na hora, sem esperar para serem agrupados. Não altera a velocidade da internet.",
            applied ? "Aplicado" : "Padrão do Windows", applied ? "Success" : "Accent"));
        return Surface(panel);
    }

    private Border AdaptersCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Conexões ativas"));
        var adapters = NetworkService.ActiveAdapters();
        if (adapters.Count == 0) panel.Children.Add(Label("Nenhuma conexão com a internet encontrada.", 12.5, true));
        foreach (var a in adapters)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var speed = Pill(a.Speed, "Info"); DockPanel.SetDock(speed, Dock.Right); row.Children.Add(speed);
            var chip = IconChip(a.Type == "Wi-Fi" ? Glyphs.Network : Glyphs.Globe, "Accent", 32); DockPanel.SetDock(chip, Dock.Left); row.Children.Add(chip);
            var text = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = Label($"{a.Name} · {a.Type}", 12.5); name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0);
            text.Children.Add(name);
            var desc = Label(a.Description, 11, true); desc.Margin = new Thickness(0); desc.Tag = Translator.SystemDataTag; desc.TextTrimming = TextTrimming.CharacterEllipsis; desc.TextWrapping = TextWrapping.NoWrap;
            text.Children.Add(desc);
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        if (adapters.Any(a => a.Type == "Wi-Fi"))
        {
            var tip = Label("Dica: para jogar, o cabo de rede tem ping mais baixo e estável que o Wi-Fi.", 11.5, true);
            tip.Margin = new Thickness(0, 4, 0, 0);
            panel.Children.Add(tip);
        }
        return Surface(panel);
    }

    private Border NetworkRepairCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Reparos de rede", "Resolvem a maioria dos problemas de conexão, DNS e login em jogos."));
        var grid = Responsive(new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -12, 0) }, 260, 3);

        var flush = RepairTile(Glyphs.Broom, "Limpar cache DNS", "Resolve sites e servidores que não abrem depois de mudarem de endereço.", "Limpar");
        ((Button)flush.Tag).Click += async (_, _) => await ExecuteTrackedAsync("Limpando cache DNS", async _ => await Network.FlushDnsAsync());
        var time = RepairTile(Glyphs.Clock, "Sincronizar relógio", "Relógio errado causa falha de login, de certificado e de anti-cheat em jogos.", "Sincronizar");
        ((Button)time.Tag).Click += async (_, _) => await ExecuteTrackedAsync("Sincronizando relógio", async _ =>
        {
            if (!await Network.SyncTimeAsync()) throw new InvalidOperationException("Não foi possível sincronizar o relógio agora. Verifique a conexão.");
        });
        var reset = RepairTile(Glyphs.Refresh, "Redefinir rede", "Reinicia Winsock e TCP/IP. Use quando nada mais funcionar; precisa reiniciar o PC.", "Redefinir");
        ((Button)reset.Tag).Click += async (_, _) =>
        {
            if (Msg("Redefinir a rede? Configurações de IP fixo e de VPN podem precisar ser refeitas, e o PC precisa ser reiniciado em seguida.", "Redefinir rede", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var progress = new Progress<string>(line => { OperationStatus.Text = line; _log.Write(line.StartsWith("[ERRO]") ? "ERROR" : "INFO", line); });
            await ExecuteTrackedAsync("Redefinindo rede", async _ => await Network.ResetNetworkAsync(progress));
        };
        grid.Children.Add(flush); grid.Children.Add(time); grid.Children.Add(reset);
        panel.Children.Add(grid);
        return Surface(panel);
    }

    /// <summary>Bloco com ícone, texto e botão; o botão fica em Tag para quem cria ligar o clique.</summary>
    private Border RepairTile(string glyph, string title, string detail, string action)
    {
        var dock = new DockPanel();
        var button = IconButton(Glyphs.ChevronRight, action);
        button.HorizontalAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(button, Dock.Bottom); dock.Children.Add(button);
        var stack = new StackPanel();
        var chip = IconChip(glyph, "Accent", 36); chip.HorizontalAlignment = HorizontalAlignment.Left; chip.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(chip);
        var t = Label(title, 13.5); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(t);
        var d = Label(detail, 11.5, true); d.Margin = new Thickness(0);
        stack.Children.Add(d);
        dock.Children.Add(stack);
        var tile = new Border { Child = dock, Padding = new Thickness(16), Margin = new Thickness(0, 0, 12, 0), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Tag = button };
        tile.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        tile.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return tile;
    }
    #endregion
}
