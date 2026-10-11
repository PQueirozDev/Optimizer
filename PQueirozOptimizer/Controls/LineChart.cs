using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PQueirozOptimizer.Controls;

/// <summary>
/// Gráfico de linha leve (QrzChart): um OnRender por leitura, sem animação contínua. Curva suave monotônica
/// (não inventa picos entre os pontos), área em gradiente que some embaixo, grade quase invisível, ponto final
/// destacado e o valor de cada ponto ao passar o mouse. Valores nulos viram buracos: o que não foi medido não é
/// desenhado como se fosse zero.
/// </summary>
public sealed class LineChart : FrameworkElement
{
    private readonly List<double?> _values = new();
    private int? _hover;
    public int Capacity { get; set; } = 60;
    /// <summary>Teto fixo do eixo (ex.: 100 para %). Null = acompanha o maior valor.</summary>
    public double? Maximum { get; set; }
    /// <summary>Piso do teto automático (ex.: 40 ms no ping), para uma linha quase parada não parecer um serrote.</summary>
    public double MinimumScale { get; set; } = 1;
    public string Unit { get; set; } = "";
    public string StrokeKey { get; set; } = "ChartLineBrush";
    /// <summary>Recurso da área; null = gradiente da própria cor da linha, que some embaixo.</summary>
    public string? FillKey { get; set; }
    public bool ShowScale { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public bool ShowEndDot { get; set; } = true;
    /// <summary>Intervalo entre leituras, para a dica dizer "há 12 s"; 0 = sem tempo na dica.</summary>
    public double SecondsPerSample { get; set; }
    /// <summary>Texto do valor na dica (padrão: número + unidade).</summary>
    public Func<double, string>? Format { get; set; }

    public LineChart()
    {
        SnapsToDevicePixels = true;
        MinHeight = 40;
        // Fundo transparente para receber o mouse em toda a área do gráfico
        Cursor = Cursors.Cross;
    }

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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var w = ActualWidth;
        if (w <= 0 || _values.Count == 0) return;
        var step = w / Math.Max(1, Capacity - 1);
        var offset = Capacity - _values.Count;
        var index = (int)Math.Round(e.GetPosition(this).X / step) - offset;
        index = Math.Clamp(index, 0, _values.Count - 1);
        if (_hover != index) { _hover = index; InvalidateVisual(); }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover is null) return;
        _hover = null;
        InvalidateVisual();
    }

    private string Describe(double value) => Format?.Invoke(value) ?? $"{value.ToString(value < 10 && value % 1 != 0 ? "0.#" : "0", CultureInfo.CurrentCulture)}{Unit}";

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // área de mouse
        var stroke = TryFindResource(StrokeKey) as Brush ?? Brushes.SteelBlue;
        var fill = FillKey is { } key ? TryFindResource(key) as Brush : AreaFor(stroke);
        var grid = TryFindResource("ChartGridBrush") as Brush ?? Brushes.Transparent;
        var muted = TryFindResource("MutedBrush") as Brush ?? Brushes.Gray;
        var text = TryFindResource("TextBrush") as Brush ?? Brushes.White;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (ShowGrid)
        {
            var gridPen = new Pen(grid, 1); gridPen.Freeze();
            for (var i = 1; i < 4; i++) dc.DrawLine(gridPen, new Point(0, Math.Round(h * i / 4) + 0.5), new Point(w, Math.Round(h * i / 4) + 0.5));
        }
        var present = _values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var max = Maximum ?? (present.Count == 0 ? 1 : Math.Max(MinimumScale, present.Max() * 1.2));
        // Sem dados e sem teto fixo, a escala ("1") não significa nada
        if (ShowScale && (Maximum is not null || present.Count > 0))
        {
            var scale = new FormattedText(Describe(max), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, muted, dpi);
            dc.DrawText(scale, new Point(4, 2));
        }
        if (present.Count == 0) return;
        var step = w / Math.Max(1, Capacity - 1);
        var offset = Capacity - _values.Count;
        double Y(double v) => h - Math.Clamp(v / max, 0, 1) * (h - 8) - 4;
        var pen = new Pen(stroke, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        // Cada trecho contínuo (sem nulos) vira uma curva com a área preenchida embaixo
        var segment = new List<Point>();
        void Flush()
        {
            if (segment.Count >= 2)
            {
                var area = new StreamGeometry();
                using (var ctx = area.Open())
                {
                    ctx.BeginFigure(new Point(segment[0].X, h), true, true);
                    ctx.LineTo(segment[0], false, false);
                    Smooth(ctx, segment);
                    ctx.LineTo(new Point(segment[^1].X, h), false, false);
                }
                area.Freeze();
                if (fill != null) dc.DrawGeometry(fill, null, area);
                var line = new StreamGeometry();
                using (var ctx = line.Open())
                {
                    ctx.BeginFigure(segment[0], false, false);
                    Smooth(ctx, segment);
                }
                line.Freeze();
                dc.DrawGeometry(null, pen, line);
            }
            else if (segment.Count == 1) dc.DrawEllipse(stroke, null, segment[0], 2, 2);
            segment.Clear();
        }
        Point? last = null;
        for (var i = 0; i < _values.Count; i++)
        {
            if (_values[i] is not { } v) { Flush(); continue; }
            var p = new Point((i + offset) * step, Y(v));
            segment.Add(p);
            last = p;
        }
        Flush();

        // Ponto final: miolo claro com um anel azul translúcido (sem desfoque, custa o mesmo que um círculo)
        if (ShowEndDot && _hover is null && last is { } end && _values[^1] is not null)
        {
            var ring = stroke.Clone(); ring.Opacity = 0.28; ring.Freeze();
            dc.DrawEllipse(ring, null, end, 7, 7);
            dc.DrawEllipse(text, new Pen(stroke, 2), end, 3.6, 3.6);
        }

        // Dica do ponto sob o mouse: linha guia, ponto e o valor (com há quanto tempo foi lido)
        if (_hover is { } index && index < _values.Count && _values[index] is { } hv)
        {
            var x = (index + offset) * step;
            var guide = new Pen(muted, 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) }; guide.Freeze();
            dc.DrawLine(guide, new Point(Math.Round(x) + 0.5, 0), new Point(Math.Round(x) + 0.5, h));
            var point = new Point(x, Y(hv));
            dc.DrawEllipse(text, new Pen(stroke, 2), point, 3.6, 3.6);
            var ago = SecondsPerSample > 0 ? (_values.Count - 1 - index) * SecondsPerSample : -1;
            var label = Describe(hv) + (ago > 0.5 ? "  ·  " + string.Format(Services.Translator.Tr("há {0} s"), ago.ToString("0", CultureInfo.CurrentCulture)) : "");
            var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 11, text, dpi);
            var boxW = ft.Width + 14; var boxH = ft.Height + 8;
            var bx = Math.Clamp(x - boxW / 2, 0, Math.Max(0, w - boxW));
            var by = point.Y - boxH - 10 < 0 ? point.Y + 10 : point.Y - boxH - 10;
            var bg = TryFindResource("CardElevatedBrush") as Brush ?? Brushes.Black;
            var border = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
            dc.DrawRoundedRectangle(bg, new Pen(border, 1), new Rect(bx, by, boxW, boxH), 6, 6);
            dc.DrawText(ft, new Point(bx + 7, by + 4));
        }
    }

    private Brush? _area;
    private Color _areaColor;

    /// <summary>Gradiente vertical da cor da linha (translúcido no alto, transparente embaixo).</summary>
    private Brush? AreaFor(Brush stroke)
    {
        if (stroke is not SolidColorBrush solid) return TryFindResource("ChartFillBrush") as Brush;
        if (_area != null && _areaColor == solid.Color) return _area;
        _areaColor = solid.Color;
        var top = Color.FromArgb(0x50, solid.Color.R, solid.Color.G, solid.Color.B);
        var area = new LinearGradientBrush(top, Color.FromArgb(0, solid.Color.R, solid.Color.G, solid.Color.B), new Point(0, 0), new Point(0, 1));
        area.Freeze();
        return _area = area;
    }

    /// <summary>
    /// Curva cúbica monotônica (Fritsch–Carlson): passa por todos os pontos e não ultrapassa os valores vizinhos,
    /// então a linha fica suave sem desenhar máximos ou mínimos que não foram medidos.
    /// </summary>
    private static void Smooth(StreamGeometryContext ctx, IReadOnlyList<Point> p)
    {
        var n = p.Count;
        if (n < 3) { for (var i = 1; i < n; i++) ctx.LineTo(p[i], true, true); return; }
        var dx = new double[n - 1]; var slope = new double[n - 1]; var m = new double[n];
        for (var i = 0; i < n - 1; i++) { dx[i] = p[i + 1].X - p[i].X; slope[i] = dx[i] == 0 ? 0 : (p[i + 1].Y - p[i].Y) / dx[i]; }
        m[0] = slope[0]; m[n - 1] = slope[n - 2];
        for (var i = 1; i < n - 1; i++) m[i] = slope[i - 1] * slope[i] <= 0 ? 0 : (slope[i - 1] + slope[i]) / 2;
        for (var i = 0; i < n - 1; i++)
        {
            if (slope[i] == 0) { m[i] = 0; m[i + 1] = 0; continue; }
            var a = m[i] / slope[i]; var b = m[i + 1] / slope[i];
            var s = a * a + b * b;
            if (s > 9) { var t = 3 / Math.Sqrt(s); m[i] = t * a * slope[i]; m[i + 1] = t * b * slope[i]; }
        }
        for (var i = 0; i < n - 1; i++)
        {
            var c1 = new Point(p[i].X + dx[i] / 3, p[i].Y + m[i] * dx[i] / 3);
            var c2 = new Point(p[i + 1].X - dx[i] / 3, p[i + 1].Y - m[i + 1] * dx[i] / 3);
            ctx.BezierTo(c1, c2, p[i + 1], true, true);
        }
    }
}
