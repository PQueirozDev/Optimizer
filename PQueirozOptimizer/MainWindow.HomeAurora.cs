using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PQueirozOptimizer.Controls;
using PQueirozOptimizer.Engine;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Início no visual Aurora: card de saúde (nota, indicadores com evidência real e a ação principal), HUD com
/// medidores de CPU, GPU e RAM, gráfico de carga de 1, 5 ou 15 minutos e acesso rápido.
/// </summary>
public partial class MainWindow
{
    private static int _homeRangeMinutes = 5;

    /// <summary>Um indicador do card de saúde: nome, estado lido agora, tom e a página que explica.</summary>
    private sealed record HomeCheck(string Name, string Status, string Tone, string Page);

    // As leituras do card de saúde (WMI, log e dispositivos) valem por 10 minutos: o Início abre na hora
    private static (DateTime At, List<HomeCheck> Checks)? _homeChecks;

    private UIElement AuroraTopRow(SystemSnapshot snapshot, CpuInfo cpuInfo, int score, string headline, string tone, AppearanceSettings prefs)
    {
        var grid = HomeGrid(300);
        grid.MaxColumns = 3; grid.AllowedColumns = new[] { 1, 3 };
        grid.Children.Add(AuroraHealthCard(score, headline, tone, prefs.DashboardStatus));
        if (prefs.DashboardHardware) grid.Children.Add(Span(Mark(AuroraHudCard(snapshot, cpuInfo, prefs.DashboardLive), "home.hud"), 2));
        return grid;
    }

    private UIElement AuroraSecondRow(int score, AppearanceSettings prefs)
    {
        var grid = HomeGrid(300);
        grid.MaxColumns = 3; grid.AllowedColumns = new[] { 1, 3 };
        if (prefs.DashboardLive) grid.Children.Add(Span(AuroraLoadCard(), 2));
        if (prefs.DashboardQuickActions) grid.Children.Add(AuroraQuickCard(score));
        return grid;
    }

    // ================= Saúde =================
    private Border AuroraHealthCard(int score, string headline, string tone, bool showChecks)
    {
        var body = new StackPanel();
        var name = CurrentLicense?.Licensee.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var greeting = new TextBlock { Text = Tr(Greeting()) + (name is null ? "" : ", " + name), FontSize = 13, FontWeight = FontWeights.SemiBold, Tag = Translator.SystemDataTag };
        greeting.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        body.Children.Add(greeting);
        var title = new TextBlock { Text = Tr(headline), FontSize = 22, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 16), Tag = Translator.SystemDataTag };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        body.Children.Add(title);

