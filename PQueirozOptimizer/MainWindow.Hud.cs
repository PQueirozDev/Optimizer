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
    private void StartHud()
    {
        Action<HardwareSample> onSample = sample => Dispatcher.BeginInvoke(() => UpdateHud(sample));
        HardwareMonitorService.Shared.Sampled += onSample;
        Closed += (_, _) => HardwareMonitorService.Shared.Sampled -= onSample;
        UpdateGameModeBadge();
    }

    private void UpdateHud(HardwareSample s)
    {
        // Janela minimizada ou na bandeja: os medidores da barra lateral não são vistos, então não são redesenhados.
        // As curvas do Início continuam recebendo as leituras, para não mostrarem um minuto velho ao voltar.
        if (IsVisible && WindowState != WindowState.Minimized) UpdateSidebarMeters(s);

        // Os cards do Início (e do Meu PC) só são atualizados enquanto estão na tela
        if (_homeRoot is null || _homeLive.Count == 0 || !IsInContent(_homeRoot)) return;
        foreach (var update in _homeLive) update(s);
    }

    private void UpdateSidebarMeters(HardwareSample s)
    {
        SetMeter(HudCpuBar, HudCpuText, s.Cpu);
        SetMeter(HudRamBar, HudRamText, s.Ram);
        if (s.Gpu is { } gpu) SetMeter(HudGpuBar, HudGpuText, gpu);
        else { HudGpuBar.Value = 0; HudGpuText.Text = "--"; }
        HudPing.Text = s.PingMs is { } ping ? $"{ping:0} ms" : "-- ms";
        HudPing.SetResourceReference(TextBlock.ForegroundProperty, s.PingMs switch { null => "MutedBrush", < 60 => "SuccessBrush", < 120 => "WarningBrush", _ => "DangerBrush" });
        HudLiveDot.Opacity = HudLiveDot.Opacity > 0.6 ? 0.45 : 1; // pisca a cada leitura: mostra que está ao vivo
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
            // Transições curtas (até 220 ms): a página aparece rápido e a cascata só sugere a ordem
            var delay = TimeSpan.FromMilliseconds(index++ * 30);
            // Reaproveita o deslocamento do efeito de "subir ao passar o mouse" dos cartões
            var slide = block.RenderTransform as TranslateTransform ?? new TranslateTransform();
            block.RenderTransform = slide;
            block.Opacity = 0; // invisível até a vez dele na cascata
            block.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { BeginTime = delay, EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { BeginTime = delay, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
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
        if (SubTabs != null) RefreshTopNav(_topNavPage);
    }
}
