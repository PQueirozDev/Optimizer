using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PQueirozOptimizer.Controls;
using PQueirozOptimizer.Engine;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Cards do Início (QrzStatCard e companhia), no estilo da referência: rótulo discreto, modelo em destaque,
/// número grande em azul, selo de folga, linhas de detalhe e a caixa "atividade ao vivo" com a curva do último minuto.
/// Tudo vem de leituras reais; sem sensor, o card diz "Indisponível".
/// </summary>
public partial class MainWindow
{
    /// <summary>Atualizações ao vivo dos cards na tela (uma leitura por segundo do monitor).</summary>
    private readonly List<Action<HardwareSample>> _homeLive = new();
    private FrameworkElement? _homeRoot;

    private static string Tr(string text) => Translator.Tr(text);

    /// <summary>Superfície dos cards da grade (sem a margem inferior do Surface).</summary>
    private static Border HomeCard(UIElement content, string background = "CardBgBrush")
    {
        var card = new Border { Child = content, Padding = new Thickness(AppearanceService.Space(20)), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, background);
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return card;
    }

    /// <summary>Card clicável: a borda clareia e o card sobe 2 px ao passar o mouse (160 ms).</summary>
    private static void MakeClickable(Border card, Action click, string? tooltip = null)
    {
        card.Cursor = Cursors.Hand;
        if (tooltip != null) card.ToolTip = tooltip;
        var lift = new TranslateTransform();
        card.RenderTransform = lift;
        void Hover(bool on)
        {
            card.SetResourceReference(Border.BorderBrushProperty, on ? "BorderStrongBrush" : "CardBorderBrush");
            if (AppearanceService.AnimationsEnabled)
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(on ? -2 : 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        card.MouseEnter += (_, _) => Hover(true);
        card.MouseLeave += (_, _) => Hover(false);
        card.MouseLeftButtonUp += (_, e) => { if (!e.Handled) click(); };
    }

    private Button OpenButton(string tooltip, Action open)
    {
        var button = new Button { Width = 30, Height = 30, Content = GlyphIcon(Glyphs.OpenInNew, 11, "MutedBrush"), ToolTip = tooltip, VerticalAlignment = VerticalAlignment.Top };
        button.SetResourceReference(StyleProperty, "RoundIconButton");
        button.Click += (_, _) => open();
        return button;
    }

    /// <summary>Cabeçalho dos cards: ícone, rótulo discreto e título (modelo); à direita um botão ou selo opcional.</summary>
    private DockPanel CardHeader(string glyph, string label, string title, FrameworkElement? right = null, bool titleIsData = true)
    {
        var dock = new DockPanel();
        if (right != null) { DockPanel.SetDock(right, Dock.Right); dock.Children.Add(right); }
        var chip = IconChip(glyph, "Accent", 36); DockPanel.SetDock(chip, Dock.Left); dock.Children.Add(chip);
        var texts = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var l = new TextBlock { Text = label, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var t = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = title, Margin = new Thickness(0, 1, 0, 0) };
        if (titleIsData) t.Tag = Translator.SystemDataTag;
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        texts.Children.Add(l); texts.Children.Add(t);
        dock.Children.Add(texts);
        return dock;
    }

    /// <summary>Número grande em azul com o rótulo em cima e o selo de folga embaixo.</summary>
    private static (StackPanel Panel, TextBlock Value, TextBlock PillText, Border Pill) ValueBlock(string caption)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var c = new TextBlock { Text = caption, FontSize = 12 };
        c.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var value = new TextBlock { Text = "--", FontSize = 28 * AppearanceService.CardScale, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 6), Tag = Translator.SystemDataTag };
        value.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        value.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        var pill = Pill("--", "Accent");
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        var pillText = (TextBlock)pill.Child;
        pillText.Tag = Translator.SystemDataTag;
        panel.Children.Add(c); panel.Children.Add(value); panel.Children.Add(pill);
        return (panel, value, pillText, pill);
    }

    /// <summary>Linha de detalhe que muda ao vivo (devolve o texto do valor).</summary>
    private static (DockPanel Row, TextBlock Value) LiveRow(string glyph, string label, string value)
    {
        var row = DetailRow(glyph, label, value);
        return (row, (TextBlock)row.Children[0]);
    }

