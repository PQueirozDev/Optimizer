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

    /// <summary>Executa uma operação do script mostrando a saída completa ao vivo.</summary>
    private async Task RunLiveAsync(string operation, IReadOnlyList<string>? selectedSteps = null)
    {
        if (_operationRunning) { OperationStatus.Text = "Aguarde a operação em andamento."; return; }
        var (title, icon, description) = OperationInfo(operation);
        PageTitle.Text = title;
        PageBadge.Visibility = Visibility.Collapsed;

        var root = new StackPanel();
        var head = new DockPanel();
        var status = Pill("Em execução", "Accent");
        var elapsed = Label("00:00", 12, true); elapsed.Margin = new Thickness(0, 6, 0, 0); elapsed.HorizontalAlignment = HorizontalAlignment.Right;
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        status.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(status); right.Children.Add(elapsed);
        // SFC/DISM podem levar muito tempo ou travar: a operação pode ser interrompida
        var cancel = IconButton(Glyphs.Cancel, "Cancelar");
        cancel.SetResourceReference(StyleProperty, "GhostButton");
        cancel.Margin = new Thickness(0, 6, 0, 0); cancel.Padding = new Thickness(10, 4, 10, 4); cancel.HorizontalAlignment = HorizontalAlignment.Right;
        cancel.Click += (_, _) => { if (ConfirmCancelOperation()) cancel.IsEnabled = false; };
        right.Children.Add(cancel);
        DockPanel.SetDock(right, Dock.Right); head.Children.Add(right);
        var chip = OutlineChip(icon, 52, primary: true); DockPanel.SetDock(chip, Dock.Left); head.Children.Add(chip);
        var headText = new StackPanel { Margin = new Thickness(18, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        var t = Label(title, 18); t.Margin = new Thickness(0, 0, 0, 4);
        headText.Children.Add(t);
        var d = Label(selectedSteps is null ? description : $"{selectedSteps.Count} ajustes selecionados. Um ponto de restauração é criado antes de qualquer alteração.", 12.5, true); d.Margin = new Thickness(0);
        headText.Children.Add(d);
        head.Children.Add(headText);
        var progress = new ProgressBar { IsIndeterminate = true, Height = 5, Margin = new Thickness(0, 18, 0, 0) };
        var headPanel = new StackPanel(); headPanel.Children.Add(head); headPanel.Children.Add(progress);
        var headCard = Surface(headPanel); headCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        root.Children.Add(headCard);

        // Contadores do resultado
        var okCount = 0; var failCount = 0; var warnCount = 0;
        var counters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var okPill = Pill("Concluídos: 0", "Success"); var failPill = Pill("Falhas: 0", "Danger"); var warnPill = Pill("Avisos: 0", "Warning");
        foreach (var p in new[] { okPill, warnPill, failPill }) { p.Margin = new Thickness(0, 0, 8, 0); counters.Children.Add(p); }
        void UpdateCounters()
        {
            ((TextBlock)okPill.Child).Text = $"Concluídos: {okCount}";
            ((TextBlock)failPill.Child).Text = $"Falhas: {failCount}";
            ((TextBlock)warnPill.Child).Text = $"Avisos: {warnCount}";
        }

        // Saída ao vivo
        var lines = new ObservableCollection<OutputLine>();
        var output = new ListBox { ItemsSource = lines, Height = 380, ItemTemplate = OutputLineTemplate() };
        VirtualizingPanel.SetIsVirtualizing(output, true);
        var outputPanel = new StackPanel();
        var outputHead = new DockPanel();
        var copy = IconButton(Glyphs.Document, "Copiar resultado"); copy.Margin = new Thickness(0); copy.VerticalAlignment = VerticalAlignment.Top;
        copy.SetResourceReference(StyleProperty, "GhostButton");
        copy.Click += (_, _) => CopyText(string.Join(Environment.NewLine, lines.Select(l => l.Text)), "Resultado copiado para a área de transferência.");
        DockPanel.SetDock(copy, Dock.Right); outputHead.Children.Add(copy);
        outputHead.Children.Add(SectionHeader("Saída da execução"));
        outputPanel.Children.Add(outputHead);
        outputPanel.Children.Add(counters);
        outputPanel.Children.Add(output);
        root.Children.Add(Surface(outputPanel));

        var actions = new WrapPanel();
        root.Children.Add(actions);
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);

        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => elapsed.Text = clock.Elapsed.ToString(@"mm\:ss");
        timer.Start();

        var failed = false;
        var cancelled = false;
        await ExecuteTrackedAsync(title, async token =>
        {
            try
            {
                await _powershell.RunAsync(operation, selectedSteps, new Progress<string>(raw =>
                {
                    if (ClassifyLine(raw) is not { } line) return;
                    if (!cancelled) OperationStatus.Text = line.Text;
                    lines.Add(line);
                    if (line.Tone == "Success") okCount++;
                    else if (line.Tone == "Danger") failCount++;
                    else if (line.Tone == "Warning") warnCount++;
                    UpdateCounters();
                    output.ScrollIntoView(line);
                }), token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }
            catch
            {
                failed = true;
                throw;
            }
        });
        timer.Stop(); clock.Stop();
        cancel.Visibility = Visibility.Collapsed;
        elapsed.Text = clock.Elapsed.ToString(@"mm\:ss");
        progress.IsIndeterminate = false; progress.Value = 100;
        var tone = cancelled || failed || failCount > 0 ? "Warning" : "Success";
        ((TextBlock)status.Child).Text = cancelled ? "Cancelado" : failed || failCount > 0 ? "Concluído com falhas" : "Concluído";
        ((TextBlock)status.Child).SetResourceReference(TextBlock.ForegroundProperty, tone + "Brush");
        status.SetResourceReference(Border.BackgroundProperty, tone + "SoftBrush");
        chip.SetResourceReference(Border.BackgroundProperty, tone + "SoftBrush");
        ((TextBlock)chip.Child).SetResourceReference(TextBlock.ForegroundProperty, tone + "Brush");

        if (operation is "padrao" or "gamer" or "gamerservicos" or "debloat" or "reverter")
        {
            var history = IconButton(Glyphs.History, "Ver backup e reversão"); history.Click += History_Click;
            actions.Children.Add(history);
        }
        var back = IconButton(Glyphs.ChevronRight, "Voltar às otimizações", primary: true);
        back.Click += (_, _) => NavigateTo("optimization");
        actions.Children.Add(back);
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
        text.SetValue(TextBlock.FontSizeProperty, 12.0);
        text.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
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
