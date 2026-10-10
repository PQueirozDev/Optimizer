using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>HUD ao vivo: medidores da barra lateral, painel em tempo real da visão geral e transição entre páginas.</summary>
public partial class MainWindow
{
    private sealed record LiveTile(TextBlock Value, TextBlock Detail, Canvas Chart, Func<HardwareSample, double?> Select, bool AutoScale);
    private readonly List<LiveTile> _liveTiles = new();
    private Border? _livePanel;

    private void StartHud()
    {
        Action<HardwareSample> onSample = sample => Dispatcher.BeginInvoke(() => UpdateHud(sample));
        HardwareMonitorService.Shared.Sampled += onSample;
        Closed += (_, _) => HardwareMonitorService.Shared.Sampled -= onSample;
        UpdateGameModeBadge();
    }

    private void UpdateHud(HardwareSample s)
    {
        SetMeter(HudCpuBar, HudCpuText, s.Cpu);
        SetMeter(HudRamBar, HudRamText, s.Ram);
        if (s.Gpu is { } gpu) SetMeter(HudGpuBar, HudGpuText, gpu);
        else { HudGpuBar.Value = 0; HudGpuText.Text = "--"; }
        HudPing.Text = s.PingMs is { } ping ? $"{ping:0} ms" : "-- ms";
        HudPing.SetResourceReference(TextBlock.ForegroundProperty, s.PingMs switch { null => "MutedBrush", < 60 => "SuccessBrush", < 120 => "WarningBrush", _ => "DangerBrush" });
        HudLiveDot.Opacity = HudLiveDot.Opacity > 0.6 ? 0.45 : 1; // pisca a cada leitura: mostra que está ao vivo

        // O painel da visão geral só é atualizado enquanto está na tela
        if (_livePanel is null || !IsInContent(_livePanel)) return;
        var history = HardwareMonitorService.Shared.History;
        foreach (var tile in _liveTiles) DrawSparkline(tile, history);
        _liveTiles[0].Value.Text = $"{s.Cpu:0}%";
        _liveTiles[1].Value.Text = s.Gpu is { } g ? $"{g:0}%" : "--";
        _liveTiles[1].Detail.Text = s.Gpu is null ? "Uso da GPU indisponível neste PC" : "Motor 3D da placa de vídeo";
        _liveTiles[2].Value.Text = $"{s.Ram:0}%";
        _liveTiles[2].Detail.Text = $"{s.RamUsedGb:0.0} de {s.RamTotalGb:0.0} GB em uso";
        _liveTiles[3].Value.Text = s.PingMs is { } p ? $"{p:0} ms" : "--";
        _liveTiles[3].Detail.Text = $"↓ {FormatRate(s.DownBytesPerSec)}   ↑ {FormatRate(s.UpBytesPerSec)}";
    }

    private bool IsInContent(DependencyObject element)
    {
        for (var node = element; node != null; node = VisualTreeHelper.GetParent(node))
            if (node == ContentHost) return true;
        return false;
    }

    private static void SetMeter(ProgressBar bar, TextBlock text, double value)
    {
        bar.Value = value;
        text.Text = $"{value:0}%";
        // Acima de 85% o medidor fica em alerta
        text.SetResourceReference(TextBlock.ForegroundProperty, value >= 85 ? "DangerBrush" : value >= 65 ? "WarningBrush" : "TextBrush");
    }

    internal static string FormatRate(double bytesPerSecond)
    {
        var bits = bytesPerSecond * 8;
        return bits >= 1_000_000 ? $"{bits / 1_000_000:0.0} Mbps" : bits >= 1000 ? $"{bits / 1000:0} Kbps" : "0 Kbps";
    }

