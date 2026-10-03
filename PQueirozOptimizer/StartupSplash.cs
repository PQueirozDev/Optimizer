using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PQueirozOptimizer;

/// <summary>
/// Tela de abertura. Roda numa thread própria, então continua animada enquanto a thread principal
/// monta a janela, e mostra as etapas reais da inicialização. Fecha assim que a janela principal
/// desenha o primeiro quadro: não há tempo mínimo nem espera artificial.
/// </summary>
public static class StartupSplash
{
    /// <summary>Cores do tema ativo, copiadas como valores: os pincéis do app pertencem à thread principal.</summary>
    public sealed record Palette(Color Background, Color Card, Color Border, Color Text, Color Muted, Color AccentA, Color AccentB, bool Animations);

    // Estado compartilhado entre a thread principal e a da tela de abertura (sempre sob Gate)
    private static readonly object Gate = new();
    private static bool _started, _closeRequested;
    private static Dispatcher? _dispatcher;
    private static string? _pendingStatus;
    private static double _pendingProgress;

    // Só usados na thread da tela de abertura
    private static Window? _window;
    private static TextBlock? _status;
    private static Border? _bar;
    private static double _barWidth;

    public static Palette CurrentPalette()
    {
        var resources = Application.Current.Resources;
        Color C(string key, Color fallback) => resources[key] is SolidColorBrush b ? b.Color : fallback;
        var accent = resources["AccentGradientBrush"] is LinearGradientBrush g && g.GradientStops.Count >= 2
            ? (g.GradientStops[0].Color, g.GradientStops[^1].Color)
            : (Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x60, 0xA5, 0xFA));
        return new Palette(C("BackgroundBrush", Color.FromRgb(0x0B, 0x0B, 0x14)), C("CardBgBrush", Color.FromRgb(0x13, 0x13, 0x1F)),
            C("BorderBrush", Color.FromRgb(0x2A, 0x2A, 0x3A)), C("TextBrush", Colors.White), C("MutedBrush", Color.FromRgb(0x9C, 0xA3, 0xAF)),
            accent.Item1, accent.Item2, Services.AppearanceService.AnimationsEnabled);
    }

    /// <summary>Abre a tela de abertura sem bloquear: ela é montada na própria thread, em paralelo à inicialização.</summary>
    public static void Show(Palette palette, string version, string firstStatus)
    {
        lock (Gate)
        {
            if (_started) return;
            _started = true;
            _pendingStatus = firstStatus;
        }
        var thread = new Thread(() =>
        {
            Services.StartupProfiler.Mark("splash-thread");
            var window = Build(palette, version, firstStatus);
            Services.StartupProfiler.Mark("splash-built");
            lock (Gate)
            {
                // A janela principal ficou pronta antes da tela de abertura: ela nem aparece
                if (_closeRequested) return;
                _window = window;
                _dispatcher = Dispatcher.CurrentDispatcher;
            }
            window.Show();
            Services.StartupProfiler.Mark("splash-visible");
            ApplyPending();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Splash" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>Etapa atual e quanto já foi feito (0 a 1).</summary>
    public static void Report(string status, double progress)
    {
        Dispatcher? dispatcher;
        lock (Gate) { _pendingStatus = status; _pendingProgress = progress; dispatcher = _dispatcher; }
        dispatcher?.BeginInvoke(ApplyPending);
    }

    private static void ApplyPending()
    {
        string? status; double progress;
        lock (Gate) { status = _pendingStatus; progress = _pendingProgress; }
        if (_status is null || _bar is null) return;
        if (status != null) _status.Text = status;
        var target = _barWidth * Math.Clamp(progress, 0, 1);
        if (_window?.Tag is true) _bar.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else _bar.Width = target;
    }

    /// <summary>Some (com uma saída curta, se as animações estiverem ligadas) e encerra a thread.</summary>
    public static void Close()
    {
        Dispatcher? dispatcher;
        lock (Gate)
        {
            if (_closeRequested) return;
            _closeRequested = true;
            dispatcher = _dispatcher;
            _dispatcher = null;
        }
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(() =>
        {
            if (_window is not { } window) { dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); return; }
            void Finish() { window.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }
            if (window.Tag is not true) { Finish(); return; }
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => Finish();
            window.BeginAnimation(UIElement.OpacityProperty, fade);
        });
    }

    private static SolidColorBrush Frozen(Color color, double opacity = 1)
    {
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static Window Build(Palette p, string version, string firstStatus)
    {
        const double width = 420, height = 248;
        var window = new Window
        {
            Width = width + 48, Height = height + 48, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Title = "PQueiroz Optimizer", Tag = p.Animations,
        };
        var accent = new LinearGradientBrush(p.AccentA, p.AccentB, 0); accent.Freeze();

        var card = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(22), Background = Frozen(p.Card), BorderBrush = Frozen(p.Border), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 8, Opacity = 0.45, Color = Colors.Black },
        };
        var layers = new Grid { ClipToBounds = true };
        // Brilho do roxo no topo, como o fundo da janela principal
        var glow = new System.Windows.Shapes.Ellipse { Width = 360, Height = 220, Margin = new Thickness(0, -150, 0, 0), VerticalAlignment = VerticalAlignment.Top, Fill = Frozen(p.AccentA, 0.22), Effect = new BlurEffect { Radius = 70 } };
        layers.Children.Add(glow);

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36, 0, 36, 0) };
        var logoHost = new Grid { Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center };
        var halo = new Border { CornerRadius = new CornerRadius(22), Background = accent, Opacity = 0.55, Effect = new BlurEffect { Radius = 22 } };
        logoHost.Children.Add(halo);
        var logo = new Border { CornerRadius = new CornerRadius(22), Background = accent };
        var image = new BitmapImage();
        try
        {
            image.BeginInit(); image.UriSource = new Uri("pack://application:,,,/PQueirozOptimizer;component/Assets/app.png"); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 96; image.EndInit(); image.Freeze();
            logo.Child = new Image { Source = image, Width = 44, Height = 44 };
            RenderOptions.SetBitmapScalingMode(logo.Child, BitmapScalingMode.HighQuality);
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException) { }
        logoHost.Children.Add(logo);
        content.Children.Add(logoHost);

        content.Children.Add(new TextBlock { Text = "PQueiroz", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Frozen(p.Text), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0), FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });
        content.Children.Add(new TextBlock { Text = "O P T I M I Z E R", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Frozen(p.Muted), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 22) });

        _status = new TextBlock { Text = firstStatus, FontSize = 12, Foreground = Frozen(p.Muted), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
        content.Children.Add(_status);
        _barWidth = width - 72;
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Frozen(p.Border), Width = _barWidth, HorizontalAlignment = HorizontalAlignment.Center };
        _bar = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = accent, Width = 0, HorizontalAlignment = HorizontalAlignment.Left };
        track.Child = _bar;
        content.Children.Add(track);
        layers.Children.Add(content);

        layers.Children.Add(new TextBlock { Text = "v" + version, FontSize = 10.5, Foreground = Frozen(p.Muted, 0.8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 16, 12) });
        card.Child = layers;
        window.Content = card;

        if (p.Animations)
        {
            // Entrada curta e o halo do logo "respirando" enquanto carrega
            window.Opacity = 0;
            window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            var scale = new ScaleTransform(0.97, 0.97);
            card.RenderTransformOrigin = new Point(0.5, 0.5); card.RenderTransform = scale;
            var pop = new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop); scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            halo.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.3, 0.8, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        }
        return window;
    }
}
