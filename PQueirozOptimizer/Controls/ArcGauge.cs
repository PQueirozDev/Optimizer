using System.Windows;
using System.Windows.Media;

namespace PQueirozOptimizer.Controls;

/// <summary>
/// Medidor em arco de 270° (estilo velocímetro) do Início: trilha discreta, arco no degradê do destaque e um halo
/// suave por baixo. Só redesenha quando o valor muda; as cores vêm dos recursos do tema a cada desenho.
/// </summary>
public sealed class ArcGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0 a 100. NaN = sem leitura (só a trilha).</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get; set; } = 10;

    private const double Sweep = 270, Start = 135; // abre embaixo, como um velocímetro

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - Thickness) / 2 - 4;
        var track = TryFindResource("ChartGridBrush") as Brush ?? Brushes.Gray;
        var arcBrush = TryFindResource("AccentGradientBrush") as Brush ?? Brushes.DodgerBlue;
        var glow = TryFindResource("AccentSoftBrush") as Brush;
        dc.DrawGeometry(null, Pen(TryFindResource("BorderSubtleBrush") as Brush ?? track, Thickness), Arc(center, radius, Start, Sweep));
        if (double.IsNaN(Value)) return;
        var sweep = Math.Clamp(Value, 0, 100) / 100 * Sweep;
        if (sweep < 0.5) return;
        var geometry = Arc(center, radius, Start, sweep);
        if (glow != null) dc.DrawGeometry(null, Pen(glow, Thickness + 8), geometry);
        dc.DrawGeometry(null, Pen(arcBrush, Thickness), geometry);
    }

    private static Pen Pen(Brush brush, double thickness) => new(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    private static Geometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        Point At(double deg) { var rad = deg * Math.PI / 180; return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad)); }
        var figure = new PathFigure { StartPoint = At(startDeg), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(At(startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true));
        var g = new PathGeometry(new[] { figure });
        g.Freeze();
        return g;
    }
}
