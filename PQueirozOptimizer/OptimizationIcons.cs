using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PQueirozOptimizer;

/// <summary>
/// Ícones vetoriais próprios das otimizações, desenhados num grid de 24 px com traço de 1,75 px.
/// Fill é a forma de fundo (fica translúcida), Stroke são os traços e Solid os detalhes preenchidos.
/// </summary>
public sealed record VectorIcon(string Fill, string Stroke, string Solid = "");

public static class OptimizationIcons
{
    private static readonly Dictionary<string, VectorIcon> Icons = new()
    {
        ["padrao"] = new("M4.6 17a8.5 8.5 0 1 1 14.8 0z",
            "M4.6 17a8.5 8.5 0 1 1 14.8 0 M12 13.5l3.5 -4.5 M7 12.2l1 .5 M12 7v1.2 M17 12.2l-1 .5",
            "M10.4 13.5a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0 -3.2 0z"),
        ["gamer"] = new(Controller,
            Controller + " M7.5 10.5v3 M6 12h3",
            "M14.5 11a1 1 0 1 0 2 0a1 1 0 1 0 -2 0z M16.4 13a1 1 0 1 0 2 0a1 1 0 1 0 -2 0z"),
        // Controle menor com um selo de confirmação: os mesmos ajustes, sem parar serviços
        ["gamerservicos"] = new(SmallController,
            SmallController + " M6.5 11v2.6 M5.2 12.3h2.6 M13.5 7a3.5 3.5 0 1 0 7 0a3.5 3.5 0 1 0 -7 0z M15.6 7l1 1 1.8 -1.9"),
        ["debloat"] = new(Shield,
            Shield + " M12 12.2v3 M10.3 10.4a1.7 1.7 0 1 0 3.4 0a1.7 1.7 0 1 0 -3.4 0z"),
        ["quickclean"] = new(BigSpark,
            BigSpark + " M17.5 14l.8 2.2 2.2 .8 -2.2 .8 -.8 2.2 -.8 -2.2 -2.2 -.8 2.2 -.8z M17.5 3.5v3 M16 5h3"),
        ["analisar"] = new(Screen, Screen + " M6.5 12.5H9l1.5 -3 3 6 1.5 -3h2.5"),
        ["benchmark"] = new(Dial, Dial + " M12 13.5V10 M10 3.5h4 M12 3.5v3 M18.2 7.3l1.3 -1.3"),
        ["sfc"] = new(Page, Page + " M14 3.5V8h4.5 M9 14l2 2 4 -4"),
        ["dism"] = new("M12 4l8 4 -8 4 -8 -4z", "M12 4l8 4 -8 4 -8 -4z M4 12l8 4 8 -4 M4 16l8 4 8 -4"),
        ["chkdsk"] = new(DriveBase,
            DriveBase + " M4.6 13.4l2.6 -6.6A2 2 0 0 1 9.1 5.5h5.8a2 2 0 0 1 1.9 1.3l2.6 6.6 M7 16.25h4",
            "M15.5 16.25a1 1 0 1 0 2 0a1 1 0 1 0 -2 0z"),
        ["reparar"] = new(Wrench, Wrench),
        ["update"] = new("", "M19.5 12a7.5 7.5 0 0 1 -13 5.1 M4.5 12a7.5 7.5 0 0 1 13 -5.1 M17.5 3.5v3.4h-3.4 M6.5 20.5v-3.4h3.4"),
        ["reverter"] = new("M4.5 12a7.5 7.5 0 1 0 15 0a7.5 7.5 0 1 0 -15 0z",
            "M4.5 12a7.5 7.5 0 1 0 2.2 -5.3 M4.5 4v3.5H8 M12 8.5V12l2.5 1.5"),
        ["settings"] = new("", "M5 7h8 M17 7h2 M5 17h2 M11 17h8 M13 7a2 2 0 1 0 4 0a2 2 0 1 0 -4 0z M7 17a2 2 0 1 0 4 0a2 2 0 1 0 -4 0z"),
    };

    private const string Controller = "M7.5 7h9a4.5 4.5 0 0 1 4.4 3.6l.9 4.6a2.7 2.7 0 0 1 -4.7 2.3L15.5 16h-7l-1.6 1.5a2.7 2.7 0 0 1 -4.7 -2.3l.9 -4.6A4.5 4.5 0 0 1 7.5 7z";
    private const string SmallController = "M6.5 8h7a4 4 0 0 1 3.9 3.2l.8 4a2.4 2.4 0 0 1 -4.2 2L12.6 16H7.4l-1.4 1.2a2.4 2.4 0 0 1 -4.2 -2l.8 -4A4 4 0 0 1 6.5 8z";
    private const string Shield = "M12 3.5l7 2.6v5.4c0 4.4 -2.9 7.9 -7 9 -4.1 -1.1 -7 -4.6 -7 -9V6.1z";
    private const string BigSpark = "M10 4l1.6 4.4L16 10l-4.4 1.6L10 16l-1.6 -4.4L4 10l4.4 -1.6z";
    private const string Screen = "M6.5 5h11a3 3 0 0 1 3 3v8a3 3 0 0 1 -3 3h-11a3 3 0 0 1 -3 -3V8a3 3 0 0 1 3 -3z";
    private const string Dial = "M5 13.5a7 7 0 1 0 14 0a7 7 0 1 0 -14 0z";
    private const string Page = "M14 3.5H7.5a2 2 0 0 0 -2 2v13a2 2 0 0 0 2 2h9a2 2 0 0 0 2 -2V8z";
    private const string DriveBase = "M5.5 13h13a2 2 0 0 1 2 2v2.5a2 2 0 0 1 -2 2h-13a2 2 0 0 1 -2 -2V15a2 2 0 0 1 2 -2z";
    private const string Wrench = "M14.7 6.3a4 4 0 0 0 -5.4 5.1L4.5 16.2a1.8 1.8 0 0 0 2.5 2.5l4.8 -4.8a4 4 0 0 0 5.1 -5.4l-2.4 2.4 -2.1 -.4 -.4 -2.1z";

    /// <summary>Ícone da otimização (pelo Id); operações sem desenho próprio usam o de ajustes.</summary>
    public static VectorIcon For(string id) => id switch
    {
        "corrupcao" => Icons["sfc"],
        _ => Icons.TryGetValue(id, out var icon) ? icon : Icons["settings"],
    };

    /// <summary>Desenha o ícone no tamanho pedido, na cor do recurso informado (segue a troca de tema).</summary>
    public static FrameworkElement Render(VectorIcon icon, double size, string brushKey)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        if (icon.Fill.Length > 0)
        {
            var fill = new Path { Data = Geometry.Parse(icon.Fill), Opacity = 0.14 };
            fill.SetResourceReference(Shape.FillProperty, brushKey);
            canvas.Children.Add(fill);
        }
        var stroke = new Path
        {
            Data = Geometry.Parse(icon.Stroke), StrokeThickness = 1.75,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        stroke.SetResourceReference(Shape.StrokeProperty, brushKey);
        canvas.Children.Add(stroke);
        if (icon.Solid.Length > 0)
        {
            var solid = new Path { Data = Geometry.Parse(icon.Solid) };
            solid.SetResourceReference(Shape.FillProperty, brushKey);
            canvas.Children.Add(solid);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    }
}
