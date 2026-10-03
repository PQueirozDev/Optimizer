using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PQueirozOptimizer;

/// <summary>
/// Animações dos controles (interruptor, caixa de seleção e botão) feitas em código, ligadas pelo estilo
/// com <c>Motion.Kind</c>. Diferente das animações em gatilhos do XAML, que seguram o último valor e
/// podem ficar fora de ordem quando o estado muda dentro do próprio evento (ex.: ação falhou e o
/// interruptor volta), aqui cada mudança anima até o estado ATUAL, partindo de onde o desenho estiver.
/// Com as animações desligadas em Configurações → Aparência, tudo muda na hora.
/// </summary>
public static class Motion
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind", typeof(string), typeof(Motion), new PropertyMetadata(null, OnKindChanged));

    public static string? GetKind(DependencyObject d) => (string?)d.GetValue(KindProperty);
    public static void SetKind(DependencyObject d, string? value) => d.SetValue(KindProperty, value);

    private static bool Animate(FrameworkElement element) => element.IsLoaded && Services.AppearanceService.AnimationsEnabled;
    private static IEasingFunction EaseOut => new CubicEase { EasingMode = EasingMode.EaseOut };

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue != null) return; // ligado uma vez por controle
        switch (d)
        {
            case ToggleButton toggle when e.NewValue is "switch" or "check":
                // Ao entrar na tela (ou ser recolocado) o desenho assume o estado sem animar
                toggle.Loaded += (_, _) => SyncToggle(toggle, animate: false);
                RoutedEventHandler changed = (_, _) =>
                    // Depois do evento: se o estado for revertido dentro dele, vale o estado final
                    toggle.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => SyncToggle(toggle, Animate(toggle)));
                toggle.Checked += changed; toggle.Unchecked += changed; toggle.Indeterminate += changed;
                if (e.NewValue is "switch")
                {
                    toggle.PreviewMouseLeftButtonDown += (_, _) => PressKnob(toggle, true);
                    toggle.PreviewMouseLeftButtonUp += (_, _) => PressKnob(toggle, false);
                    toggle.MouseLeave += (_, _) => PressKnob(toggle, false);
                }
                break;
            case ButtonBase button when e.NewValue is "button":
                button.MouseEnter += (_, _) => Hover(button);
                button.MouseLeave += (_, _) => Hover(button);
                button.IsEnabledChanged += (_, _) => Hover(button);
                button.PreviewMouseLeftButtonDown += (_, _) => Press(button, true);
                button.PreviewMouseLeftButtonUp += (_, _) => Press(button, false);
                button.MouseLeave += (_, _) => Press(button, false);
                button.LostMouseCapture += (_, _) => Press(button, false);
                break;
        }
    }

    private static T? Part<T>(Control control, string name) where T : class
    {
        control.ApplyTemplate();
        return control.Template?.FindName(name, control) as T;
    }

    /// <summary>Anima (ou define na hora) uma propriedade a partir do valor atual na tela.</summary>
    private static void To(IAnimatable target, DependencyProperty property, double value, int ms, bool animate, IEasingFunction? ease = null)
    {
        if (animate)
        {
            target.BeginAnimation(property, new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? EaseOut });
            return;
        }
        target.BeginAnimation(property, null);
        ((DependencyObject)target).SetValue(property, value);
    }

    // ---------- Interruptor e caixa de seleção ----------
    private static void SyncToggle(ToggleButton toggle, bool animate)
    {
        var on = toggle.IsChecked == true;
        if (GetKind(toggle) == "switch") SyncSwitch(toggle, on, animate);
        else SyncCheck(toggle, on, animate);
    }

    private static (TranslateTransform Move, ScaleTransform Scale) KnobTransforms(FrameworkElement knob)
    {
        if (knob.RenderTransform is TransformGroup { Children: [ScaleTransform scale, TranslateTransform move] }) return (move, scale);
        var s = new ScaleTransform(1, 1); var m = new TranslateTransform();
        // A posição passa a ser só o deslocamento: a margem de "ligado" do estilo fica como reserva para telas não exibidas
        knob.Margin = new Thickness(3, 0, 0, 0);
        knob.RenderTransformOrigin = new Point(0.5, 0.5);
        knob.RenderTransform = new TransformGroup { Children = { s, m } };
        return (m, s);
    }

    private static void SyncSwitch(ToggleButton toggle, bool on, bool animate)
    {
        if (Part<FrameworkElement>(toggle, "knob") is not { } knob || Part<UIElement>(toggle, "trackOn") is not { } trackOn) return;
        var (move, scale) = KnobTransforms(knob);
        var distance = Part<FrameworkElement>(toggle, "trackArea") is { ActualWidth: > 0 } area ? area.ActualWidth - knob.Width - 6 : 18;
        var target = on ? distance : 0;
        if (animate && Math.Abs(move.X - target) > 0.5)
        {
            // Desliza com um leve quique no fim e "estica" no meio do caminho
            To(move, TranslateTransform.XProperty, target, 300, true, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });
            var stretch = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(300) };
            stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1.3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)), EaseOut));
            stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)), EaseOut));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, stretch);
            // Ao ligar: um anel da cor principal se expande e some em volta da trilha
            if (on && Part<UIElement>(toggle, "pulse") is { } pulse)
            {
                if (pulse.RenderTransform is not ScaleTransform grow) pulse.RenderTransform = grow = new ScaleTransform(1, 1);
                var expand = new DoubleAnimation(1, 1.55, TimeSpan.FromMilliseconds(520)) { EasingFunction = EaseOut };
                grow.BeginAnimation(ScaleTransform.ScaleXProperty, expand);
                grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.9, TimeSpan.FromMilliseconds(520)) { EasingFunction = EaseOut });
                pulse.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.55, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = EaseOut });
            }
        }
        else
        {
            To(move, TranslateTransform.XProperty, target, 0, false);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.ScaleX = 1;
        }
        To(trackOn, UIElement.OpacityProperty, on ? 1 : 0, 220, animate);
    }

    private static void PressKnob(ToggleButton toggle, bool pressed)
    {
        if (!toggle.IsEnabled || Part<FrameworkElement>(toggle, "knob") is not { } knob) return;
        var (_, scale) = KnobTransforms(knob);
        To(scale, ScaleTransform.ScaleYProperty, pressed ? 0.82 : 1, pressed ? 90 : 220, Animate(toggle),
            pressed ? null : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 });
    }

    private static void SyncCheck(ToggleButton toggle, bool on, bool animate)
    {
        if (Part<FrameworkElement>(toggle, "fill") is not { } fill || Part<Shape>(toggle, "check") is not { } check) return;
        if (fill.RenderTransform is not ScaleTransform pop)
        {
            pop = new ScaleTransform(1, 1);
            fill.RenderTransformOrigin = new Point(0.5, 0.5);
            fill.RenderTransform = pop;
        }
        To(fill, UIElement.OpacityProperty, on ? 1 : 0, 160, animate);
        To(check, UIElement.OpacityProperty, on ? 1 : 0, on ? 60 : 140, animate);
        // O ✓ se desenha da esquerda para a direita (tracejado revelado)
        const double length = 8;
        if (check.StrokeDashArray is not { Count: 2 }) check.StrokeDashArray = new DoubleCollection { length, length };
        if (animate && on)
        {
            check.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(length, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = EaseOut });
            var bounce = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, bounce); pop.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
        }
        else
        {
            check.BeginAnimation(Shape.StrokeDashOffsetProperty, null); check.StrokeDashOffset = 0;
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, null); pop.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            pop.ScaleX = pop.ScaleY = 1;
        }
    }

    // ---------- Botão ----------
    private static void Hover(ButtonBase button) => Hover(button, button.IsPressed);

    private static void Hover(ButtonBase button, bool pressed)
    {
        if (Part<UIElement>(button, "hover") is not { } hover) return;
        var level = !button.IsEnabled ? 0 : pressed ? 0.14 : button.IsMouseOver ? 0.07 : 0;
        To(hover, UIElement.OpacityProperty, level, button.IsMouseOver ? 120 : 220, Animate(button));
    }

    private static void Press(ButtonBase button, bool pressed)
    {
        if (Part<FrameworkElement>(button, "btnRoot") is not { } root) return;
        pressed &= button.IsEnabled;
        if (root.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            root.RenderTransform = scale;
        }
        // Afunda rápido ao pressionar e volta com um pequeno "salto"
        var ease = pressed ? null : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
        To(scale, ScaleTransform.ScaleXProperty, pressed ? 0.96 : 1, pressed ? 80 : 260, Animate(button), ease);
        To(scale, ScaleTransform.ScaleYProperty, pressed ? 0.96 : 1, pressed ? 80 : 260, Animate(button), ease);
        Hover(button, pressed);
    }
}
