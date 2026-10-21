using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Preferências do overlay: canto da tela, tamanho do texto e quais linhas aparecem.</summary>
public sealed class OverlaySettings
{
    public string Corner { get; set; } = "TopLeft";
    public double Scale { get; set; } = 1.0;
    public bool ShowFrameTime { get; set; } = true;
    public bool ShowLow { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowClock { get; set; }

    private static string PathFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "overlay.json");

    public static OverlaySettings Load()
    {
        try { return File.Exists(PathFile) ? JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(PathFile)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile)!); File.WriteAllText(PathFile, JsonSerializer.Serialize(this)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Overlay de FPS por cima do jogo: janela transparente, sempre no topo, que não recebe clique nem foco (o mouse
/// passa direto para o jogo). Não injeta nada no processo do jogo, então é seguro com anti-cheat; por isso só
/// aparece em tela cheia sem bordas ou janela (a tela cheia exclusiva fica por cima de qualquer janela).
/// O FPS vem do <see cref="FrameMonitor"/> (ETW) e o resto do monitor ao vivo.
/// </summary>
public sealed class GameOverlayWindow : Window
{
    private readonly OverlaySettings _settings;
    private readonly TextBlock _fps = Line(22, bold: true), _detail = Line(12), _system = Line(12);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private HardwareSample? _sample;
    private IntPtr _monitor;

    public GameOverlayWindow(OverlaySettings settings)
    {
        _settings = settings;
        Title = "Qrztweaks · Overlay";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        var stack = new StackPanel();
        stack.Children.Add(_fps); stack.Children.Add(_detail); stack.Children.Add(_system);
        // Preto translúcido e texto branco em qualquer tema: precisa ser legível sobre o jogo, não seguir o app
        var card = new Border
        {
            Child = stack, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 14, 9),
            Background = new SolidColorBrush(Color.FromArgb(0xB8, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            LayoutTransform = new ScaleTransform(Math.Clamp(settings.Scale, 0.7, 2), Math.Clamp(settings.Scale, 0.7, 2)),
        };
        Content = card;
        FontFamily = new FontFamily("Cascadia Mono, Consolas");

        Action<HardwareSample> onSample = s => _sample = s;
        HardwareMonitorService.Shared.Sampled += onSample;
        FrameMonitor.Shared.Start();
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { MakeClickThrough(); Refresh(); _timer.Start(); };
        SizeChanged += (_, _) => Place();
        Closed += (_, _) => { _timer.Stop(); HardwareMonitorService.Shared.Sampled -= onSample; FrameMonitor.Shared.Stop(); };
    }

    private static TextBlock Line(double size, bool bold = false) => new()
    {
        FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Tag = Translator.SystemDataTag,
        Foreground = new SolidColorBrush(bold ? Colors.White : Color.FromRgb(0xD0, 0xD0, 0xD0)),
    };

    private void Refresh()
    {
        var frames = FrameMonitor.Shared.GetForegroundStats();
        _fps.Text = frames is { } f ? $"{f.Fps:0} FPS" : "— FPS";
        var detail = new List<string>();
        if (frames is { } fr)
        {
            if (_settings.ShowFrameTime) detail.Add($"{fr.FrameTimeMs:0.0} ms");
            if (_settings.ShowLow) detail.Add($"1% {fr.Low1Fps:0}");
            detail.Add(fr.ProcessName);
        }
        else detail.Add(FrameMonitor.Shared.Error is { } error ? error : Translator.IsEnglish ? "waiting for a DirectX game" : "aguardando um jogo DirectX");
        _detail.Text = string.Join("  ·  ", detail);
        var system = new List<string>();
        if (_sample is { } s)
        {
            if (_settings.ShowCpu) system.Add($"CPU {s.Cpu:0}%" + (_settings.ShowClock && s.CpuMhz is { } mhz ? $" {mhz / 1000:0.0}GHz" : ""));
            if (_settings.ShowGpu && s.Gpu is { } gpu) system.Add($"GPU {gpu:0}%" + (s.GpuTempC is { } t ? $" {t:0}°C" : ""));
            if (_settings.ShowRam) system.Add($"RAM {s.Ram:0}%");
        }
        _system.Text = string.Join("  ·  ", system);
        _system.Visibility = system.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // A cada tick volta para o topo e para a tela do jogo: o jogo em tela cheia pode ter passado na frente,
        // trocado a resolução ou estar em outro monitor
        Place(followForeground: frames is not null);
    }

    /// <summary>Jogo em tela cheia exclusiva (Direct3D): nenhuma janela aparece por cima, só overlays injetados.</summary>
    public static bool IsExclusiveFullscreen() => SHQueryUserNotificationState(out var state) == 0 && state == QunsRunningD3DFullScreen;

    private void Place(bool followForeground = false)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var own)) return;
        // Segue o monitor do jogo em primeiro plano (só quando ele está desenhando quadros): clicar no navegador
        // em outro monitor não leva o overlay junto
        var foreground = followForeground ? GetForegroundWindow() : IntPtr.Zero;
        if (foreground != IntPtr.Zero && GetWindowThreadProcessId(foreground, out var pid) != 0 && pid != Environment.ProcessId)
            _monitor = MonitorFromWindow(foreground, MonitorDefaultToPrimary);
        if (_monitor == IntPtr.Zero) _monitor = MonitorFromWindow(hwnd, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(_monitor, ref info)) { _monitor = IntPtr.Zero; return; }

        // Tela cheia sem bordas cobre a barra de tarefas: usa o monitor inteiro, não só a área de trabalho.
        // Tudo em pixels do monitor, lido na hora: o jogo pode trocar a resolução ao abrir
        var margin = (int)Math.Round(16 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        int width = own.Right - own.Left, height = own.Bottom - own.Top;
        var right = _settings.Corner.EndsWith("Right", StringComparison.Ordinal);
        var bottom = _settings.Corner.StartsWith("Bottom", StringComparison.Ordinal);
        var x = right ? info.Monitor.Right - width - margin : info.Monitor.Left + margin;
        var y = bottom ? info.Monitor.Bottom - height - margin : info.Monitor.Top + margin;
        SetWindowPos(hwnd, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        // Transparente ao mouse, sem foco, fora do Alt+Tab
        style |= WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
        Place();
    }

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20, WsExLayered = 0x80000, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001, SwpNoActivate = 0x0010, SwpShowWindow = 0x0040, MonitorDefaultToPrimary = 1;
    private const int QunsRunningD3DFullScreen = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
