using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PQueirozOptimizer;

/// <summary>
/// Tela de abertura. Roda numa thread própria, então continua animada enquanto a thread principal
/// monta a janela, e mostra as etapas reais da inicialização. Com as animações ligadas, toca uma
/// sequência curta (logo, nome letra a letra e anel de progresso) e fica pelo menos <see cref="MinimumDuration"/>;
/// com elas desligadas, fecha assim que a janela principal desenha o primeiro quadro.
/// </summary>
public static class StartupSplash
{
    /// <summary>Cores do tema ativo, copiadas como valores: os pincéis do app pertencem à thread principal.</summary>
    public sealed record Palette(Color Background, Color Card, Color Border, Color Text, Color Muted, Color Accent, bool Animations);

    /// <summary>Tempo mínimo na tela com as animações ligadas: o suficiente para a sequência de entrada terminar.</summary>
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(2400);

    // Estado compartilhado entre a thread principal e a da tela de abertura (sempre sob Gate)
    private static readonly object Gate = new();
    private static bool _started, _closeRequested;
    private static Dispatcher? _dispatcher;
    private static string? _pendingStatus;
    private static double _pendingProgress;
    private static readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Só usados na thread da tela de abertura
    private static Window? _window;
    private static TextBlock? _status;
    private static Ellipse? _ring;
    private static Border? _logo;
    private static double _ringLength;
    private static double _shownProgress;
    private static readonly Stopwatch Clock = new();

    /// <summary>A tela de abertura foi aberta e ainda não sumiu.</summary>
    public static bool Active { get { lock (Gate) return _started && !Closed.Task.IsCompleted; } }

