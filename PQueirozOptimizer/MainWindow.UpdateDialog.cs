using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Janela de atualização dentro do app: versão atual → nova, novidades da release, progresso do download
/// com as etapas (baixar, verificar, instalar) e os botões Atualizar agora / Depois.
/// </summary>
public partial class MainWindow
{
    private ProgressBar? _updateProgress;
    private TextBlock? _updateStatus;
    private Button? _updateNow, _updateLater;

    private void ShowUpdateDialog(UpdateInfo info)
    {
        if (UpdateLayer.Visibility == Visibility.Visible) return;
        UpdateLayer.Children.Clear();
        var shade = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x02, 0x03, 0x08)) };
        shade.MouseLeftButtonDown += (_, _) => { if (!_installingUpdate) CloseUpdateDialog(); };
        UpdateLayer.Children.Add(shade);

        var card = new Border { Width = 520, CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        card.Effect = new DropShadowEffect { BlurRadius = 48, ShadowDepth = 12, Opacity = 0.55, Color = Colors.Black };
        var layers = new Grid();
        var glow = new Border { Height = 160, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        glow.SetResourceReference(Border.BackgroundProperty, "GlowBrush");
        layers.Children.Add(glow);

        var body = new StackPanel { Margin = new Thickness(28, 26, 28, 24) };
        // Cabeçalho: ícone com brilho, título e versão atual → nova
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var icon = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(16), Child = GlyphIcon(Glyphs.Download, 22, "OnAccentBrush") };
        icon.SetResourceReference(Border.BackgroundProperty, "AccentGradientBrush");
        icon.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 0, Opacity = 0.6, Color = (Color)(Application.Current.Resources["AccentColor"] ?? Colors.MediumPurple) };
        DockPanel.SetDock(icon, Dock.Left); head.Children.Add(icon);
        var titles = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = Label("Nova versão disponível", 19); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0);
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        titles.Children.Add(title);
        var versions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var from = Pill("v" + info.CurrentVersion.TrimStart('v'), "Info"); from.Margin = new Thickness(0);
        var arrow = GlyphIcon(Glyphs.ChevronRight, 11, "MutedBrush"); arrow.Margin = new Thickness(8, 0, 8, 0);
        var to = Pill("v" + info.LatestVersion.TrimStart('v'), "Success"); to.Margin = new Thickness(0);
        versions.Children.Add(from); versions.Children.Add(arrow); versions.Children.Add(to);
        titles.Children.Add(versions);
        head.Children.Add(titles);
        body.Children.Add(head);

        // Novidades
        if (info.Notes is { Count: > 0 } notes)
        {
            var whatsNew = Label("O que há de novo", 13); whatsNew.FontWeight = FontWeights.SemiBold; whatsNew.Margin = new Thickness(0, 0, 0, 8);
            body.Children.Add(whatsNew);
            var list = new StackPanel();
            foreach (var note in notes)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
                var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(2, 6, 10, 0), VerticalAlignment = VerticalAlignment.Top };
                dot.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                DockPanel.SetDock(dot, Dock.Left); row.Children.Add(dot);
                var text = Label(note, 12.5, true); text.Margin = new Thickness(0); text.TextWrapping = TextWrapping.Wrap;
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                row.Children.Add(text);
                list.Children.Add(row);
            }
            var scroll = new ScrollViewer { Content = list, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 16) };
            body.Children.Add(scroll);
        }

        // Progresso (aparece ao atualizar)
        var progressArea = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 16) };
        _updateStatus = Label("Baixando instalador...", 12.5, true); _updateStatus.Margin = new Thickness(0, 0, 0, 8);
        _updateProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6, IsIndeterminate = true };
        progressArea.Children.Add(_updateStatus); progressArea.Children.Add(_updateProgress);
        body.Children.Add(progressArea);

        var safety = Label(UpdateService.CanAutoInstall(info)
            ? "O instalador é verificado por SHA256 antes de rodar e o app reabre sozinho ao terminar. Suas configurações e backups são mantidos."
            : "Esta versão é baixada pela página de releases.", 11.5, true);
        safety.Margin = new Thickness(0, 0, 0, 18);
        body.Children.Add(safety);

        var actions = new DockPanel { LastChildFill = false };
        _updateNow = IconButton(Glyphs.Download, UpdateService.CanAutoInstall(info) ? "Atualizar agora" : "Abrir download", primary: true);
        _updateNow.Margin = new Thickness(8, 0, 0, 0);
        _updateLater = IconButton(Glyphs.Clock, "Depois");
        _updateLater.Margin = new Thickness(0);
        var page = IconButton(Glyphs.OpenInNew, "Ver no GitHub");
        page.Margin = new Thickness(0); page.SetResourceReference(StyleProperty, "GhostButton");
        page.Click += (_, _) => OpenUrl(info.DownloadUrl);
        DockPanel.SetDock(_updateNow, Dock.Right); DockPanel.SetDock(_updateLater, Dock.Right); DockPanel.SetDock(page, Dock.Left);
        actions.Children.Add(_updateNow); actions.Children.Add(_updateLater); actions.Children.Add(page);
        body.Children.Add(actions);
        _updateLater.Click += (_, _) => CloseUpdateDialog();
        _updateNow.Click += async (_, _) =>
        {
            if (!UpdateService.CanAutoInstall(info)) { OpenUrl(info.DownloadUrl); CloseUpdateDialog(); return; }
            progressArea.Visibility = Visibility.Visible;
            await InstallUpdateAsync(info, _updateNow);
        };

        layers.Children.Add(body);
        card.Child = layers;
        UpdateLayer.Children.Add(card);
        UpdateLayer.Visibility = Visibility.Visible;
        UpdateLayer.Focusable = true;
        UpdateLayer.Focus();
        UpdateLayer.PreviewKeyDown += UpdateLayerKey;

        if (AppearanceService.AnimationsEnabled)
        {
            shade.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            var scale = new ScaleTransform(0.94, 0.94); var move = new TranslateTransform(0, 14);
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            card.RenderTransform = new TransformGroup { Children = { scale, move } };
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            // O ícone "respira" para chamar atenção sem piscar
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            var pulse = new ScaleTransform(1, 1); icon.RenderTransform = pulse;
            var breathe = new DoubleAnimation(1, 1.07, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            pulse.BeginAnimation(ScaleTransform.ScaleXProperty, breathe); pulse.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
        }
    }

    private void UpdateLayerKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_installingUpdate) { CloseUpdateDialog(); e.Handled = true; }
    }

    private void CloseUpdateDialog()
    {
        UpdateLayer.PreviewKeyDown -= UpdateLayerKey;
        void Hide() { UpdateLayer.Visibility = Visibility.Collapsed; UpdateLayer.Children.Clear(); UpdateLayer.BeginAnimation(OpacityProperty, null); UpdateLayer.Opacity = 1; }
        _updateProgress = null; _updateStatus = null; _updateNow = null; _updateLater = null;
        if (!AppearanceService.AnimationsEnabled) { Hide(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => Hide();
        UpdateLayer.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Etapa e progresso do download dentro da janela (e na barra inferior).</summary>
    private void ReportUpdateProgress(string status, double? percent)
    {
        OperationStatus.Text = status;
        if (_updateStatus != null) _updateStatus.Text = status;
        if (_updateProgress is null) return;
        _updateProgress.IsIndeterminate = percent is null;
        if (percent is { } p) _updateProgress.Value = p;
        if (_updateLater != null) _updateLater.IsEnabled = !_installingUpdate;
    }
}
