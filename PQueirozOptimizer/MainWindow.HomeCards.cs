using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PQueirozOptimizer.Controls;
using PQueirozOptimizer.Engine;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Peças do Início e do Meu PC. O Início é propositalmente enxuto: um destaque com o estado do PC e a ação
/// principal, quatro números ao vivo com uma curva discreta e atalhos simples. Os detalhes ficam no Meu PC.
/// Tudo vem de leituras reais; sem sensor, o card diz "Indisponível".
/// </summary>
public partial class MainWindow
{
    /// <summary>Atualizações ao vivo dos cards na tela (uma leitura por segundo do monitor).</summary>
    private readonly List<Action<HardwareSample>> _homeLive = new();
    private FrameworkElement? _homeRoot;

    private static string Tr(string text) => Translator.Tr(text);

    /// <summary>Superfície dos cards da grade: a mesma do Surface (raio e borda do design system), sem a margem inferior.</summary>
    private static Border HomeCard(UIElement content, string background = "CardBgBrush")
    {
        var card = new Border { Child = content, Padding = new Thickness(AppearanceService.Space(20)), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.CornerRadiusProperty, "CardRadius");
        card.SetResourceReference(Border.BackgroundProperty, background);
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return card;
    }

    /// <summary>
    /// Card clicável: a borda acende no destaque e o card sobe 2 px ao passar o mouse (160 ms). Também é alcançável
    /// pelo Tab e abre com Enter ou Espaço, com o mesmo realce de borda como indicação de foco.
    /// </summary>
    private static void MakeClickable(Border card, Action click, string? tooltip = null)
    {
        card.Cursor = Cursors.Hand;
        card.Focusable = true;
        card.FocusVisualStyle = null;
        System.Windows.Automation.AutomationProperties.SetName(card, tooltip ?? "");
        if (tooltip != null) card.ToolTip = tooltip;
        var lift = new TranslateTransform();
        card.RenderTransform = lift;
        void Hover(bool on)
        {
            // Foco pelo teclado com o destaque sólido (3:1 em todos os temas); o hover do mouse fica mais suave
            card.SetResourceReference(Border.BorderBrushProperty, !on ? "CardBorderBrush" : card.IsKeyboardFocused ? "AccentBrush" : "BorderHoverBrush");
            if (AppearanceService.AnimationsEnabled)
                lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(on ? -2 : 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        card.MouseEnter += (_, _) => Hover(true);
        card.MouseLeave += (_, _) => { if (!card.IsKeyboardFocused) Hover(false); };
        card.GotKeyboardFocus += (_, _) => Hover(true);
        // Sem foco: volta ao hover suave se o mouse ainda está em cima, senão à borda normal
        card.LostKeyboardFocus += (_, _) => Hover(card.IsMouseOver);
        card.MouseLeftButtonUp += (_, e) => { if (!e.Handled) click(); };
        card.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; click(); } };
    }