    /// <summary>Caixa interna "atividade ao vivo": título, selo e a curva do último minuto.</summary>
    private static (Border Box, LineChart Chart, TextBlock Badge, Grid ChartHost) LiveChartBox(string title, double? max, string unit, Func<double, string>? format = null, double height = 56)
    {
        var stack = new DockPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(head, Dock.Top);
        var badge = Pill("--", "Accent"); badge.Padding = new Thickness(7, 2, 7, 2);
        var badgeText = (TextBlock)badge.Child; badgeText.Tag = Translator.SystemDataTag; badgeText.FontSize = 10.5;
        DockPanel.SetDock(badge, Dock.Right); head.Children.Add(badge);
        var icon = GlyphIcon(Glyphs.AreaChart, 11, "MutedBrush"); icon.Margin = new Thickness(0, 0, 7, 0);
        head.Children.Add(icon);
        var t = new TextBlock { Text = title, FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        head.Children.Add(t);
        var chart = new LineChart { MinHeight = height * AppearanceService.CardScale, Maximum = max, Unit = unit, ShowScale = false, SecondsPerSample = 1, Capacity = HardwareMonitorService.HistoryLength, Format = format };
        var host = new Grid(); host.Children.Add(chart);
        stack.Children.Add(head); stack.Children.Add(host);
        return (Inset(stack, 12), chart, badgeText, host);
    }

    /// <summary>Conteúdo fixo no alto e um bloco que ocupa o resto da altura do card (a caixa do gráfico).</summary>
    private static DockPanel FillLast(UIElement top, UIElement? fill, UIElement? bottom = null)
    {
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
        if (bottom != null) { DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom); }
        dock.Children.Add(fill ?? new Border());
        return dock;
    }

    private static string Percent(double value) => value.ToString("0.0", CultureInfo.CurrentCulture) + "%";
    private static string Gb(double value) => value.ToString(value >= 100 ? "0" : "0.0", CultureInfo.CurrentCulture) + " GB";
    private static string FormatSize(double gb) => gb >= 1000 ? (gb / 1024).ToString("0.##", CultureInfo.CurrentCulture) + " TB" : Gb(gb);

    private static string FirstGpu(string graphics)
    {
        var name = graphics.Split(new[] { ", ", " | ", ";" }, StringSplitOptions.RemoveEmptyEntries).Select(g => g.Trim())
            .FirstOrDefault(g => !g.Contains("Basic", StringComparison.OrdinalIgnoreCase) && !g.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) ?? graphics;
        return Regex.Replace(Regex.Replace(name, @"\((R|TM|tm|r)\)", ""), @"\s{2,}", " ").Trim();
    }

    /// <summary>Nome do processador sem marcas registradas, frequência e o "with Radeon Graphics" dos APUs.</summary>
    private static string ShortCpuName(string name) => Regex.Replace(CleanCpuName(name), @"\s+(with|w/)\s+Radeon.*$", "", RegexOptions.IgnoreCase).Trim();

    private static (double? Avg, double? Peak) Window(Func<HardwareSample, double?> select)
    {
        var values = HardwareMonitorService.Shared.History.Select(select).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return values.Count == 0 ? (null, null) : (values.Average(), values.Max());
    }

    // ================= Processador =================
    private Border CpuCard(SystemSnapshot snapshot, CpuInfo cpu, bool live)
    {
        var stack = new StackPanel();
        stack.Children.Add(CardHeader(Glyphs.Chip, Tr("Processador"), ShortCpuName(snapshot.Processor), OpenButton(Tr("Ver detalhes em Meu PC"), () => NavigateTo("mypc"))));
        var middle = new Grid { Margin = new Thickness(0, 16, 0, 14) };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var (valuePanel, value, pillText, _) = ValueBlock(Tr("Uso"));
        middle.Children.Add(valuePanel);
        // Temperatura da CPU: sem driver de kernel não há leitura, então o medidor diz isso em vez de estimar
        var gauge = new QrzGauge { Width = 84 * AppearanceService.CardScale, Height = 84 * AppearanceService.CardScale, ToolTip = Tr("A temperatura da CPU exige um driver de kernel que o Qrztweaks não instala, por segurança.") };
        gauge.Set(null, "--", Tr("Indisponível"));
        Grid.SetColumn(gauge, 1); middle.Children.Add(gauge);
        stack.Children.Add(middle);
        stack.Children.Add(DetailRow(Glyphs.Chip, Tr("Núcleos / threads"), cpu.Cores > 0 ? $"{cpu.Cores} / {cpu.Threads}" : cpu.Threads.ToString()));
        var (_, freq) = LiveRow(Glyphs.Speed, Tr("Frequência"), "--"); stack.Children.Add((UIElement)freq.Parent);
        var (_, procs) = LiveRow(Glyphs.Apps, Tr("Processos"), "--"); stack.Children.Add((UIElement)procs.Parent);
        var (_, avg) = LiveRow(Glyphs.AreaChart, Tr("Carga média (1 min)"), "--"); stack.Children.Add((UIElement)avg.Parent);
        LineChart? chart = null; TextBlock? badge = null; Border? liveBox = null;
        if (live)
        {
            var box = LiveChartBox(Tr("ATIVIDADE AO VIVO"), 100, "%");
            box.Box.Margin = new Thickness(0, 6, 0, 0);
            box.Chart.SetValues(HardwareMonitorService.Shared.History.Select(h => (double?)h.Cpu));
            chart = box.Chart; badge = box.Badge; liveBox = box.Box;
        }
        _homeLive.Add(s =>
        {
            value.Text = Percent(s.Cpu);
            pillText.Text = $"▲ {Percent(100 - s.Cpu)} {Tr("livre")}";
            freq.Text = s.CpuMhz is { } mhz ? (mhz / 1000).ToString("0.00", CultureInfo.CurrentCulture) + " GHz"
                : cpu.BaseMhz is { } b ? (b / 1000).ToString("0.00", CultureInfo.CurrentCulture) + " GHz " + Tr("(base)") : Tr("Indisponível");
            procs.Text = s.Processes?.ToString(CultureInfo.CurrentCulture) ?? "--";
            avg.Text = Window(h => h.Cpu).Avg is { } a ? Percent(a) : "--";
            chart?.Push(s.Cpu);
            if (badge != null) badge.Text = $"{100 - s.Cpu:0}% {Tr("livre")}";
        });
        return HomeCard(FillLast(stack, liveBox));
    }

