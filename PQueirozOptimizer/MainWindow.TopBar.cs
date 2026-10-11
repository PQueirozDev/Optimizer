using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Barra superior: sino de alertas (os mesmos avisos reais do Início: restauração pendente do Modo Jogo, disco
/// cheio, licença vencendo, atualização, Modo Jogo ativo) e as ações de cada página no cabeçalho.
/// </summary>
public partial class MainWindow
{
    /// <summary>Atualiza a bolinha vermelha do sino com os alertas da leitura mais recente.</summary>
    private void RefreshAlertsDot()
    {
        var snapshot = _snapshot;
        var count = snapshot is null ? 0 : CommandAlerts(snapshot).Count(a => a.Tone is "Danger" or "Warning");
        AlertsDot.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AlertsButton.ToolTip = count switch { 0 => Translator.Tr("Alertas"), 1 => Translator.Tr("1 alerta"), _ => string.Format(Translator.Tr("{0} alertas"), count) };
    }

    private async void AlertsButton_Click(object sender, RoutedEventArgs e)
    {
        AlertsList.Children.Clear();
        var head = new TextBlock { Text = Translator.Tr("Alertas"), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 4, 8, 8) };
        head.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        AlertsList.Children.Add(head);
        AlertsPopup.IsOpen = true;
        SystemSnapshot snapshot;
        try { snapshot = await ReadSnapshotAsync(); }
        catch (Exception ex)
        {
            _log.Write("WARN", "Alertas: leitura do sistema falhou: " + ex.Message);
            AlertsList.Children.Add(AlertRow(Glyphs.Warning, "Não foi possível ler o sistema agora.", "Warning", null));
            return;
        }
        var alerts = CommandAlerts(snapshot);
        if (alerts.Count == 0) AlertsList.Children.Add(AlertRow(Glyphs.Check, "Nenhum alerta. Tudo certo por aqui.", "Success", null));
        foreach (var (text, tone, page) in alerts)
            AlertsList.Children.Add(AlertRow(tone switch { "Danger" => Glyphs.Error, "Warning" => Glyphs.Warning, _ => Glyphs.Info }, text, tone, page));
        RefreshAlertsDot();
    }

    private Border AlertRow(string glyph, string text, string tone, string? page)
    {
        var row = new DockPanel();
        var chip = IconChip(glyph, tone, 30); DockPanel.SetDock(chip, Dock.Left); row.Children.Add(chip);
        if (page != null)
        {
            var arrow = GlyphIcon(Glyphs.ChevronRight, 10, "MutedBrush"); DockPanel.SetDock(arrow, Dock.Right); row.Children.Add(arrow);
        }
        var label = new TextBlock { Text = Translator.Tr(text), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(11, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        row.Children.Add(label);
        var item = new Border { Child = row, Padding = new Thickness(8), CornerRadius = new CornerRadius(10), Background = System.Windows.Media.Brushes.Transparent };
        if (page != null)
        {
            item.Cursor = Cursors.Hand;
            item.MouseEnter += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "NavHoverBrush");
            item.MouseLeave += (_, _) => item.Background = System.Windows.Media.Brushes.Transparent;
            item.MouseLeftButtonUp += (_, _) => { AlertsPopup.IsOpen = false; NavigateTo(page); };
        }
        return item;
    }

    /// <summary>Botão das ações da página no cabeçalho (ex.: Atualizar no Início).</summary>
    private Button AddPageAction(string glyph, string text, Action click, bool primary = false)
    {
        var button = IconButton(glyph, text, primary);
        button.Margin = new Thickness(8, 0, 0, 0);
        button.Padding = new Thickness(13, 7, 13, 7);
        QrzUi.SetCornerRadius(button, new CornerRadius(17));
        button.Click += (_, _) => click();
        PageActions.Children.Add(button);
        return button;
    }

    /// <summary>Selo "● Ao vivo" do cabeçalho: indica que os números da página são atualizados a cada segundo.</summary>
    private void AddLiveBadge()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(StatusDot("Success", 7));
        var t = new TextBlock { Text = Translator.Tr("Ao vivo"), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        content.Children.Add(t);
        var badge = new Border { Child = content, Padding = new Thickness(12, 7, 13, 7), CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Translator.Tr("CPU, placa de vídeo, memória e rede lidas a cada segundo") };
        badge.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        badge.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        PageActions.Children.Add(badge);
    }
}