    /// <summary>Painel "Monitor em tempo real" da visão geral: uso atual e gráfico do último minuto.</summary>
    private Border BuildLivePanel()
    {
        _liveTiles.Clear();
        var panel = new StackPanel();
        var head = new DockPanel();
        var live = Pill("● AO VIVO", "Success"); DockPanel.SetDock(live, Dock.Right); live.VerticalAlignment = VerticalAlignment.Top;
        head.Children.Add(live);
        head.Children.Add(SectionHeader("Monitor em tempo real", "Uso de CPU, placa de vídeo, memória e rede no último minuto."));
        panel.Children.Add(head);

        var grid = Responsive(new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -12, 0) }, 210, 4);
        grid.Children.Add(LiveTileView("CPU", Glyphs.Chip, "Accent", "Processador", s => s.Cpu, false));
        grid.Children.Add(LiveTileView("GPU", Glyphs.Monitor, "Warning", "Motor 3D da placa de vídeo", s => s.Gpu, false));
        grid.Children.Add(LiveTileView("MEMÓRIA", Glyphs.Memory, "Info", "Memória RAM", s => s.Ram, false));
        grid.Children.Add(LiveTileView("PING", Glyphs.Globe, "Success", "Rede", s => s.PingMs, true));
        panel.Children.Add(grid);

        _livePanel = Surface(panel);
        if (HardwareMonitorService.Shared.Latest is { } latest) Dispatcher.BeginInvoke(() => UpdateHud(latest), System.Windows.Threading.DispatcherPriority.Loaded);
        return _livePanel;
    }

    private Border LiveTileView(string title, string glyph, string tone, string detail, Func<HardwareSample, double?> select, bool autoScale)
    {
        var stack = new StackPanel();
        var top = new DockPanel();
        var scale = AppearanceService.CardScale;
        var chip = IconChip(glyph, tone, 26 * scale); DockPanel.SetDock(chip, Dock.Right); top.Children.Add(chip);
        var label = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        top.Children.Add(label);
        stack.Children.Add(top);
        var value = new TextBlock { Text = "--", FontSize = 24 * scale, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0), Tag = Translator.SystemDataTag };
        value.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        value.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(value);
        var sub = new TextBlock { Text = detail, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(sub);
        var chart = new Canvas { Height = 36 * scale, Margin = new Thickness(0, 10, 0, 0), ClipToBounds = true, Tag = tone };
        stack.Children.Add(chart);
        var tile = new LiveTile(value, sub, chart, select, autoScale);
        chart.SizeChanged += (_, _) => DrawSparkline(tile, HardwareMonitorService.Shared.History);
        _liveTiles.Add(tile);

        var card = new Border { Child = stack, Padding = new Thickness(AppearanceService.Space(15) * scale, AppearanceService.Space(13) * scale, AppearanceService.Space(15) * scale, AppearanceService.Space(11) * scale), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 0) };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return card;
    }

    /// <summary>Gráfico de linha com área preenchida; escala fixa 0–100% ou automática (ping).</summary>
    private static void DrawSparkline(LiveTile tile, IReadOnlyList<HardwareSample> history)
    {
        var canvas = tile.Chart;
        canvas.Children.Clear();
        var w = canvas.ActualWidth; var h = canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var values = history.Select(tile.Select).ToList();
        var known = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (known.Count < 2) return;
        var max = tile.AutoScale ? Math.Max(40, known.Max() * 1.25) : 100;
        var step = w / (HardwareMonitorService.HistoryLength - 1);
        var offset = HardwareMonitorService.HistoryLength - values.Count;
        var line = new Polyline { StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round };
        var area = new Polygon();
        double lastX = 0;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not { } v) continue;
            var point = new Point((offset + i) * step, h - Math.Clamp(v / max, 0, 1) * (h - 2) - 1);
            if (line.Points.Count == 0) area.Points.Add(new Point(point.X, h));
            line.Points.Add(point); area.Points.Add(point); lastX = point.X;
        }
        area.Points.Add(new Point(lastX, h));
        var tone = (string)canvas.Tag;
        line.SetResourceReference(Shape.StrokeProperty, tone + "Brush");
        area.SetResourceReference(Shape.FillProperty, tone + "SoftBrush");
        canvas.Children.Add(area);
        canvas.Children.Add(line);
    }

    /// <summary>Os blocos da página entram um após o outro, subindo e aparecendo (efeito cascata).</summary>
    private void AnimatePageIn()
    {
        if (!IsVisible || !AppearanceService.AnimationsEnabled) return; // janela não exibida, teste ou animações desligadas
        if (ContentHost.Children.Count == 0 || ContentHost.Children[0] is not Panel page) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var index = 0;
        foreach (UIElement block in page.Children)
        {
            if (index >= 10) break; // o que está fora da tela não precisa de animação
            // Transições curtas (150–250 ms): a página aparece rápido e a cascata só sugere a ordem
            var delay = TimeSpan.FromMilliseconds(index++ * 35);
            // Reaproveita o deslocamento do efeito de "subir ao passar o mouse" dos cartões
            var slide = block.RenderTransform as TranslateTransform ?? new TranslateTransform();
            block.RenderTransform = slide;
            block.Opacity = 0; // invisível até a vez dele na cascata
            block.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = delay, EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(250)) { BeginTime = delay, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }
    }

    private void UpdateGameModeBadge()
    {
        var session = GamingService.ActiveSession();
        NavGamingBadge.Visibility = session is null ? Visibility.Collapsed : Visibility.Visible;
        var pending = session?.RecoveryPending == true;
        NavGamingBadge.SetResourceReference(Border.BackgroundProperty, pending ? "WarningSoftBrush" : "SuccessSoftBrush");
        if (NavGamingBadge.Child is TextBlock label)
        {
            label.Text = Translator.Tr(pending ? "Pendente" : "ATIVO");
            label.SetResourceReference(TextBlock.ForegroundProperty, pending ? "WarningBrush" : "SuccessBrush");
        }
    }
}