        var row = new DockPanel();
        var ring = ScoreRing(score, Tr("SAÚDE"), 108);
        ring.Margin = new Thickness(0, 0, 18, 0);
        DockPanel.SetDock(ring, Dock.Left); row.Children.Add(ring);
        var checks = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (showChecks)
        {
            void Fill(List<HomeCheck> list)
            {
                checks.Children.Clear();
                foreach (var check in list) checks.Children.Add(HomeCheckRow(check));
            }
            if (_homeChecks is { } cached && DateTime.Now - cached.At < TimeSpan.FromMinutes(10)) Fill(cached.Checks);
            else
            {
                Fill(new List<HomeCheck> { new("Temperaturas", "Lendo…", "Muted", "health"), new("Discos", "Lendo…", "Muted", "health"), new("Estabilidade", "Lendo…", "Muted", "health"), new("Drivers", "Lendo…", "Muted", "devices") });
                _ = Task.Run(ReadHomeChecks).ContinueWith(t =>
                {
                    if (!t.IsCompletedSuccessfully) return;
                    _homeChecks = (DateTime.Now, t.Result);
                    Fill(t.Result);
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }
        row.Children.Add(checks);
        body.Children.Add(row);

        var analysis = SmartHistory.Default.LastAnalysis;
        var primary = IconButton(Glyphs.Lightning, analysis is { Recommended: > 0 } a ? string.Format(Tr("Revisar {0} ajustes"), a.Recommended) : Tr("Analisar o PC"), primary: true);
        primary.HorizontalAlignment = HorizontalAlignment.Stretch; primary.HorizontalContentAlignment = HorizontalAlignment.Center;
        primary.Height = 44; primary.Margin = new Thickness(0, 20, 0, 0);
        primary.Click += (_, _) => NavigateTo("smart");
        body.Children.Add(Mark(primary, "home.primary"));
        var secondary = new Button { Height = 40, Margin = new Thickness(0, 10, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        var secondaryContent = new StackPanel { Orientation = Orientation.Horizontal };
        secondaryContent.Children.Add(new TextBlock { Text = Tr("Ver meu PC"), VerticalAlignment = VerticalAlignment.Center });
        var arrow = GlyphIcon(Glyphs.ChevronRight, 12, "MutedBrush"); arrow.Margin = new Thickness(8, 1, 0, 0);
        secondaryContent.Children.Add(arrow);
        secondary.Content = secondaryContent;
        secondary.SetResourceReference(StyleProperty, "GhostButton");
        secondary.SetResourceReference(BorderBrushProperty, "BorderBrush");
        secondary.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
        secondary.Click += (_, _) => NavigateTo("mypc");
        body.Children.Add(secondary);
        var card = HomeCard(body, "HeroBrush");
        card.Padding = new Thickness(AppearanceService.Space(24));
        return card;
    }

    private Border HomeCheckRow(HomeCheck check)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var dot = StatusDot(check.Tone, 7); dot.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(dot, Dock.Left); dock.Children.Add(dot);
        var status = new TextBlock { Text = Tr(check.Status), FontSize = 12, Margin = new Thickness(10, 0, 0, 0), Tag = Translator.SystemDataTag, VerticalAlignment = VerticalAlignment.Center };
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        DockPanel.SetDock(status, Dock.Right); dock.Children.Add(status);
        var name = new TextBlock { Text = Tr(check.Name), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Tag = Translator.SystemDataTag };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        dock.Children.Add(name);
        var row = new Border { Child = dock, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = Tr("Ver detalhes") };
        row.MouseLeftButtonUp += (_, _) => NavigateTo(check.Page);
        return row;
    }

    /// <summary>Indicadores com evidência: temperatura da GPU, S.M.A.R.T. dos discos, erros WHEA da semana e drivers com problema.</summary>
    private static List<HomeCheck> ReadHomeChecks()
    {
        var list = new List<HomeCheck>();
        // O Início pode abrir antes da primeira leitura do monitor: lê o sensor direto em vez de guardar "sem sensor" por 10 min
        var temp = HardwareMonitorService.Shared.Latest?.GpuTempC ?? NvmlReader.TemperatureC();
        list.Add(temp switch
        {
            null => new("Temperaturas", "Sem sensor", "Muted", "health"),
            >= 87 => new("Temperaturas", $"GPU {temp:0} °C", "Danger", "health"),
            >= 80 => new("Temperaturas", $"GPU {temp:0} °C", "Warning", "health"),
            _ => new("Temperaturas", "Normal", "Success", "health"),
        });
        try
        {
            var disks = HardwareHealthService.ReadDisks();
            list.Add(disks.Any(d => d.Verdict == HealthVerdict.Critical) ? new("Discos", "Com problema", "Danger", "health")
                : disks.Any(d => d.Verdict == HealthVerdict.Warning) ? new("Discos", "Atenção", "Warning", "health")
                : disks.All(d => d.Verdict == HealthVerdict.Unknown) ? new("Discos", "Sem dados", "Muted", "health")
                : new("Discos", "Saudáveis", "Success", "health"));
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException) { list.Add(new("Discos", "Sem dados", "Muted", "health")); }
        var errors = HardwareHealthService.ErrorsSince(DateTime.Now.AddDays(-7));
        list.Add(errors.Error != null ? new("Estabilidade", "Sem dados", "Muted", "health")
            : errors.Whea > 0 ? new("Estabilidade", errors.Whea == 1 ? "1 erro WHEA" : $"{errors.Whea} erros WHEA", "Danger", "health")
            : errors.GpuResets > 0 ? new("Estabilidade", errors.GpuResets == 1 ? "1 alerta" : $"{errors.GpuResets} alertas", "Warning", "health")
            : new("Estabilidade", "Sem erros", "Success", "health"));
        try
        {
            var problems = DeviceCleanupService.List().Count(d => d.HasProblem);
            list.Add(problems == 0 ? new("Drivers", "Em dia", "Success", "devices") : new("Drivers", problems == 1 ? "1 com problema" : $"{problems} com problema", "Warning", "devices"));
        }
        catch (System.ComponentModel.Win32Exception) { list.Add(new("Drivers", "Sem dados", "Muted", "devices")); }
        return list;
    }

    // ================= HUD =================
    private Border AuroraHudCard(SystemSnapshot snapshot, CpuInfo cpuInfo, bool live)
    {
        var body = new DockPanel();
        var meta = new TextBlock { Text = $"{ShortCpuName(snapshot.Processor)} · {FirstGpu(snapshot.Graphics)} · {Gb(snapshot.MemoryGb)}", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420, Tag = Translator.SystemDataTag };
        meta.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var head = HomeBandTitle("Desempenho", live, meta);
        head.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(head, Dock.Top); body.Children.Add(head);

        var row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Rows = 1 };
        (ArcGauge Gauge, TextBlock Value, TextBlock Sub) Gauge(string label)
        {
            var host = new Grid { Width = 156, Height = 156, HorizontalAlignment = HorizontalAlignment.Center };
            var gauge = new ArcGauge { Value = double.NaN, Thickness = 10 };
            host.Children.Add(gauge);
            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            var value = new TextBlock { Text = "—", FontSize = 32, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Tag = Translator.SystemDataTag };
            value.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            value.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var l = new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Tag = Translator.SystemDataTag };
            l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            center.Children.Add(value); center.Children.Add(l);
            host.Children.Add(center);
            var sub = new TextBlock { Text = "—", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -14, 0, 0), Tag = Translator.SystemDataTag };
            sub.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            sub.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(host); stack.Children.Add(sub);
            row.Children.Add(stack);
            return (gauge, value, sub);
        }
        var cpu = Gauge("CPU");
        var gpu = Gauge("GPU");
        var ram = Gauge("RAM");

        var stats = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        TextBlock Stat(string label, string unit)
        {
            var l = new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.Bold, Tag = Translator.SystemDataTag };
            l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 14) };
            var v = new TextBlock { Text = "—", FontSize = 26, FontWeight = FontWeights.Bold, Tag = Translator.SystemDataTag };
            v.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var u = new TextBlock { Text = unit, FontSize = 13, Margin = new Thickness(4, 0, 0, 4), VerticalAlignment = VerticalAlignment.Bottom, Tag = Translator.SystemDataTag };
            u.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            line.Children.Add(v); line.Children.Add(u);
            stats.Children.Add(l); stats.Children.Add(line);
            return v;
        }
        var ping = Stat("PING", "ms");
        var down = Stat("DOWNLOAD", "Mbps");
        var disk = Stat(Tr("DISCO DO SISTEMA"), Tr("% livre"));
        disk.Text = snapshot.StorageGb > 0 ? snapshot.FreePercent.ToString("0", CultureInfo.CurrentCulture) : "—";
        row.Children.Add(stats);
        body.Children.Add(row);

        _homeLive.Add(s =>
        {
            cpu.Gauge.Value = s.Cpu; cpu.Value.Text = $"{s.Cpu:0}%";
            cpu.Sub.Text = s.CpuMhz is { } mhz ? (mhz / 1000).ToString("0.00", CultureInfo.CurrentCulture) + " GHz" : cpuInfo.BaseMhz is { } b ? (b / 1000).ToString("0.00", CultureInfo.CurrentCulture) + " GHz" : "—";
            gpu.Gauge.Value = s.Gpu ?? double.NaN; gpu.Value.Text = s.Gpu is { } g ? $"{g:0}%" : "—";
            gpu.Sub.Text = s.GpuTempC is { } t ? $"{t:0} °C" : s.VramUsedGb is { } vu && s.VramTotalGb is { } vt ? $"{vu:0.0} / {vt:0} GB" : "—";
            ram.Gauge.Value = s.Ram; ram.Value.Text = $"{s.Ram:0}%";
            ram.Sub.Text = $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0} GB";
            ping.Text = s.PingMs is { } p ? p.ToString("0", CultureInfo.CurrentCulture) : "—";
            down.Text = (s.DownBytesPerSec * 8 / 1_000_000).ToString(s.DownBytesPerSec * 8 / 1_000_000 >= 10 ? "0" : "0.0", CultureInfo.CurrentCulture);
        });
        var card = HomeCard(body);
        card.Padding = new Thickness(AppearanceService.Space(24), AppearanceService.Space(18), AppearanceService.Space(24), AppearanceService.Space(20));
        return card;
    }

    // ================= Gráfico de carga =================
    private Border AuroraLoadCard()
    {
        var body = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(chips, Dock.Right); head.Children.Add(chips);
        var title = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Tag = Translator.SystemDataTag };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        head.Children.Add(title);
        body.Children.Add(head);

        var charts = new Grid { Height = 190 };
        var gpuChart = new LineChart { StrokeKey = "ChartSecondaryBrush", Maximum = 100, Unit = "%", ShowScale = true, SecondsPerSample = 1, Format = v => $"GPU {v:0}%" };
        var cpuChart = new LineChart { StrokeKey = "ChartLineBrush", FillKey = "SemPreenchimento", Maximum = 100, Unit = "%", ShowScale = false, ShowGrid = false, SecondsPerSample = 1, Format = v => $"CPU {v:0}%" };
        charts.Children.Add(gpuChart); charts.Children.Add(cpuChart);
        body.Children.Add(charts);

        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        void Legend(string key, string text)
        {
            var swatch = new Border { Width = 14, Height = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, key);
            var t = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(0, 0, 18, 0), Tag = Translator.SystemDataTag };
            t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            legend.Children.Add(swatch); legend.Children.Add(t);
        }
        Legend("ChartSecondaryBrush", "GPU"); Legend("ChartLineBrush", "CPU");
        body.Children.Add(legend);

        void Redraw()
        {
            var history = HardwareMonitorService.Shared.LongHistory;
            var interval = HardwareMonitorService.Shared.IntervalSeconds;
            var take = _homeRangeMinutes * 60 / interval;
            var window = history.Skip(Math.Max(0, history.Count - take)).ToList();
            // No máximo 180 pontos: médias de blocos (15 min = blocos de 5 s), para a curva não virar um serrote
            var bucket = Math.Max(1, (int)Math.Ceiling(window.Count / 180.0));
            IEnumerable<double?> Series(Func<HardwareSample, double?> pick) => window.Chunk(bucket).Select(c => c.Select(pick).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(double.NaN).Average() is var avg && !double.IsNaN(avg) ? (double?)avg : null);
            gpuChart.SecondsPerSample = cpuChart.SecondsPerSample = bucket * interval;
            // A capacidade acompanha a janela escolhida (o SetValues só aumenta): 15 → 1 min ocupa o gráfico inteiro
            gpuChart.Capacity = cpuChart.Capacity = Math.Max(2, (int)Math.Ceiling(take / (double)bucket));
            gpuChart.SetValues(Series(s => s.Gpu));
            cpuChart.SetValues(Series(s => s.Cpu));
        }
        void RenderChips()
        {
            title.Text = string.Format(Tr(_homeRangeMinutes == 1 ? "Carga no último minuto" : "Carga nos últimos {0} minutos"), _homeRangeMinutes);
            chips.Children.Clear();
            foreach (var minutes in new[] { 1, 5, 15 })
            {
                var selected = minutes == _homeRangeMinutes;
                var chip = new Button { Content = $"{minutes} min", Height = 28, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(6, 0, 0, 0), FontSize = 12, BorderThickness = new Thickness(1), Tag = Translator.SystemDataTag };
                QrzUi.SetCornerRadius(chip, new CornerRadius(9));
                chip.SetResourceReference(StyleProperty, "GhostButton");
                chip.SetResourceReference(BackgroundProperty, selected ? "AccentSoftBrush" : "PanelBrush");
                chip.SetResourceReference(BorderBrushProperty, selected ? "SecondaryBorderBrush" : "BorderSubtleBrush");
                chip.SetResourceReference(ForegroundProperty, selected ? "TextBrush" : "MutedBrush");
                var m = minutes;
                chip.Click += (_, _) => { _homeRangeMinutes = m; RenderChips(); Redraw(); };
                chips.Children.Add(chip);
            }
        }
        RenderChips();
        Redraw();
        _homeLive.Add(_ => Redraw());
        return HomeCard(body);
    }

    // ================= Acesso rápido =================
    private Border AuroraQuickCard(int score)
    {
        var body = new StackPanel();
        var head = HomeBandTitle("Acesso rápido");
        head.Margin = new Thickness(4, 0, 0, 6);
        body.Children.Add(head);
        var analysis = SmartHistory.Default.LastAnalysis;
        var lastTest = PerfLabStore.Default.List().FirstOrDefault();
        var session = GamingService.ActiveSession();
        var entries = new (string Glyph, string Title, string Detail, string Page)[]
        {
            (Glyphs.Lightning, "Otimização inteligente", analysis is null ? Tr("Analise o PC") : string.Format(Tr("{0} ajustes recomendados"), analysis.Recommended), "smart"),
            (Glyphs.Flask, "Laboratório de desempenho", lastTest is null ? Tr("Meça FPS e tempo de quadro") : $"{Tr("Último teste")}: {lastTest.StartedAt:dd/MM}", "perflab"),
            (Glyphs.Fire, "Stress test", Tr("CPU e memória com conferência de erros"), "stress"),
            (Glyphs.Game, "Modo Jogo", session is null ? Tr("Desligado") : session.RecoveryPending ? Tr("Restauração pendente") : Tr("Ativo"), "gaming"),
            (Glyphs.Health, "Saúde do hardware", string.Format(Tr("Saúde {0} de 100"), score), "health"),
        };
        for (var i = 0; i < entries.Length; i++)
        {
            var (glyph, title, detail, page) = entries[i];
            var dock = new DockPanel();
            var chip = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(11), Child = GlyphIcon(glyph, 16, "AccentTextBrush") };
            chip.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            DockPanel.SetDock(chip, Dock.Left); dock.Children.Add(chip);
            var arrow = GlyphIcon(Glyphs.ChevronRight, 13, "MutedBrush");
            DockPanel.SetDock(arrow, Dock.Right); dock.Children.Add(arrow);
            var texts = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBlock { Text = Tr(title), FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var d = new TextBlock { Text = detail, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Tag = Translator.SystemDataTag };
            d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            texts.Children.Add(t); texts.Children.Add(d);
            dock.Children.Add(texts);
            var row = new Border { Child = dock, Padding = new Thickness(8, 9, 8, 9), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0), Background = Brushes.Transparent };
            row.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
            var target = page;
            MakeClickable(row, () => NavigateTo(target), Tr(title));
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "NavHoverBrush");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            body.Children.Add(row);
        }
        var card = HomeCard(body);
        card.Padding = new Thickness(AppearanceService.Space(14), AppearanceService.Space(16), AppearanceService.Space(14), AppearanceService.Space(10));
        return card;
    }
}
