using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PQueirozOptimizer.Controls;

/// <summary>
/// Medidor circular (QrzGauge): arco de 270° aberto embaixo, com o valor e uma legenda no centro. Sem valor
/// (sensor indisponível) o arco fica vazio e o centro mostra "--". Desenhado num OnRender só; a única animação é
/// a entrada (0 → valor), as leituras seguintes mudam direto para não manter a tela redesenhando.
/// </summary>
public sealed class QrzGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(QrzGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(QrzGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Valor do arco, de 0 a 100 (null = sem leitura).</summary>
    public double Value { get => (double)GetValue(ValueProperty); private set => SetValue(ValueProperty, value); }
    /// <summary>Fração desenhada (anima de 0 até 1 na entrada).</summary>
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    private string _text = "--";
    private string _caption = "";
    private bool _hasValue;
    public string StrokeKey { get; set; } = "ChartSecondaryBrush";
    public double Thickness { get; set; } = 7;

    public QrzGauge()
    {
        Width = Height = 92;
        Loaded += (_, _) =>
        {
            if (Services.AppearanceService.AnimationsEnabled && IsVisible)
                BeginAnimation(ProgressProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else Progress = 1;
        };
    }

    /// <summary>Atualiza o medidor: <paramref name="value"/> null = indisponível.</summary>
    public void Set(double? value, string text, string caption)
    {
        _hasValue = value.HasValue;
        _text = text; _caption = caption;
        Value = Math.Clamp(value ?? 0, 0, 100);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var track = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
        var stroke = TryFindResource(StrokeKey) as Brush ?? Brushes.SkyBlue;
        var text = TryFindResource("TextBrush") as Brush ?? Brushes.White;
        var muted = TryFindResource("MutedBrush") as Brush ?? Brushes.Gray;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size / 2 - Thickness / 2 - 1;
        const double start = 135, sweep = 270;

        Geometry Arc(double degrees)
        {
            Point At(double a) { var r = a * Math.PI / 180; return new Point(center.X + radius * Math.Cos(r), center.Y + radius * Math.Sin(r)); }
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(At(start), false, false);
                ctx.ArcTo(At(start + degrees), new Size(radius, radius), 0, degrees > 180, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }
        var trackPen = new Pen(track, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; trackPen.Freeze();
        dc.DrawGeometry(null, trackPen, Arc(sweep));
        var degrees = sweep * Value / 100 * Math.Clamp(Progress, 0, 1);
        if (_hasValue && degrees > 0.5)
        {
            var pen = new Pen(stroke, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; pen.Freeze();
            dc.DrawGeometry(null, pen, Arc(degrees));
        }
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var display = new FontFamily("Segoe UI Variable Display, Segoe UI");
        var big = new FormattedText(_text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(display, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), size * 0.22, _hasValue ? text : muted, dpi);
        var small = new FormattedText(_caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), Math.Max(10, size * 0.12), muted, dpi);
        var total = big.Height + (string.IsNullOrEmpty(_caption) ? 0 : small.Height - 2);
        dc.DrawText(big, new Point(center.X - big.Width / 2, center.Y - total / 2));
        if (!string.IsNullOrEmpty(_caption)) dc.DrawText(small, new Point(center.X - small.Width / 2, center.Y - total / 2 + big.Height - 2));
    }
}