    // ================= Memória =================
    private Border MemoryCard(SystemSnapshot snapshot, MemoryInfo memory, bool live)
    {
        var stack = new StackPanel();
        var title = $"RAM · {Gb(snapshot.MemoryGb)}" + (memory.Type is { } type ? " " + type : "");
        stack.Children.Add(CardHeader(Glyphs.Memory, Tr("Memória"), title, OpenButton(Tr("Ver detalhes em Meu PC"), () => NavigateTo("mypc"))));
        var (valuePanel, value, pillText, _) = ValueBlock(Tr("Uso"));
        valuePanel.Margin = new Thickness(0, 16, 0, 0);
        stack.Children.Add(valuePanel);
        var bar = new ProgressBar { Height = 8, Maximum = 100, Margin = new Thickness(0, 14, 0, 16) };
        stack.Children.Add(bar);
        var (_, usedRow) = LiveRow(Glyphs.Memory, Tr("Em uso"), "--"); stack.Children.Add((UIElement)usedRow.Parent);
        var (_, freeRow) = LiveRow(Glyphs.Add, Tr("Disponível"), "--"); stack.Children.Add((UIElement)freeRow.Parent);
        stack.Children.Add(DetailRow(Glyphs.Speed, Tr("Velocidade"), memory.SpeedMts is { } mts ? $"{mts} MT/s" : Tr("Indisponível")));
        var (_, peak) = LiveRow(Glyphs.AreaChart, Tr("Pico (1 min)"), "--"); stack.Children.Add((UIElement)peak.Parent);
        LineChart? chart = null; TextBlock? badge = null; Border? liveBox = null;
        if (live)
        {
            var box = LiveChartBox(Tr("ATIVIDADE AO VIVO"), 100, "%");
            box.Box.Margin = new Thickness(0, 6, 0, 0);
            box.Chart.SetValues(HardwareMonitorService.Shared.History.Select(h => (double?)h.Ram));
            chart = box.Chart; badge = box.Badge; liveBox = box.Box;
        }
        _homeLive.Add(s =>
        {
            var free = Math.Max(0, s.RamTotalGb - s.RamUsedGb);
            value.Text = Percent(s.Ram);
            pillText.Text = $"▲ {Gb(free)} {Tr("livres")}";
            bar.Value = s.Ram;
            usedRow.Text = Gb(s.RamUsedGb);
            freeRow.Text = Gb(free);
            peak.Text = Window(h => h.Ram).Peak is { } p ? Percent(p) : "--";
            chart?.Push(s.Ram);
            if (badge != null) badge.Text = $"{Gb(free)} {Tr("livres")}";
        });
        return HomeCard(FillLast(stack, liveBox));
    }

