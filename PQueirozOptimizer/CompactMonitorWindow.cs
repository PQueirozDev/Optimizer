using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PQueirozOptimizer.BiosAdvisor.Detector;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Modo compacto (fase 13): janela pequena e externa para um segundo monitor, com CPU, GPU, RAM, temperatura da
/// GPU, perfil e Modo Jogo. Não é um overlay dentro do jogo: não injeta nada em processos (seguro com anti-cheat).
/// Clique direito: sempre no topo, opacidade e fechar. A posição fica salva.
/// </summary>
public sealed class CompactMonitorWindow : Window
{
    private sealed class Prefs { public double Left { get; set; } = double.NaN; public double Top { get; set; } = double.NaN; public bool Topmost { get; set; } = true; public double Opacity { get; set; } = 0.95; }
    private static readonly string PrefsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "compact.json");

    private readonly TextBlock _cpu = Value(), _gpu = Value(), _ram = Value(), _temp = Value(), _fps = Value(), _profile = Small(), _game = Small();
    private readonly ProgressBar _cpuBar = Bar(), _gpuBar = Bar(), _ramBar = Bar();
    private readonly Func<double?> _liveFps;
    private readonly Func<string> _profileName;
    private int _tick;

    public CompactMonitorWindow(Func<double?> liveFps, Func<string> profileName)
    {
        _liveFps = liveFps; _profileName = profileName;
        Title = "Qrztweaks · Modo compacto";
        Width = 270; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true;
        AllowsTransparency = true; Background = Brushes.Transparent;
        var prefs = Load();
        Topmost = prefs.Topmost; Opacity = Math.Clamp(prefs.Opacity, 0.5, 1);
        if (!double.IsNaN(prefs.Left) && !double.IsNaN(prefs.Top) && OnScreen(prefs.Left, prefs.Top)) { WindowStartupLocation = WindowStartupLocation.Manual; Left = prefs.Left; Top = prefs.Top; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        var row = 0;
        void Line(string label, UIElement middle, TextBlock value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            var l = Small(); l.Text = Translator.Tr(label); l.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(l, row); grid.Children.Add(l);
            Grid.SetRow(middle, row); Grid.SetColumn(middle, 1); grid.Children.Add(middle);
            Grid.SetRow(value, row); Grid.SetColumn(value, 2); grid.Children.Add(value);
            row++;
        }
        Line("CPU", _cpuBar, _cpu);
        Line("GPU", _gpuBar, _gpu);
        Line("RAM", _ramBar, _ram);
        Line("Temp.", new Border(), _temp);
        Line("FPS", new Border(), _fps);
        var footer = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        footer.Children.Add(_profile); footer.Children.Add(_game);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(footer, row); Grid.SetColumnSpan(footer, 3); grid.Children.Add(footer);

        var card = new Border { Child = grid, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Content = card;
        SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        LocationChanged += (_, _) => Save();
        ContextMenu = Menu();

        Action<HardwareSample> onSample = s => Dispatcher.BeginInvoke(() => Update(s));
        HardwareMonitorService.Shared.Sampled += onSample;
        Closed += (_, _) => { HardwareMonitorService.Shared.Sampled -= onSample; Save(); };
        if (HardwareMonitorService.Shared.Latest is { } latest) Update(latest);
    }

    private ContextMenu Menu()
    {
        var menu = new ContextMenu();
        var top = new MenuItem { Header = Translator.Tr("Sempre no topo"), IsCheckable = true, IsChecked = Topmost };
        top.Click += (_, _) => { Topmost = top.IsChecked; Save(); };
        menu.Items.Add(top);
        foreach (var o in new[] { 1.0, 0.85, 0.7 })
        {
            var item = new MenuItem { Header = string.Format(Translator.Tr("Opacidade {0}%"), (int)(o * 100)) };
            item.Click += (_, _) => { Opacity = o; Save(); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var close = new MenuItem { Header = Translator.Tr("Fechar") };
        close.Click += (_, _) => Close();
        menu.Items.Add(close);
        return menu;
    }

    private void Update(HardwareSample s)
    {
        _cpuBar.Value = s.Cpu; _cpu.Text = $"{s.Cpu:0}%";
        _gpuBar.Value = s.Gpu ?? 0; _gpu.Text = s.Gpu is { } g ? $"{g:0}%" : "--";
        _ramBar.Value = s.Ram; _ram.Text = $"{s.Ram:0}%";
        _fps.Text = _liveFps() is { } f ? $"{f:0}" : "--";
        _fps.ToolTip = Translator.Tr("FPS só aparece durante uma captura do Performance Lab com o PresentMon.");
        _profile.Text = Translator.Tr("Perfil: ") + _profileName();
        var session = GamingService.ActiveSession();
        _game.Text = Translator.Tr(session is null ? "Modo Jogo desligado" : session.RecoveryPending ? "Modo Jogo: restauração pendente" : "Modo Jogo ativo");
        // Temperatura da GPU a cada 3 s (o nvidia-smi custa mais que os contadores)
        if (_tick++ % 3 != 0) return;
        _ = Task.Run(() =>
        {
            try { return NvidiaSmi.Available ? NvidiaSmi.Double(NvidiaSmi.Query("temperature.gpu").FirstOrDefault()?[0]) : null; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IndexOutOfRangeException) { return null; }
        }).ContinueWith(t => Dispatcher.BeginInvoke(() => { _temp.Text = t.Result is { } c ? $"{c:0} °C" : "--"; _temp.ToolTip = t.Result is null ? Translator.Tr("Sem sensor de temperatura disponível (só GPUs NVIDIA pelo driver).") : null; }), TaskScheduler.Default);
    }

    private static bool OnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 50 && top >= SystemParameters.VirtualScreenTop - 50 &&
        left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 && top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60;

    private static Prefs Load()
    {
        try { return File.Exists(PrefsPath) ? JsonSerializer.Deserialize<Prefs>(File.ReadAllText(PrefsPath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    private void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath)!); File.WriteAllText(PrefsPath, JsonSerializer.Serialize(new Prefs { Left = Left, Top = Top, Topmost = Topmost, Opacity = Opacity })); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static TextBlock Value()
    {
        var t = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = "--", Tag = Translator.SystemDataTag };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return t;
    }

    private static TextBlock Small()
    {
        var t = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return t;
    }

    private static ProgressBar Bar() => new() { Height = 5, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
}