    public static Palette CurrentPalette()
    {
        var resources = Application.Current.Resources;
        Color C(string key, Color fallback) => resources[key] is SolidColorBrush b ? b.Color with { A = 255 } : fallback;
        return new Palette(C("BackgroundBrush", Color.FromRgb(0x0B, 0x0B, 0x14)), C("CardBgBrush", Color.FromRgb(0x13, 0x13, 0x1F)),
            C("BorderBrush", Color.FromRgb(0x2A, 0x2A, 0x3A)), C("TextBrush", Colors.White), C("MutedBrush", Color.FromRgb(0x9C, 0xA3, 0xAF)),
            C("AccentBrush", Color.FromRgb(0x8B, 0x5C, 0xF6)), Services.AppearanceService.AnimationsEnabled);
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
                if (_closeRequested) { Closed.TrySetResult(); return; }
                _window = window;
                _dispatcher = Dispatcher.CurrentDispatcher;
            }
            Clock.Start();
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
        if (_window?.Tag is not true) SetRing(progress);
        if (_status is null || status is null || _status.Text == status) return;
        if (_window?.Tag is not true) { _status.Text = status; return; }
        // Troca de etapa com um fade curto: o texto antigo sai, o novo entra
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
        fadeOut.Completed += (_, _) =>
        {
            _status.Text = status;
            _status.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        };
        _status.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Pede para fechar. Com as animações ligadas, espera completar o tempo mínimo, fecha o anel e sai;
    /// <paramref name="immediate"/> pula a espera (quando outra janela, como a ativação, precisa aparecer já).
    /// A tarefa termina quando a tela de abertura sumiu.
    /// </summary>
    public static Task Close(bool immediate = false)
    {
        Dispatcher? dispatcher;
        lock (Gate)
        {
            if (!_started) return Task.CompletedTask;
            if (_closeRequested) return Closed.Task;
            _closeRequested = true;
            dispatcher = _dispatcher;
            _dispatcher = null;
        }
        if (dispatcher is null) return Closed.Task; // ainda montando: a thread vê o pedido e encerra sozinha
        dispatcher.BeginInvoke(() =>
        {
            void Finish()
            {
                _window?.Close();
                Closed.TrySetResult();
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
            }
            if (_window is not { } window || window.Tag is not true || immediate) { Finish(); return; }

            var wait = MinimumDuration - Clock.Elapsed;
            var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(1) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                lock (Gate) { _pendingStatus = Services.Translator.Tr("Pronto"); _pendingProgress = 1; }
                ApplyPending();
                PlayExit(window, Finish);
            };
            timer.Start();
        });
        return Closed.Task;
    }

    private static SolidColorBrush Frozen(Color color, double opacity = 1)
    {
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static DoubleAnimation Ease(double? from, double to, double ms, double beginMs = 0, IEasingFunction? easing = null) =>
        new(to, TimeSpan.FromMilliseconds(ms)) { From = from, BeginTime = TimeSpan.FromMilliseconds(beginMs), EasingFunction = easing ?? new CubicEase { EasingMode = EasingMode.EaseOut } };

    private static Window Build(Palette p, string version, string firstStatus)
    {
        const double width = 480, height = 300, ringSize = 104, ringStroke = 3;
        var window = new Window
        {
            Width = width + 60, Height = height + 60, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Title = "Qrztweaks", Tag = p.Animations,
        };
        var accent = Frozen(p.Accent);

        var card = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(16), Background = Frozen(p.Card), BorderBrush = Frozen(p.Border), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.4, Color = Colors.Black },
        };
        var layers = new Grid { ClipToBounds = true };
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36, 0, 36, 6) };

        // Logo dentro de um anel: o anel é a barra de progresso da abertura
        var logoHost = new Grid { Width = ringSize, Height = ringSize, HorizontalAlignment = HorizontalAlignment.Center };
        var track = new Ellipse { Stroke = Frozen(p.Border), StrokeThickness = ringStroke };
        logoHost.Children.Add(track);
        _ringLength = Math.PI * (ringSize - ringStroke) / ringStroke;
        _ring = new Ellipse
        {
            Stroke = accent, StrokeThickness = ringStroke, StrokeDashCap = PenLineCap.Round, StrokeDashArray = new DoubleCollection { _ringLength, _ringLength },
            StrokeDashOffset = _ringLength, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-90),
        };
        logoHost.Children.Add(_ring);
        var logo = _logo = new Border { Width = 68, Height = 68, RenderTransformOrigin = new Point(0.5, 0.5) }; // o logo já tem o próprio bloco arredondado
        try
        {
            var image = new BitmapImage();
            image.BeginInit(); image.UriSource = new Uri("pack://application:,,,/Qrztweaks;component/Assets/app.png"); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 192; image.EndInit(); image.Freeze();
            logo.Child = new Image { Source = image, Width = 68, Height = 68 };
            RenderOptions.SetBitmapScalingMode(logo.Child, BitmapScalingMode.HighQuality);
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException) { }
        logoHost.Children.Add(logo);
        content.Children.Add(logoHost);

        // Nome letra a letra (cada letra é um bloco para poder entrar com atraso próprio)
        var word = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
        var display = new FontFamily("Segoe UI Variable Display, Segoe UI");
        foreach (var letter in "Qrztweaks")
            word.Children.Add(new TextBlock { Text = letter.ToString(), FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Frozen(p.Text), FontFamily = display, RenderTransform = new TranslateTransform() });
        content.Children.Add(word);
        var subtitle = new TextBlock { Text = "P C   O P T I M I Z E R", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 22), RenderTransform = new TranslateTransform() };
        content.Children.Add(subtitle);

        _status = new TextBlock { Text = firstStatus, FontSize = 12.5, Foreground = Frozen(p.Muted), HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(_status);
        layers.Children.Add(content);

        layers.Children.Add(new TextBlock { Text = "v" + version, FontSize = 11, Foreground = Frozen(p.Muted, 0.8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 16, 12) });
        card.Child = layers;
        window.Content = card;

        if (!p.Animations)
        {
            // Sem animações: tudo no lugar; o anel acompanha as etapas sem transição (em ApplyPending)
            return window;
        }

        // Sequência de entrada (em ms): cartão → logo → anel → letras → subtítulo → etapa
        window.Opacity = 0;
        window.BeginAnimation(UIElement.OpacityProperty, Ease(0, 1, 220));
        var cardScale = new ScaleTransform(0.94, 0.94);
        card.RenderTransformOrigin = new Point(0.5, 0.5); card.RenderTransform = cardScale;
        var pop = Ease(0.94, 1, 520, 0, new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut });
        cardScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop); cardScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

        var logoScale = new ScaleTransform(0.55, 0.55);
        var logoTurn = new RotateTransform(-14);
        logo.RenderTransform = new TransformGroup { Children = { logoScale, logoTurn } };
        logo.Opacity = 0;
        var back = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
        logo.BeginAnimation(UIElement.OpacityProperty, Ease(0, 1, 260, 160));
        logoScale.BeginAnimation(ScaleTransform.ScaleXProperty, Ease(0.55, 1, 620, 160, back));
        logoScale.BeginAnimation(ScaleTransform.ScaleYProperty, Ease(0.55, 1, 620, 160, back));
        logoTurn.BeginAnimation(RotateTransform.AngleProperty, Ease(-14, 0, 700, 160));
        track.Opacity = 0;
        track.BeginAnimation(UIElement.OpacityProperty, Ease(0, 1, 400, 380));

        var delay = 520.0;
        foreach (TextBlock letter in word.Children)
        {
            letter.Opacity = 0;
            letter.BeginAnimation(UIElement.OpacityProperty, Ease(0, 1, 280, delay));
            ((TranslateTransform)letter.RenderTransform).BeginAnimation(TranslateTransform.YProperty, Ease(12, 0, 380, delay));
            delay += 50;
        }
        subtitle.Opacity = 0;
        subtitle.BeginAnimation(UIElement.OpacityProperty, Ease(0, 1, 360, delay + 80));
        ((TranslateTransform)subtitle.RenderTransform).BeginAnimation(TranslateTransform.YProperty, Ease(8, 0, 420, delay + 80));
        _status.Opacity = 0;
        _status.BeginAnimation(UIElement.OpacityProperty, Ease(0, 1, 300, delay + 260));

        // O anel avança suavemente a cada quadro rumo à etapa atual
        CompositionTarget.Rendering += (_, _) => SetRing(TargetProgress());
        return window;
    }

    /// <summary>
    /// Progresso mostrado no anel: a etapa real informada e, com animações, nunca abaixo do tempo já
    /// passado da sequência (até 90%), para o anel não ficar parado enquanto a interface monta.
    /// </summary>
    private static double TargetProgress()
    {
        double reported;
        lock (Gate) reported = _pendingProgress;
        var byTime = Math.Min(0.9, Clock.Elapsed.TotalMilliseconds / MinimumDuration.TotalMilliseconds * 0.9);
        var target = Math.Max(reported, byTime);
        _shownProgress += (target - _shownProgress) * 0.12; // aproximação suave, sem saltos
        return _shownProgress;
    }

    private static void SetRing(double progress)
    {
        if (_ring is null) return;
        _ring.StrokeDashOffset = _ringLength * (1 - Math.Clamp(progress, 0, 1));
    }

    /// <summary>Saída: o anel fecha, o logo dá um pulso e o cartão some crescendo de leve.</summary>
    private static void PlayExit(Window window, Action finish)
    {
        var card = (Border)window.Content;
        if (_logo?.RenderTransform is TransformGroup { Children: [ScaleTransform logoScale, ..] })
        {
            var pulse = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(260) };
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
            logoScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            logoScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
        if (card.RenderTransform is ScaleTransform cardScale)
        {
            var grow = Ease(null, 1.03, 280, 620, new CubicEase { EasingMode = EasingMode.EaseIn });
            cardScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow); cardScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        var fade = Ease(null, 0, 280, 620, new CubicEase { EasingMode = EasingMode.EaseIn });
        fade.Completed += (_, _) => finish();
        window.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