    // ================= Placa de vídeo =================
    private Border GpuCard(SystemSnapshot snapshot, bool live)
    {
        var stack = new StackPanel();
        stack.Children.Add(CardHeader(Glyphs.Monitor, Tr("Placa de vídeo"), FirstGpu(snapshot.Graphics), OpenButton(Tr("Ver detalhes em Meu PC"), () => NavigateTo("mypc"))));
        var middle = new Grid { Margin = new Thickness(0, 16, 0, 14) };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var (valuePanel, value, pillText, pill) = ValueBlock(Tr("Uso (motor 3D)"));
        middle.Children.Add(valuePanel);
        var gauge = new QrzGauge { Width = 84 * AppearanceService.CardScale, Height = 84 * AppearanceService.CardScale };
        gauge.Set(null, "--", Tr("Indisponível"));
        Grid.SetColumn(gauge, 1); middle.Children.Add(gauge);
        stack.Children.Add(middle);
        var (_, vram) = LiveRow(Glyphs.Memory, Tr("Memória de vídeo"), "--"); stack.Children.Add((UIElement)vram.Parent);
        var (_, temp) = LiveRow(Glyphs.Thermometer, Tr("Temperatura"), "--"); stack.Children.Add((UIElement)temp.Parent);
        var (_, avg) = LiveRow(Glyphs.AreaChart, Tr("Carga média (1 min)"), "--"); stack.Children.Add((UIElement)avg.Parent);
        var (_, peak) = LiveRow(Glyphs.Speed, Tr("Pico (1 min)"), "--"); stack.Children.Add((UIElement)peak.Parent);
        LineChart? chart = null; TextBlock? badge = null; Border? liveBox = null;
        if (live)
        {
            var box = LiveChartBox(Tr("ATIVIDADE AO VIVO"), 100, "%");
            box.Box.Margin = new Thickness(0, 6, 0, 0);
            box.Chart.SetValues(HardwareMonitorService.Shared.History.Select(h => h.Gpu));
            chart = box.Chart; badge = box.Badge; liveBox = box.Box;
        }
        _homeLive.Add(s =>
        {
            if (s.Gpu is { } g)
            {
                value.Text = Percent(g);
                pillText.Text = $"▲ {Percent(100 - g)} {Tr("livre")}";
                pill.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush"); pill.SetResourceReference(Border.BorderBrushProperty, "AccentSoftBrush");
            }
            else
            {
                value.Text = "--";
                pillText.Text = Tr("Uso indisponível neste PC");
            }
            if (s.GpuTempC is { } t)
            {
                gauge.Set(Math.Clamp(t, 0, 100), $"{t:0}°", "Celsius");
                gauge.ToolTip = Tr("Temperatura da GPU lida pelo driver NVIDIA.");
                temp.Text = $"{t:0} °C";
            }
            else
            {
                gauge.Set(null, "--", Tr("Indisponível"));
                gauge.ToolTip = Tr("O driver desta placa não informa a temperatura sem um driver extra.");
                temp.Text = Tr("Indisponível");
            }
            vram.Text = s.VramUsedGb is { } used && s.VramTotalGb is { } total ? $"{used.ToString("0.0", CultureInfo.CurrentCulture)} / {Gb(total)}"
                : s.VramTotalGb is { } onlyTotal ? Gb(onlyTotal) : Tr("Indisponível");
            var (a, p) = Window(h => h.Gpu);
            avg.Text = a is { } av ? Percent(av) : "--";
            peak.Text = p is { } pk ? Percent(pk) : "--";
            chart?.Push(s.Gpu);
            if (badge != null) badge.Text = s.Gpu is { } gu ? $"{gu:0}% {Tr("em uso")}" : "--";
        });
        return HomeCard(FillLast(stack, liveBox));
    }

    // ================= Meu PC =================
    private Border MyPcCard(SystemSnapshot snapshot, int score, string tone)
    {
        var grid = new Grid();
        grid.Children.Add(new QrzArt { Kind = ArtKind.Squares, Anchor = 1, Margin = new Thickness(-20) });
        var stack = new StackPanel();
        // Marca e plano, como o card de destaque da referência
        var brand = new DockPanel();
        var plan = CurrentLicense is { } license ? (license.IsAdmin ? "ADMIN" : Tr(license.PlanName).ToUpperInvariant()) : PlanAccess.DemoMode ? "DEMO" : "";
        if (plan.Length > 0) { var p = Pill(plan, "Accent"); ((TextBlock)p.Child).Tag = Translator.SystemDataTag; DockPanel.SetDock(p, Dock.Right); brand.Children.Add(p); }
        var logo = new Image { Width = 22, Height = 22, Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Qrztweaks;component/Assets/app.png")) };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        DockPanel.SetDock(logo, Dock.Left); brand.Children.Add(logo);
        var brandText = new TextBlock { Text = "Qrztweaks", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = Translator.SystemDataTag };
        brandText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        brand.Children.Add(brandText);
        stack.Children.Add(brand);
        var welcome = new TextBlock { Text = Tr("Bem-vindo de volta"), FontSize = 13, Margin = new Thickness(0, 16, 0, 0) };
        welcome.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(welcome);
        var name = new TextBlock { Text = Environment.MachineName, FontSize = 24 * AppearanceService.CardScale, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = Environment.MachineName, Tag = Translator.SystemDataTag };
        name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(name);
        var os = new TextBlock { Text = snapshot.OperatingSystem.Replace("Microsoft ", ""), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0), Tag = Translator.SystemDataTag };
        os.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(os);
        var build = new TextBlock { Text = $"Build {snapshot.Build} · {Tr("ligado há")} {snapshot.UptimeText}", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 14), Tag = Translator.SystemDataTag };
        build.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(build);

