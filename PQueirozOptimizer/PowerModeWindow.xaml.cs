using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Seletor rápido de modo de energia. Abre pelo atalho da Área de Trabalho (sem administrador)
/// ou por Ferramentas. Nos notebooks, o modo de energia do Windows é o que mais pesa na bateria.
/// </summary>
public partial class PowerModeWindow : Window
{
    private readonly ActivityLog _log;
    private readonly bool _closeAfterChoice;

    public PowerModeWindow(ActivityLog log, bool closeAfterChoice)
    {
        InitializeComponent();
        _log = log;
        _closeAfterChoice = closeAfterChoice;
        Loaded += (_, _) =>
        {
            Render();
            // Aberto pelo atalho, o processo novo nem sempre ganha o foco: traz a janela para a frente
            Activate(); Topmost = true; Topmost = false;
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void Render()
    {
        var source = PowerModeService.GetSource();
        SourceText.Text = PowerModeService.Describe(source);
        HeaderIcon.Text = source.HasBattery && source.PluggedIn ? Glyphs.BatteryCharging : Glyphs.Battery;

        var active = PowerModeService.GetActivePlan();
        var plans = PowerModeService.GetPlans();
        var mode = PowerModeService.GetMode();
        var modesAvailable = mode != null && plans.Any(p => p.Id == PowerModeService.BalancedPlan);

        ModesPanel.Children.Clear();
        ModesHeader.Visibility = ModesPanel.Visibility = modesAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (modesAvailable)
        {
            var onBalanced = active == PowerModeService.BalancedPlan;
            foreach (var (value, glyph, title, subtitle) in new[]
            {
                (PowerMode.Efficiency, Glyphs.SpeedLow, "Melhor eficiência energética", "Mais bateria e menos calor; o desempenho diminui."),
                (PowerMode.Balanced, Glyphs.SpeedMedium, "Equilibrado", "Padrão do Windows: equilibra desempenho e consumo."),
                (PowerMode.Performance, Glyphs.Speed, "Melhor desempenho", "Mais desempenho em jogos e programas pesados; ideal na tomada."),
            })
            {
                ModesPanel.Children.Add(Option(glyph, title, subtitle, onBalanced && mode == value, () => PowerModeService.SetMode(value)));
            }
        }

        // Com o modo de energia disponível, o Equilibrado já aparece acima como "Equilibrado"
        var otherPlans = plans.Where(p => !modesAvailable || p.Id != PowerModeService.BalancedPlan).ToList();
        PlansPanel.Children.Clear();
        PlansHeader.Visibility = PlansPanel.Visibility = otherPlans.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var plan in otherPlans)
        {
            var subtitle = plan.Name.Contains("PQueiroz", StringComparison.OrdinalIgnoreCase) ? "Criado pelo PQueiroz Optimizer" : "Plano de energia instalado neste PC";
            PlansPanel.Children.Add(Option(Glyphs.Power, plan.Name, subtitle, active == plan.Id, () => PowerModeService.SetActivePlan(plan.Id)));
        }
    }

    private Button Option(string glyph, string title, string subtitle, bool active, Action apply)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var chip = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Child = GlyphIcon(glyph, 15, active ? "OnAccentBrush" : "AccentBrush") };
        chip.SetResourceReference(Border.BackgroundProperty, active ? "AccentGradientBrush" : "AccentSoftBrush");
        grid.Children.Add(chip);

        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var detail = new TextBlock { Text = subtitle, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        text.Children.Add(detail);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (active)
        {
            var check = GlyphIcon(Glyphs.Check, 14, "AccentBrush");
            Grid.SetColumn(check, 2);
            grid.Children.Add(check);
        }

        var button = new Button { Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8), ToolTip = title };
        if (active)
        {
            button.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
            button.SetResourceReference(Control.BackgroundProperty, "AccentSoftBrush");
        }
        button.Click += (_, _) => Choose(apply, title);
        return button;
    }

    private async void Choose(Action apply, string title)
    {
        try
        {
            apply();
            _log.Write("SUCCESS", "Modo de energia alterado: " + title);
            Render();
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            StatusText.Text = "Ativado: " + title;
            if (_closeAfterChoice)
            {
                await Task.Delay(900);
                Close();
            }
        }
        catch (InvalidOperationException ex)
        {
            _log.Write("ERROR", "Modo de energia: " + ex.Message);
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            StatusText.Text = ex.Message;
        }
    }

    private static TextBlock GlyphIcon(string glyph, double size, string brushKey)
    {
        var icon = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return icon;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
