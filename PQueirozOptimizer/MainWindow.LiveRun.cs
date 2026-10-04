using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Linha da saída de uma operação, já limpa e classificada por tipo.</summary>
public sealed record OutputLine(string Text, string Tone);

public partial class MainWindow
{
    private static readonly Regex DecorationOnly = new(@"^[\s=\-─═╔╗╚╝║┌┐└┘├┤┬┴┼│]+$", RegexOptions.Compiled);
    private static readonly Regex StepCounter = new(@"^\[\d+/\d+\]\s*", RegexOptions.Compiled);

    /// <summary>
    /// Limpa uma linha do script (molduras, contadores, marcadores) e decide a cor.
    /// Retorna null para linhas puramente decorativas.
    /// </summary>
    public static OutputLine? ClassifyLine(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0 || DecorationOnly.IsMatch(text)) return null;
        var isError = text.StartsWith("[ERRO]");
        if (isError) text = text[6..].Trim();
        // Moldura técnica dos erros do PowerShell ("+ CategoryInfo", "At line:1", "+ ~~~~")
        if (text.StartsWith("+ ") || text.StartsWith("At line:") || text.StartsWith("No linha:") || DecorationOnly.IsMatch(text.Replace("~", "-"))) return null;
        if (isError) return new OutputLine(text, "Danger");
        text = StepCounter.Replace(text, "").Trim();
        if (text.StartsWith("•")) text = text.TrimStart('•').Trim();
        string tone = "Muted";
        if (text.StartsWith("[OK]")) { tone = "Success"; text = text[4..].Trim(); }
        else if (text.StartsWith("[!!]")) { tone = "Danger"; text = text[4..].Trim(); }
        else if (text.StartsWith("[FALHA]")) { tone = "Danger"; text = text[7..].Trim(); }
        else if (text.StartsWith("[AVISO]")) { tone = "Warning"; text = text[7..].Trim(); }
        else if (text.StartsWith("[INFO]")) { tone = "Info"; text = text[6..].Trim(); }
        else if (text.StartsWith("[IGNORADA]")) { tone = "Muted"; text = "Ignorada: " + text[10..].Trim(); }
        else if (text.StartsWith("[IGNORADO]")) { tone = "Warning"; text = text[10..].Trim(); }
        else if (text.StartsWith("[--]")) { tone = "Muted"; text = text[4..].Trim(); }
        else if (text == text.ToUpperInvariant() && text.Any(char.IsLetter) && text.Length > 3) tone = "Text";
        return text.Length == 0 ? null : new OutputLine(text, tone);
    }

    private (string Title, VectorIcon Icon, string Description) OperationInfo(string operation)
    {
        var def = ConfigService.AllOptimizations.FirstOrDefault(o => o.Operation == operation);
        return operation switch
        {
            "reverter" => ("Restaurar configurações", OptimizationIcons.For("reverter"), "Desfazendo as alterações registradas no último backup."),
            "corrupcao" => ("Verificação de corrupção do sistema", OptimizationIcons.For("corrupcao"), "ChkDsk, SFC, DISM e uma verificação final do SFC. Pode levar mais de 30 minutos."),
            _ => (def?.Name ?? operation, OptimizationIcons.For(def?.Id ?? "reverter"), def?.Description ?? ""),
        };
    }

    private static DataTemplate OutputLineTemplate()
    {
        // Ponto colorido + texto; a cor vem do "Tone" (Success, Danger, Warning, Info, Muted, Text)
        var panel = new FrameworkElementFactory(typeof(DockPanel));
        var dot = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
        dot.SetValue(FrameworkElement.WidthProperty, 7.0);
        dot.SetValue(FrameworkElement.HeightProperty, 7.0);
        dot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 10, 0));
        dot.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        dot.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding(nameof(OutputLine.Tone)) { Converter = ToneBrushConverter.Instance });
        panel.AppendChild(dot);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(OutputLine.Text)));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(TextBlock.FontSizeProperty, 12.5);
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 4)); // fonte normal do app: a saída não parece mais um terminal
        text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(OutputLine.Tone)) { Converter = ToneBrushConverter.Instance, ConverterParameter = "text" });
        panel.AppendChild(text);
        return new DataTemplate { VisualTree = panel };
    }

    private sealed class ToneBrushConverter : System.Windows.Data.IValueConverter
    {
        public static readonly ToneBrushConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var tone = value as string ?? "Muted";
            // Texto comum fica na cor normal; o ponto usa a cor do tipo
            if (parameter as string == "text") tone = tone is "Danger" or "Warning" ? tone : tone == "Muted" ? "Muted" : "Text";
            return Application.Current.TryFindResource(tone + "Brush") as Brush ?? Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
    }
}