        // Indicadores: começam "verificando" e trocam quando a leitura termina (nada é afirmado sem dados)
        var rows = new Dictionary<string, (System.Windows.Shapes.Ellipse Dot, TextBlock Status, FrameworkElement Row)>();
        foreach (var (key, title) in new[] { ("temperatures", "Temperaturas"), ("health", "Saúde"), ("stability", "Estabilidade"), ("drivers", "Drivers") })
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 9), Background = Brushes.Transparent };
            var dot = StatusDot("Muted"); dot.Margin = new Thickness(0, 4, 11, 0); dot.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(dot, Dock.Left); row.Children.Add(dot);
            var texts = new StackPanel();
            var t = new TextBlock { Text = Tr(title), FontSize = 13, FontWeight = FontWeights.SemiBold };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var st = new TextBlock { Text = Tr("Verificando..."), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Tag = Translator.SystemDataTag };
            st.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            texts.Children.Add(t); texts.Children.Add(st);
            row.Children.Add(texts);
            stack.Children.Add(row);
            rows[key] = (dot, st, row);
        }
        var analysis = SmartHistory.Default.LastAnalysis;
        var last = new TextBlock { Text = analysis is null ? Tr("Nenhuma análise ainda") : $"{Tr("Última análise")}: {analysis.At:dd/MM HH:mm}", FontSize = 11.5, Margin = new Thickness(0, 2, 0, 16), Tag = Translator.SystemDataTag };
        last.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(last);
        var buttons = new WrapPanel { VerticalAlignment = VerticalAlignment.Bottom };
        var view = ChevronButton(Tr("Ver meu PC"), primary: true); view.Margin = new Thickness(0, 8, 8, 0);
        view.Click += (_, _) => NavigateTo("mypc");
        var game = SecondaryButton(Glyphs.Game, Tr("Modo Jogo")); game.Margin = new Thickness(0, 8, 0, 0);
        game.Click += (_, _) => NavigateTo("gaming");
        buttons.Children.Add(view); buttons.Children.Add(game);
        grid.Children.Add(FillLast(stack, null, buttons));

        HomeData.HealthAsync(snapshot, score, tone).ContinueWith(task => Dispatcher.BeginInvoke(() =>
        {
            if (task.IsFaulted || task.Result is not { } indicators) return;
            foreach (var indicator in indicators)
            {
                if (!rows.TryGetValue(indicator.Key, out var r)) continue;
                r.Dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, indicator.Tone + "Brush");
                r.Status.Text = Tr(indicator.Status);
                r.Row.ToolTip = Tr(indicator.Detail);
            }
        }), TaskScheduler.Default);
        return HomeCard(grid, "HeroBrush");
    }

    // ================= Armazenamento =================
    private Border StorageCard(IReadOnlyList<DriveStat> drives)
    {
        var stack = new StackPanel();
        var total = drives.Sum(d => d.TotalGb);
        var header = drives.Count == 0 ? Tr("Nenhuma unidade") : (drives.Count == 1 ? Tr("1 unidade") : string.Format(Tr("{0} unidades"), drives.Count)) + " · " + FormatSize(total);
        stack.Children.Add(CardHeader(Glyphs.Drive, Tr("Armazenamento"), header, OpenButton(Tr("Ver detalhes em Meu PC"), () => NavigateTo("mypc"))));
        if ((drives.FirstOrDefault(d => d.IsSystem) ?? drives.FirstOrDefault()) is { } main)
        {
            var (valuePanel, value, pillText, pill) = ValueBlock(string.Format(Tr("Uso do disco do sistema ({0})"), main.Name));
            valuePanel.Margin = new Thickness(0, 16, 0, 14);
            value.Text = main.UsedPercent.ToString("0", CultureInfo.CurrentCulture) + "%";
            pillText.Text = $"▲ {FormatSize(main.FreeGb)} {Tr("livres")}";
            var mainTone = main.FreePercent >= 20 ? "Accent" : main.FreePercent >= 10 ? "Warning" : "Danger";
            if (mainTone != "Accent") { pill.SetResourceReference(Border.BackgroundProperty, mainTone + "SoftBrush"); pillText.SetResourceReference(TextBlock.ForegroundProperty, mainTone + "Brush"); }
            stack.Children.Add(valuePanel);
        }
        foreach (var d in drives.Take(4))
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 11) };
            var head = new DockPanel();
            var free = new TextBlock { Text = $"{FormatSize(d.FreeGb)} {Tr("livres de")} {FormatSize(d.TotalGb)}", FontSize = 11.5, Tag = Translator.SystemDataTag };
            free.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            DockPanel.SetDock(free, Dock.Right); head.Children.Add(free);
            var label = new TextBlock { Text = d.Name + (string.IsNullOrWhiteSpace(d.Label) ? "" : $" {d.Label}"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Tag = Translator.SystemDataTag, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(label);
            if (d.MediaType is { Length: > 0 } media) { var m = Pill(media, "Info"); m.Padding = new Thickness(6, 0, 6, 0); m.Margin = new Thickness(8, 0, 0, 0); ((TextBlock)m.Child).FontSize = 10; ((TextBlock)m.Child).Tag = Translator.SystemDataTag; name.Children.Add(m); }
            head.Children.Add(name);
            block.Children.Add(head);
            var tone = d.FreePercent >= 20 ? "AccentGradientBrush" : d.FreePercent >= 10 ? "WarningBrush" : "DangerBrush";
            var bar = new ProgressBar { Height = 6, Maximum = 100, Value = d.UsedPercent, Margin = new Thickness(0, 6, 0, 0) };
            bar.SetResourceReference(ProgressBar.ForegroundProperty, tone);
            block.Children.Add(bar);
            stack.Children.Add(block);
        }
        var worst = drives.OrderBy(d => d.FreePercent).FirstOrDefault();
        var status = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var (statusTone, statusText) = worst is null ? ("Muted", Tr("Sem unidades fixas para mostrar"))
            : worst.FreePercent < 10 ? ("Danger", string.Format(Tr("Pouco espaço em {0}"), worst.Name))
            : worst.FreePercent < 20 ? ("Warning", string.Format(Tr("Espaço ficando curto em {0}"), worst.Name))
            : ("Success", Tr("Espaço suficiente em todas as unidades"));
        var dot = StatusDot(statusTone); dot.Margin = new Thickness(0, 0, 9, 0); status.Children.Add(dot);
        var s = new TextBlock { Text = statusText, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Tag = Translator.SystemDataTag, TextWrapping = TextWrapping.Wrap };
        s.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        status.Children.Add(s);
        stack.Children.Add(status);
        return HomeCard(stack);
    }

    // ================= Rede =================
    private Border NetworkCard(NetworkInfo network, bool live)
    {
        var stack = new StackPanel();
        var state = Pill(network.Connected ? "● " + Tr("Conectado") : "● " + Tr("Sem conexão"), network.Connected ? "Success" : "Danger");
        ((TextBlock)state.Child).Tag = Translator.SystemDataTag;
        state.VerticalAlignment = VerticalAlignment.Top;
        var title = network.Name is { } n ? (network.Kind.Length > 0 ? $"{network.Kind} · {n}" : n) : Tr("Nenhum adaptador conectado");
        stack.Children.Add(CardHeader(network.Kind == "Wi-Fi" ? Glyphs.Wifi : Glyphs.Ethernet, Tr("Rede"), title, state));

        var stats = new UniformGrid { Columns = 3, Margin = new Thickness(0, 16, 0, 14) };
        TextBlock Stat(string glyph, string caption)
        {
            var panel = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = GlyphIcon(glyph, 11, "MutedBrush"); icon.Margin = new Thickness(0, 0, 6, 0);
            head.Children.Add(icon);
            var c = new TextBlock { Text = caption, FontSize = 12 }; c.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            head.Children.Add(c);
            var v = new TextBlock { Text = "--", FontSize = 20 * AppearanceService.CardScale, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 3, 0, 0), Tag = Translator.SystemDataTag };
            v.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            panel.Children.Add(head); panel.Children.Add(v);
            stats.Children.Add(panel);
            return v;
        }
        var down = Stat(Glyphs.Download, Tr("Download"));
        var up = Stat(Glyphs.Upload, Tr("Upload"));
        var ping = Stat(Glyphs.Globe, Tr("Latência"));
        stack.Children.Add(stats);
        LineChart? downChart = null, upChart = null; TextBlock? badge = null; Border? liveBox = null; StackPanel? legendRow = null;
        if (live)
        {
            static string Mbps(double v) => v.ToString(v < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " Mbps";
            var box = LiveChartBox(Tr("ATIVIDADE DA REDE"), null, " Mbps", Mbps, 92);
            box.Chart.MinimumScale = 1;
            box.Chart.SetValues(HardwareMonitorService.Shared.History.Select(h => (double?)(h.DownBytesPerSec * 8 / 1_000_000)));
            // Upload por cima, na cor secundária, com a mesma escala de tempo
            var upLine = new LineChart { Height = box.Chart.Height, Maximum = null, MinimumScale = 1, ShowScale = false, ShowGrid = false, StrokeKey = "ChartSecondaryBrush", SecondsPerSample = 1, Capacity = HardwareMonitorService.HistoryLength, Format = Mbps, IsHitTestVisible = false };
            upLine.SetValues(HardwareMonitorService.Shared.History.Select(h => (double?)(h.UpBytesPerSec * 8 / 1_000_000)));
            box.ChartHost.Children.Add(upLine);
            downChart = box.Chart; upChart = upLine; badge = box.Badge; liveBox = box.Box;
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 8, 0, 0) };
            foreach (var (brush, label) in new[] { ("ChartLineBrush", Tr("Download")), ("ChartSecondaryBrush", Tr("Upload")) })
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
                var l = new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 0, 14, 0) }; l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                legend.Children.Add(dot); legend.Children.Add(l);
            }
            legendRow = legend;
        }
        _homeLive.Add(s =>
        {
            down.Text = FormatRate(s.DownBytesPerSec);
            up.Text = FormatRate(s.UpBytesPerSec);
            ping.Text = s.PingMs is { } p ? $"{p:0} ms" : "--";
            ping.SetResourceReference(TextBlock.ForegroundProperty, s.PingMs switch { null => "MutedBrush", < 60 => "TextBrush", < 120 => "WarningBrush", _ => "DangerBrush" });
            var downMbps = s.DownBytesPerSec * 8 / 1_000_000; var upMbps = s.UpBytesPerSec * 8 / 1_000_000;
            downChart?.Push(downMbps); upChart?.Push(upMbps);
            // As duas linhas usam o mesmo teto para a comparação ser honesta
            if (downChart != null && upChart != null)
            {
                var max = Math.Max(1, HardwareMonitorService.Shared.History.Select(h => Math.Max(h.DownBytesPerSec, h.UpBytesPerSec) * 8 / 1_000_000).DefaultIfEmpty(0).Max() * 1.2);
                downChart.Maximum = upChart.Maximum = max;
            }
            if (badge != null) badge.Text = s.PingMs is { } pm ? $"{pm:0} ms" : Tr("sem ping");
        });
        return HomeCard(FillLast(stack, liveBox, legendRow));
    }

    // ================= Atividade recente =================
    private Border ActivityCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(CardHeader(Glyphs.History, Tr("Atividade"), Tr("Atividade recente"), OpenButton(Tr("Ver todo o histórico"), () => NavigateTo("history")), titleIsData: false));
        var list = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        var lines = _activity.TakeLast(6).Reverse().ToList();
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"^\[(?:\d{4}-\d{2}-\d{2} )?(\d{2}:\d{2})(?::\d{2})?\]\s*\[(\w+)\]\s*(.+)$");
            var level = m.Success ? m.Groups[2].Value : "";
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 11) };
            var dot = StatusDot(level switch { "ERROR" => "Danger", "WARN" => "Warning", "SUCCESS" => "Success", _ => "Accent" }, 7);
            dot.Margin = new Thickness(0, 5, 10, 0); dot.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(dot);
            var texts = new StackPanel();
            var message = new TextBlock { Text = m.Success ? Tr(m.Groups[3].Value.Trim()) : line, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = line, Tag = Translator.SystemDataTag };
            message.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            texts.Children.Add(message);
            if (m.Success)
            {
                var time = new TextBlock { Text = m.Groups[1].Value, FontSize = 11, Tag = Translator.SystemDataTag };
                time.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                texts.Children.Add(time);
            }
            row.Children.Add(texts);
            list.Children.Add(row);
        }
        if (lines.Count == 0)
        {
            var empty = Label("Suas próximas execuções aparecerão aqui.", 12.5, true);
            list.Children.Add(empty);
        }
        stack.Children.Add(list);
        return HomeCard(stack);
    }

    // ================= Acesso rápido =================
    private Border QuickCard(string title, string subtitle, string tone, string status, string action, ArtKind art, Action run)
    {
        var grid = new Grid { MinHeight = 172 * AppearanceService.CardScale };
        grid.Children.Add(new QrzArt { Kind = art, Anchor = 1, Seed = (int)art, Margin = new Thickness(-20) });
        var stack = new StackPanel();
        var t = new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var s = new TextBlock { Text = subtitle, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
        s.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var dot = StatusDot(tone); dot.Margin = new Thickness(0, 0, 9, 0); statusRow.Children.Add(dot);
        var st = new TextBlock { Text = status, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Tag = Translator.SystemDataTag };
        st.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        statusRow.Children.Add(st);
        stack.Children.Add(t); stack.Children.Add(s); stack.Children.Add(statusRow);
        grid.Children.Add(stack);
        var button = ChevronButton(action);
        button.VerticalAlignment = VerticalAlignment.Bottom;
        button.Click += (_, _) => run();
        grid.Children.Add(button);
        var card = HomeCard(grid);
        MakeClickable(card, run);
        return card;
    }

    // ================= Configurações rápidas =================
    private Border QuickSettingsCard()
    {
        var stack = new StackPanel();
        var all = ChevronButton(Tr("Todas as configurações"));
        all.Click += (_, _) => NavigateTo("settings");
        stack.Children.Add(CardHeader(Glyphs.Sliders, Tr("Configurações"), Tr("Configurações rápidas"), titleIsData: false));
        var prefs = AppearanceService.Current;
        void Section(string caption) { var c = new TextBlock { Text = caption, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6) }; c.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); stack.Children.Add(c); }
        void Row(string title, string hint, bool value, Action<bool> changed, Action<CheckBox>? setup = null)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4), ToolTip = hint, Background = Brushes.Transparent };
            var box = new CheckBox { IsChecked = value, Content = "", VerticalAlignment = VerticalAlignment.Center };
            box.SetResourceReference(StyleProperty, "SwitchCheckBox");
            box.Checked += (_, _) => changed(true);
            box.Unchecked += (_, _) => changed(false);
            setup?.Invoke(box);
            DockPanel.SetDock(box, Dock.Right); row.Children.Add(box);
            var t = new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            row.Children.Add(t);
            stack.Children.Add(row);
        }
        void Update(Action<AppearanceSettings> change) { var next = AppearanceService.Current.Clone(); change(next); SaveAppearance(next); }
        Section(Tr("PREFERÊNCIAS GERAIS"));
        Row(Tr("Animações da interface"), Tr("Entrada das páginas e transições curtas. Os números ao vivo continuam atualizando."), prefs.Animations, on => Update(s => s.Animations = on));
        Row(Tr("Barra lateral compacta"), Tr("Só os ícones; o nome aparece ao passar o mouse (Ctrl+B)."), prefs.CompactSidebar, on => Update(s => s.CompactSidebar = on));
        Row(Tr("Seguir a redução de movimento do Windows"), Tr("Desliga as animações quando \"Mostrar animações no Windows\" estiver desligado."), prefs.FollowSystemMotion, on => Update(s => s.FollowSystemMotion = on));
        Section(Tr("MONITORAMENTO"));
        Row(Tr("Gráficos ao vivo no Início"), Tr("Curvas de CPU, placa de vídeo, memória e rede atualizadas a cada segundo."), prefs.DashboardLive, on => Update(s => s.DashboardLive = on));
        Row(Tr("Perfis automáticos ao abrir jogos"), Tr("Ativa o perfil do jogo quando ele abre e restaura ao fechar."), ProfileStore.Default.WatchGames, on =>
        {
            ProfileStore.Default.WatchGames = on;
            if (on) StartGameWatcher(); else StopGameWatcher();
            ShowToast(Tr("Perfis automáticos"), on ? Tr("Ligados: o perfil de cada jogo é ativado quando ele abre.") : Tr("Desligados."), on ? "Success" : "Info");
        });
        Row(Tr("Modo compacto (segundo monitor)"), Tr("Janela pequena com CPU, GPU, RAM e temperaturas."), _compact is { IsVisible: true }, on =>
        {
            if (on) OpenCompactMonitor(); else _compact?.Close();
        }, box => { if (_compact is { } window) window.Closed += (_, _) => box.IsChecked = false; });
        all.HorizontalAlignment = HorizontalAlignment.Stretch;
        all.HorizontalContentAlignment = HorizontalAlignment.Center;
        all.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(all);
        return HomeCard(stack);
    }

    // ================= Seus jogos =================
    private Border GamesCard()
    {
        var stack = new StackPanel();
        var lastRun = SmartHistory.Default.Runs.FirstOrDefault()?.At ?? SmartHistory.Default.LastAnalysis?.At;
        FrameworkElement? chip = null;
        if (lastRun is { } at)
        {
            var ago = DateTime.Now - at;
            var text = ago.TotalMinutes < 1 ? Tr("agora") : ago.TotalHours < 1 ? string.Format(Tr("há {0} min"), (int)ago.TotalMinutes) : ago.TotalDays < 1 ? string.Format(Tr("há {0} h"), (int)ago.TotalHours) : string.Format(Tr("há {0} dias"), (int)ago.TotalDays);
            var p = Pill($"{Tr("Última otimização")} · {text}", "Info"); ((TextBlock)p.Child).Tag = Translator.SystemDataTag;
            p.VerticalAlignment = VerticalAlignment.Top;
            chip = p;
        }
        stack.Children.Add(CardHeader(Glyphs.Game, Tr("Jogos"), Tr("Seus jogos"), chip, titleIsData: false));
        var tiles = new QrzGrid { MinColumnWidth = 150, MaxColumns = 5, ColumnGap = 10, RowGap = 10, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var preset in GameConfigService.Presets)
        {
            string status; string tone;
            try
            {
                var applied = GameConfigService.AppliedProfile(preset);
                var found = GameConfigService.ConfigPath(preset) != null;
                (status, tone) = applied is { } profile ? ($"{Tr("Perfil")}: {Tr(profile)}", "Success") : found ? (Tr("Encontrado"), "Info") : (Tr("Não encontrado"), "Muted");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { (status, tone) = (Tr("Não encontrado"), "Muted"); }
            var tile = new Border { Height = 104 * AppearanceService.CardScale, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = Tr("Abrir as configurações deste jogo") };
            tile.SetResourceReference(Border.BackgroundProperty, "CardInsetBrush");
            tile.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
            var layers = new Grid();
            var cover = new Border { CornerRadius = new CornerRadius(11) };
            layers.Children.Add(cover);
            var shade = new Border { CornerRadius = new CornerRadius(11), Background = new LinearGradientBrush(Color.FromArgb(0x10, 0, 0, 0), Color.FromArgb(0xD8, 0, 0, 0), 90) };
            layers.Children.Add(shade);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 10, 9) };
            var name = new TextBlock { Text = preset.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Tag = Translator.SystemDataTag };
            var st = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            var dot = StatusDot(tone, 6); dot.Margin = new Thickness(0, 0, 6, 0); st.Children.Add(dot);
            st.Children.Add(new TextBlock { Text = status, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)), Tag = Translator.SystemDataTag, TextTrimming = TextTrimming.CharacterEllipsis });
            info.Children.Add(name); info.Children.Add(st);
            layers.Children.Add(info);
            tile.Child = layers;
            tile.MouseEnter += (_, _) => tile.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
            tile.MouseLeave += (_, _) => tile.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
            tile.MouseLeftButtonUp += (_, _) => OpenGamingTab(1);
            tiles.Children.Add(tile);
            // Capa (cache local da Steam/Epic ou download do CDN) sem travar a tela
            _ = GameArtService.PresetArtAsync(preset.Id).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                if (t.IsCompletedSuccessfully && t.Result is { } image) cover.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
            }), TaskScheduler.Default);
        }
        stack.Children.Add(tiles);
        var hint = new TextBlock { Text = Tr("Clique em um jogo para aplicar o preset competitivo ou um perfil."), FontSize = 11.5, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(hint);
        return HomeCard(stack);
    }
}