    /// <summary>
    /// Título discreto de uma faixa do Início (legenda em maiúsculas, como nos apps de hardware), com um ponto verde
    /// opcional de "ao vivo" e uma ação à direita.
    /// </summary>
    private FrameworkElement HomeBandTitle(string title, bool live = false, FrameworkElement? right = null)
    {
        var dock = new DockPanel { Margin = new Thickness(2, AppearanceService.Space(6), 2, AppearanceService.Space(10)) };
        if (right != null) { DockPanel.SetDock(right, Dock.Right); dock.Children.Add(right); }
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = Tr(title).ToUpper(CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        row.Children.Add(t);
        if (live)
        {
            var dot = StatusDot("Success", 6); dot.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(dot);
            var l = new TextBlock { Text = Tr("ao vivo"), FontSize = 11, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            l.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            row.Children.Add(l);
        }
        dock.Children.Add(row);
        return dock;
    }

    private Button OpenButton(string tooltip, Action open)
    {
        var button = new Button { Width = 30, Height = 30, Content = GlyphIcon(Glyphs.OpenInNew, 11, "MutedBrush"), ToolTip = tooltip, VerticalAlignment = VerticalAlignment.Top };
        button.SetResourceReference(StyleProperty, "RoundIconButton");
        button.Click += (_, _) => open();
        return button;
    }

    /// <summary>Botão redondo só com ícone (ações discretas do cabeçalho).</summary>
    private Button RoundIcon(string glyph, string tooltip, Action click)
    {
        var button = new Button { Content = GlyphIcon(glyph, 13, "TextBrush"), ToolTip = Tr(tooltip), Margin = new Thickness(8, 0, 0, 0) };
        button.SetResourceReference(StyleProperty, "RoundIconButton");
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Cabeçalho dos cards: rótulo discreto e título; à direita um botão ou selo opcional.</summary>
    private DockPanel CardHeader(string glyph, string label, string title, FrameworkElement? right = null, bool titleIsData = true)
    {
        var dock = new DockPanel();
        if (right != null) { DockPanel.SetDock(right, Dock.Right); dock.Children.Add(right); }
        var texts = new StackPanel { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var l = new TextBlock { Text = label, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var t = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = title, Margin = new Thickness(0, 2, 0, 0) };
        if (titleIsData) t.Tag = Translator.SystemDataTag;
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        texts.Children.Add(l); texts.Children.Add(t);
        dock.Children.Add(texts);
        return dock;
    }

    /// <summary>Linha de detalhe que muda ao vivo (devolve o texto do valor).</summary>
    private static (DockPanel Row, TextBlock Value) LiveRow(string glyph, string label, string value)
    {
        var row = DetailRow(glyph, label, value);
        return (row, (TextBlock)row.Children[0]);
    }

    private static string Percent(double value) => value.ToString("0.0", CultureInfo.CurrentCulture) + "%";
    private static string Gb(double value) => value.ToString(value >= 100 ? "0" : "0.0", CultureInfo.CurrentCulture) + " GB";
    private static string FormatSize(double gb) => gb >= 1000 ? (gb / 1024).ToString("0.##", CultureInfo.CurrentCulture) + " TB" : Gb(gb);

    private static string FirstGpu(string graphics)
    {
        var names = graphics.Split(new[] { ", ", " | ", ";" }, StringSplitOptions.RemoveEmptyEntries).Select(g => g.Trim())
            .Where(g => !g.Contains("Basic", StringComparison.OrdinalIgnoreCase) && !g.Contains("Virtual", StringComparison.OrdinalIgnoreCase)).ToList();
        var name = names.Count == 0 ? graphics : names.OrderByDescending(GpuRank).First();
        return Regex.Replace(Regex.Replace(name, @"\((R|TM|tm|r)\)", ""), @"\s{2,}", " ").Trim();
    }

    /// <summary>
    /// Placa principal num PC com duas (notebook híbrido): a dedicada vem antes da integrada. A NVIDIA vem primeiro
    /// porque é dela que a NVML lê temperatura e memória, então o card nunca mistura dados de placas diferentes.
    /// </summary>
    internal static int GpuRank(string name) =>
        name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? 3
        : Regex.IsMatch(name, @"Radeon\s*(\(TM\)\s*)?(RX|Pro)|\bArc\b", RegexOptions.IgnoreCase) ? 2
        : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(name, @"Radeon\s*(\(TM\)\s*)?Graphics", RegexOptions.IgnoreCase) ? 0
        : 1;

    /// <summary>Nome do processador sem marcas registradas, frequência e o "with Radeon Graphics" dos APUs.</summary>
    private static string ShortCpuName(string name) => Regex.Replace(CleanCpuName(name), @"\s+(with|w/)\s+Radeon.*$", "", RegexOptions.IgnoreCase).Trim();

    /// <summary>Saudação pela hora do dia.</summary>
    private static string Greeting() => DateTime.Now.Hour switch { >= 5 and < 12 => "Bom dia", >= 12 and < 18 => "Boa tarde", _ => "Boa noite" };

    // ================= Destaque: estado do PC e a ação principal =================
    private Border HomeHero(SystemSnapshot snapshot, int score, string headline, string tone, bool showIndicators)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var greeting = new TextBlock { Text = Tr(Greeting()), FontSize = 14 };
        greeting.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        left.Children.Add(greeting);
        var title = new TextBlock { Text = Tr(headline), FontSize = 28, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        left.Children.Add(title);
        var os = snapshot.OperatingSystem.Replace("Microsoft ", "").Replace(" Single Language", "");
        var sub = new TextBlock { Text = $"{Environment.MachineName} · {os} · {Tr("ligado há")} {snapshot.UptimeText}", FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Tag = Translator.SystemDataTag };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        left.Children.Add(sub);

        // O alerta mais importante, se houver (o resto fica no sino da barra superior)
        if (CommandAlerts(snapshot).FirstOrDefault(a => a.Tone is "Danger" or "Warning") is { Text: { } alertText } alert)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(StatusDot(alert.Tone));
            var t = new TextBlock { Text = Tr(alertText), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), Tag = Translator.SystemDataTag };
            t.SetResourceReference(TextBlock.ForegroundProperty, alert.Tone + "Brush");
            row.Children.Add(t);
            if (alert.Page is { } page && page != "dashboard") { row.Cursor = Cursors.Hand; row.MouseLeftButtonUp += (_, _) => NavigateTo(page); }
            left.Children.Add(row);
        }

        var analysis = SmartHistory.Default.LastAnalysis;
        var buttons = new WrapPanel { Margin = new Thickness(0, 22, 0, 0) };
        var main = IconButton(Glyphs.Lightning, analysis is null || analysis.Recommended == 0 ? Tr("Analisar meu PC") : string.Format(Tr("Revisar {0} ajustes"), analysis.Recommended), primary: true);
        if (main.Content is StackPanel { Children: [_, TextBlock mainText] }) mainText.Tag = Translator.SystemDataTag;
        main.Padding = new Thickness(18, 10, 18, 10); main.Margin = new Thickness(0, 0, 10, 0);
        main.ToolTip = Tr("Abrir a Otimização inteligente");
        main.Click += (_, _) => NavigateTo("smart");
        var details = new Button { Content = Tr("Ver meu PC"), Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(0) };
        details.SetResourceReference(Button.BackgroundProperty, "NavHoverBrush");
        details.BorderBrush = Brushes.Transparent;
        details.Click += (_, _) => NavigateTo("mypc");
        buttons.Children.Add(main); buttons.Children.Add(details);
        left.Children.Add(buttons);
        grid.Children.Add(left);

        // À direita: a nota de saúde e os quatro indicadores (detalhes na dica de cada um)
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 4, 0) };
        var ring = ScoreRing(score, Tr("SAÚDE"), 112 * AppearanceService.CardScale);
        ring.ToolTip = string.Format(Tr("Pontuação de saúde: {0} de 100"), score);
        right.Children.Add(ring);
        if (showIndicators)
        {
            var list = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
            var rows = new Dictionary<string, (System.Windows.Shapes.Ellipse Dot, FrameworkElement Row)>();
            foreach (var (key, name) in new[] { ("temperatures", "Temperaturas"), ("health", "Saúde"), ("stability", "Estabilidade"), ("drivers", "Drivers") })
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4), Background = Brushes.Transparent, ToolTip = Tr("Verificando...") };
                var dot = StatusDot("Muted");
                row.Children.Add(dot);
                var label = new TextBlock { Text = Tr(name), FontSize = 13, Margin = new Thickness(10, 0, 0, 0) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                row.Children.Add(label);
                list.Children.Add(row);
                rows[key] = (dot, row);
            }
            right.Children.Add(list);
            HomeData.HealthAsync(snapshot, score, tone).ContinueWith(task => Dispatcher.BeginInvoke(() =>
            {
                if (task.IsFaulted || task.Result is not { } indicators) return;
                foreach (var indicator in indicators)
                {
                    if (!rows.TryGetValue(indicator.Key, out var r)) continue;
                    r.Dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, indicator.Tone + "Brush");
                    r.Row.ToolTip = $"{Tr(indicator.Status)}\n{Tr(indicator.Detail)}";
                }
            }), TaskScheduler.Default);
        }
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        var card = HomeCard(grid, "HeroBrush");
        card.Padding = new Thickness(AppearanceService.Space(28), AppearanceService.Space(26), AppearanceService.Space(28), AppearanceService.Space(26));
        return card;
    }

    // ================= Números ao vivo =================
    /// <summary>
    /// Card de um número (QrzStatCard): ícone no destaque e rótulo, valor grande, uma linha de detalhe e a curva do
    /// último minuto. Antes da primeira leitura mostra "Lendo…" apagado e a linha de base do gráfico, para o card
    /// nunca parecer vazio. Clicar abre o Meu PC com os detalhes.
    /// </summary>
    private Border MetricTile(string glyph, string label, Func<HardwareSample, (string Value, string Detail)> read, Func<HardwareSample, double?> point, double? max, bool live, string tone = "Accent")
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        var chip = IconChip(glyph, tone, 28); chip.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(chip);
        var l = new TextBlock { Text = Tr(label), FontSize = 13, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        l.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        head.Children.Add(l);
        stack.Children.Add(head);
        var value = new TextBlock { Text = "—", FontSize = 28 * AppearanceService.CardScale, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0), Tag = Translator.SystemDataTag };
        value.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        value.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(value);
        var detail = new TextBlock { Text = Tr("Lendo…"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0), Tag = Translator.SystemDataTag };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(detail);
        LineChart? chart = null;
        if (live)
        {
            chart = new LineChart { Height = 42 * AppearanceService.CardScale, Margin = new Thickness(0, 14, 0, 0), Maximum = max, MinimumScale = 1, ShowScale = false, ShowGrid = false, SecondsPerSample = 1, Capacity = HardwareMonitorService.HistoryLength };
            chart.SetValues(HardwareMonitorService.Shared.History.Select(point));
            stack.Children.Add(chart);
        }
        var first = true;
        _homeLive.Add(s =>
        {
            var (v, d) = read(s);
            value.Text = v; detail.Text = d;
            if (first) { first = false; value.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); }
            chart?.Push(point(s));
        });
        var card = HomeCard(stack);
        MakeClickable(card, () => NavigateTo("mypc"), Tr("Ver detalhes em Meu PC"));
        return card;
    }

    // ================= Atalhos =================
    /// <summary>Atalho em linha (mais baixo que as métricas, para as duas faixas não competirem): ícone, título, estado e seta.</summary>
    private Border ShortcutTile(string glyph, string title, string status, Action run)
    {
        var dock = new DockPanel();
        var arrow = GlyphIcon(Glyphs.ChevronRight, 10, "MutedBrush"); arrow.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(arrow, Dock.Right); dock.Children.Add(arrow);
        var chip = IconChip(glyph, "Accent", 36); chip.Margin = new Thickness(0, 0, 12, 0);
        chip.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        DockPanel.SetDock(chip, Dock.Left); dock.Children.Add(chip);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // Até duas linhas: "Laboratório de desempenho" não cabe numa coluna de 1/4 da tela
        var t = new TextBlock { Text = Tr(title), FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 13.5 * 2.8, ToolTip = Tr(title) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var s = new TextBlock { Text = status, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Tag = Translator.SystemDataTag };
        s.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(t); stack.Children.Add(s);
        dock.Children.Add(stack);
        var card = HomeCard(dock);
        card.Padding = new Thickness(AppearanceService.Space(14), AppearanceService.Space(12), AppearanceService.Space(16), AppearanceService.Space(12));
        MakeClickable(card, run, Tr(title));
        return card;
    }

    // ================= Atividade recente =================
    private Border RecentActivity()
    {
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var all = new Button { Content = Tr("Ver histórico"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0) };
        all.SetResourceReference(StyleProperty, "GhostButton");
        all.Click += (_, _) => NavigateTo("history");
        DockPanel.SetDock(all, Dock.Right); head.Children.Add(all);
        var title = new TextBlock { Text = Tr("Atividade recente"), FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        head.Children.Add(title);
        stack.Children.Add(head);
        var lines = _activity.TakeLast(3).Reverse().ToList();
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"^\[(?:\d{4}-\d{2}-\d{2} )?(\d{2}:\d{2})(?::\d{2})?\]\s*\[(\w+)\]\s*(.+)$");
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var time = new TextBlock { Text = m.Success ? m.Groups[1].Value : "", FontSize = 12, Margin = new Thickness(16, 0, 0, 0), Tag = Translator.SystemDataTag, VerticalAlignment = VerticalAlignment.Center };
            time.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            DockPanel.SetDock(time, Dock.Right); row.Children.Add(time);
            var dot = StatusDot((m.Success ? m.Groups[2].Value : "") switch { "ERROR" => "Danger", "WARN" => "Warning", "SUCCESS" => "Success", _ => "Accent" }, 7);
            dot.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(dot);
            var message = new TextBlock { Text = m.Success ? Translator.LogText(m.Groups[3].Value.Trim()) : line, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = line, Tag = Translator.SystemDataTag, VerticalAlignment = VerticalAlignment.Center };
            message.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            row.Children.Add(message);
            stack.Children.Add(row);
        }
        if (lines.Count == 0) stack.Children.Add(Label("Suas próximas execuções aparecerão aqui.", 12.5, true));
        return HomeCard(stack);
    }

    // ================= Armazenamento (Meu PC) =================
    private Border StorageCard(IReadOnlyList<DriveStat> drives)
    {
        var stack = new StackPanel();
        var total = drives.Sum(d => d.TotalGb);
        var header = drives.Count == 0 ? Tr("Nenhuma unidade") : (drives.Count == 1 ? Tr("1 unidade") : string.Format(Tr("{0} unidades"), drives.Count)) + " · " + FormatSize(total);
        stack.Children.Add(CardHeader(Glyphs.Drive, Tr("Armazenamento"), header));
        var list = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        foreach (var d in drives.Take(6))
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            var head = new DockPanel();
            var free = new TextBlock { Text = $"{FormatSize(d.FreeGb)} {Tr("livres de")} {FormatSize(d.TotalGb)}", FontSize = 12, Tag = Translator.SystemDataTag };
            free.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            DockPanel.SetDock(free, Dock.Right); head.Children.Add(free);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            var label = new TextBlock { Text = d.Name + (string.IsNullOrWhiteSpace(d.Label) ? "" : $" {d.Label}"), FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Tag = Translator.SystemDataTag, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            name.Children.Add(label);
            if (d.MediaType is { Length: > 0 } media) { var m = Pill(media, "Info"); m.Padding = new Thickness(6, 0, 6, 0); m.Margin = new Thickness(8, 0, 0, 0); ((TextBlock)m.Child).FontSize = 10; ((TextBlock)m.Child).Tag = Translator.SystemDataTag; name.Children.Add(m); }
            head.Children.Add(name);
            block.Children.Add(head);
            var tone = d.FreePercent >= 20 ? "AccentGradientBrush" : d.FreePercent >= 10 ? "WarningBrush" : "DangerBrush";
            var bar = new ProgressBar { Height = 6, Maximum = 100, Value = d.UsedPercent, Margin = new Thickness(0, 8, 0, 0) };
            bar.SetResourceReference(ProgressBar.ForegroundProperty, tone);
            block.Children.Add(bar);
            list.Children.Add(block);
        }
        stack.Children.Add(list);
        var worst = drives.OrderBy(d => d.FreePercent).FirstOrDefault();
        var (statusTone, statusText) = worst is null ? ("Muted", Tr("Sem unidades fixas para mostrar"))
            : worst.FreePercent < 10 ? ("Danger", string.Format(Tr("Pouco espaço em {0}"), worst.Name))
            : worst.FreePercent < 20 ? ("Warning", string.Format(Tr("Espaço ficando curto em {0}"), worst.Name))
            : ("Success", Tr("Espaço suficiente em todas as unidades"));
        var status = new StackPanel { Orientation = Orientation.Horizontal };
        status.Children.Add(StatusDot(statusTone));
        var s = new TextBlock { Text = statusText, FontSize = 12.5, Margin = new Thickness(9, 0, 0, 0), Tag = Translator.SystemDataTag, TextWrapping = TextWrapping.Wrap };
        s.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        status.Children.Add(s);
        stack.Children.Add(status);
        return HomeCard(stack);
    }
}
