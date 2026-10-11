using System.Windows;
using System.Windows.Media;

namespace PQueirozOptimizer.Controls;

public enum ArtKind { DotWave, Rings, Squares, Bars, Waves }

/// <summary>
/// Grafismo abstrato e estático dos cards (pontos em onda, anéis, quadrados girados, barras, ondas), desenhado no
/// azul de destaque com pouca opacidade e sumindo para o lado do texto. Só é redesenhado quando o card muda de
/// tamanho: não anima e não usa efeitos (desfoque, sombra), então não pesa na tela parada.
/// </summary>
public sealed class QrzArt : FrameworkElement
{
    public ArtKind Kind { get; set; }
    /// <summary>Lado onde o desenho é mais forte (0 = esquerda, 1 = direita).</summary>
    public double Anchor { get; set; } = 1;
    public int Seed { get; set; }

    public QrzArt()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var accent = TryFindResource("AccentColor") is Color c ? c : Color.FromRgb(0x3B, 0x82, 0xF6);
        var dark = Services.ThemeService.IsDark;
        var strength = dark ? 1.0 : 0.7;
        // Mais forte do lado da âncora e desaparecendo para o lado do texto
        double Fade(double x) => Math.Clamp(1 - Math.Abs(x / w - Anchor) * 1.35, 0, 1);
        Brush Solid(double alpha) { var b = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(alpha * 255 * strength, 0, 255), accent.R, accent.G, accent.B)); b.Freeze(); return b; }
        Pen Line(double alpha, double thickness) { var p = new Pen(Solid(alpha), thickness); p.Freeze(); return p; }

        switch (Kind)
        {
            case ArtKind.DotWave:
                {
                    const double gap = 9;
                    for (var x = gap / 2; x < w; x += gap)
                        for (var y = gap / 2; y < h; y += gap)
                        {
                            var wave = Math.Sin(x / w * Math.PI * 2.2 + Seed) * 0.5 + Math.Cos(y / h * Math.PI * 1.6 + x / w * 2) * 0.5;
                            var a = Fade(x) * Math.Clamp(0.08 + wave * 0.22, 0, 0.34);
                            if (a < 0.02) continue;
                            dc.DrawEllipse(Solid(a), null, new Point(x, y), 1.1 + wave * 0.5, 1.1 + wave * 0.5);
                        }
                    break;
                }
            case ArtKind.Rings:
                {
                    var center = new Point(w * Anchor, h * 0.15);
                    for (var r = 18.0; r < Math.Max(w, h) * 1.1; r += 14)
                        dc.DrawEllipse(null, Line(Math.Max(0, 0.22 - r / (Math.Max(w, h) * 4)) * Fade(Math.Clamp(center.X - r * 0.6 * (Anchor > 0.5 ? 1 : -1), 0, w)), 1), center, r, r);
                    break;
                }
            case ArtKind.Squares:
                {
                    // Quadrados girando em torno de um centro (o "túnel" geométrico do card de boas-vindas)
                    var center = new Point(w * (0.35 + 0.4 * Anchor), h * 0.5);
                    var size = Math.Min(w, h) * 0.95;
                    for (var i = 0; i < 26; i++)
                    {
                        var s = size * (1 - i * 0.032);
                        var angle = i * 3.2 + Seed * 7;
                        var group = new TransformGroup();
                        group.Children.Add(new RotateTransform(angle, center.X, center.Y));
                        dc.PushTransform(group);
                        dc.DrawRectangle(null, Line(0.05 + i * 0.006, 1), new Rect(center.X - s / 2, center.Y - s / 2, s, s));
                        dc.Pop();
                    }
                    break;
                }
            case ArtKind.Bars:
                {
                    var random = new Random(Seed + 3);
                    const double bar = 5, gap = 5;
                    for (var x = w * (1 - 0.55 * (Anchor > 0.5 ? 1 : 0)); x < w * (Anchor > 0.5 ? 1 : 0.55); x += bar + gap)
                    {
                        var height = h * (0.15 + 0.55 * Math.Abs(Math.Sin(x / 37.0 + Seed)) * (0.6 + random.NextDouble() * 0.4));
                        dc.DrawRoundedRectangle(Solid(0.1 + 0.14 * Fade(x)), null, new Rect(x, h - height - 10, bar, height), 2, 2);
                    }
                    break;
                }
            case ArtKind.Waves:
                {
                    for (var line = 0; line < 22; line++)
                    {
                        var g = new StreamGeometry();
                        using (var ctx = g.Open())
                        {
                            var started = false;
                            for (var x = w * 0.25; x <= w + 4; x += 6)
                            {
                                var t = x / w;
                                var y = h * 0.62 + Math.Sin(t * Math.PI * 2 + line * 0.18 + Seed) * h * 0.18 - line * 3.2 + Math.Cos(t * 5 + line * 0.3) * 6;
                                if (!started) { ctx.BeginFigure(new Point(x, y), false, false); started = true; }
                                else ctx.LineTo(new Point(x, y), true, true);
                            }
                        }
                        g.Freeze();
                        dc.DrawGeometry(null, Line(0.05 + 0.012 * (22 - line) * 0.5, 1), g);
                    }
                    break;
                }
        }
    }
}
