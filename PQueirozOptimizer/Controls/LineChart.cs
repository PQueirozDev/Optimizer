using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PQueirozOptimizer.Controls;

/// <summary>
/// Gráfico de linha leve (um OnRender, sem animação contínua) para o monitor e o Performance Lab. Valores nulos
/// viram buracos na linha: o que não foi medido não é desenhado como se fosse zero.
/// </summary>
public sealed class LineChart : FrameworkElement
{
    private readonly List<double?> _values = new();
    public int Capacity { get; set; } = 60;
    /// <summary>Teto fixo do eixo (ex.: 100 para %). Null = acompanha o maior valor.</summary>
    public double? Maximum { get; set; }
    public string Unit { get; set; } = "";
    public string StrokeKey { get; set; } = "AccentBrush";
    public bool ShowScale { get; set; } = true;

    public LineChart() { SnapsToDevicePixels = true; MinHeight = 60; }

    public void Push(double? value)
    {
        _values.Add(value is { } v && (double.IsNaN(v) || double.IsInfinity(v)) ? null : value);
        while (_values.Count > Capacity) _values.RemoveAt(0);
        InvalidateVisual();
    }

    public void SetValues(IEnumerable<double?> values)
    {
        _values.Clear();
        _values.AddRange(values);
        Capacity = Math.Max(Capacity, _values.Count);
        InvalidateVisual();
    }

    public double? Latest => _values.LastOrDefault(v => v.HasValue);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var stroke = TryFindResource(StrokeKey) as Brush ?? Brushes.MediumPurple;
        var grid = TryFindResource("BorderSubtleBrush") as Brush ?? Brushes.Gray;
        var muted = TryFindResource("MutedBrush") as Brush ?? Brushes.Gray;
        var gridPen = new Pen(grid, 1);
        gridPen.Freeze();
        for (var i = 1; i < 4; i++) dc.DrawLine(gridPen, new Point(0, Math.Round(h * i / 4) + 0.5), new Point(w, Math.Round(h * i / 4) + 0.5));
        var present = _values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var max = Maximum ?? (present.Count == 0 ? 1 : Math.Max(1, present.Max() * 1.15));
        // Sem dados e sem teto fixo, a escala ("1") não significa nada
        if (ShowScale && (Maximum is not null || present.Count > 0))
        {
            var text = new FormattedText($"{max:0}{Unit}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(4, 2));
        }
        if (present.Count == 0) return;
        var step = w / Math.Max(1, Capacity - 1);
        var offset = Capacity - _values.Count;
        var pen = new Pen(stroke, 2) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        var fill = stroke.Clone(); fill.Opacity = 0.14; fill.Freeze();
        // Cada trecho contínuo (sem nulos) vira uma linha com área preenchida embaixo
        var segment = new List<Point>();
        void Flush()
        {
            if (segment.Count >= 2)
            {
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(segment[0].X, h), true, true);
                    foreach (var p in segment) ctx.LineTo(p, true, true);
                    ctx.LineTo(new Point(segment[^1].X, h), true, true);
                }
                geometry.Freeze();
                dc.DrawGeometry(fill, null, geometry);
                var line = new StreamGeometry();
                using (var ctx = line.Open())
                {
                    ctx.BeginFigure(segment[0], false, false);
                    foreach (var p in segment.Skip(1)) ctx.LineTo(p, true, true);
                }
                line.Freeze();
                dc.DrawGeometry(null, pen, line);
            }
            else if (segment.Count == 1) dc.DrawEllipse(stroke, null, segment[0], 2, 2);
            segment.Clear();
        }
        for (var i = 0; i < _values.Count; i++)
        {
            if (_values[i] is not { } v) { Flush(); continue; }
            segment.Add(new Point((i + offset) * step, h - Math.Clamp(v / max, 0, 1) * (h - 4) - 2));
        }
        Flush();
    }
}
