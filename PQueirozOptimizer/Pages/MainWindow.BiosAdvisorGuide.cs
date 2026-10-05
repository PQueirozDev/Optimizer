using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PQueirozOptimizer.BiosAdvisor;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// "Ver como configurar": caminho na BIOS da fabricante, valor alvo, como desfazer e a recuperação se o PC não ligar.
/// Itens de risco alto pedem uma confirmação antes de mostrar os passos. Caminho sem fonte nunca é inventado.
/// </summary>
public partial class MainWindow
{
    private void ShowAdvisorGuide(AdvisorReport report, AdvisorRecommendation rec, bool riskAccepted = false)
    {
        AdvisorLayer.Children.Clear();
        var shade = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x02, 0x03, 0x08)) };
        shade.MouseLeftButtonDown += (_, _) => CloseAdvisorGuide();
        AdvisorLayer.Children.Add(shade);

        var card = new Border { Width = 640, MaxHeight = 780, Margin = new Thickness(24), CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        card.Effect = new DropShadowEffect { BlurRadius = 48, ShadowDepth = 12, Opacity = 0.55, Color = Colors.Black };
        var body = new StackPanel { Margin = new Thickness(30, 26, 30, 24) };
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        card.Child = scroll;
        AdvisorLayer.Children.Add(card);

        var guide = rec.Guide;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var close = new Button { Content = GlyphIcon(Glyphs.Cancel, 11, "MutedBrush"), Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Top };
        close.SetResourceReference(StyleProperty, "GhostButton");
        close.Click += (_, _) => CloseAdvisorGuide();
        DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
        var chip = IconChip(Glyphs.Chip, rec.Risk == Level.High ? "Danger" : "Accent", 46); DockPanel.SetDock(chip, Dock.Left); head.Children.Add(chip);
        var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var iface = new TextBlock { Text = guide?.Interface ?? "BIOS / UEFI", FontSize = 11, FontWeight = FontWeights.Bold, Tag = Translator.SystemDataTag };
        iface.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        titles.Children.Add(iface);
        var title = Label(L(rec.Name), 18); title.Margin = new Thickness(0, 2, 0, 0);
        titles.Children.Add(title);
        head.Children.Add(titles);
        body.Children.Add(head);

        // Risco alto: explica e pede confirmação antes dos passos
        if (rec.RequiresExtraConfirmation && !riskAccepted)
        {
            body.Children.Add(Notice(L(rec.Description), "Danger"));
            var warn = Label(T("Esta mudança tem risco ALTO: pode impedir o Windows de iniciar ou exigir recuperação. Continue só se entender as consequências e tiver backup.",
                "This change is HIGH risk: it may stop Windows from booting or require recovery. Only continue if you understand the consequences and have a backup."), 12.5);
            warn.Margin = new Thickness(0, 14, 0, 14);
            body.Children.Add(warn);
            var accept = IconButton(Glyphs.Warning, T("Entendo os riscos, mostrar os passos", "I understand the risks, show the steps"), primary: true);
            accept.HorizontalAlignment = HorizontalAlignment.Left;
            accept.Click += (_, _) => ShowAdvisorGuide(report, rec, riskAccepted: true);
            body.Children.Add(accept);
            OpenAdvisorLayer();
            return;
        }

        body.Children.Add(SectionLabel(T("CAMINHO NA BIOS", "BIOS PATH")));
        if (guide is { HasConfirmedPath: true })
        {
            var steps = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            for (var i = 0; i < guide.Steps.Count; i++)
            {
                if (i > 0) { var arrow = GlyphIcon("", 10, "MutedBrush"); arrow.HorizontalAlignment = HorizontalAlignment.Left; arrow.Margin = new Thickness(14, 3, 0, 3); steps.Children.Add(arrow); }
                steps.Children.Add(StepRow(guide.Steps[i], last: false));
            }
            var arrowEnd = GlyphIcon("", 10, "MutedBrush"); arrowEnd.HorizontalAlignment = HorizontalAlignment.Left; arrowEnd.Margin = new Thickness(14, 3, 0, 3);
            steps.Children.Add(arrowEnd);
            steps.Children.Add(StepRow(L(rec.RecommendedValue), last: true));
            body.Children.Add(steps);
            var source = Label(T("Fonte: ", "Source: ") + guide.Source + T(". Os caminhos variam entre modelos e versões de BIOS.", ". Paths vary between models and BIOS versions."), 11, true);
            source.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(source);
            if (guide.Note is { } note) body.Children.Add(Notice(L(note), "Info"));
        }
        else
        {
            body.Children.Add(Notice(T("Localização exata não confirmada para esta versão da BIOS.", "Exact location not confirmed for this BIOS version."), "Warning"));
            if (guide is { OptionNames.Count: > 0 })
            {
                var names = Label(T("Procure por uma destas opções (o nome varia por fabricante): ", "Look for one of these options (the name varies by manufacturer): ") + string.Join(" · ", guide.OptionNames), 12.5);
                names.Margin = new Thickness(0, 12, 0, 0);
                body.Children.Add(names);
            }
            var target = Label(T("Valor recomendado: ", "Recommended value: ") + L(rec.RecommendedValue), 12.5);
            target.FontWeight = FontWeights.SemiBold;
            body.Children.Add(target);
        }
        // Lido desta BIOS pelo SCEWIN: o nome exato da opção, o valor atual e as opções que ela aceita
        if (rec.BiosValue is { } read)
        {
            var exact = new TextBlock { Text = T($"Nesta BIOS a opção se chama \"{read.Question}\" e está em \"{read.Value}\". Opções: {string.Join(" · ", read.Options)}.",
                $"In this BIOS the option is called \"{read.Question}\" and is set to \"{read.Value}\". Options: {string.Join(" · ", read.Options)}."), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = Translator.SystemDataTag };
            exact.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            var box = new Border { Child = exact, Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 12, 0, 0) };
            box.SetResourceReference(Border.BackgroundProperty, "SuccessSoftBrush");
            body.Children.Add(box);
        }
        var enter = Label(T("Para entrar na BIOS: reinicie e pressione Delete (ou F2) durante a inicialização, ou use \"Reiniciar na BIOS/UEFI\" no topo da página. Salve com F10.",
            "To enter the BIOS: restart and press Delete (or F2) during boot, or use \"Restart into BIOS/UEFI\" at the top of the page. Save with F10."), 12, true);
        enter.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(enter);

        body.Children.Add(SectionLabel(T("COMO DESFAZER", "HOW TO UNDO")));
        var rollback = Label(L(rec.Rollback), 12.5); rollback.Margin = new Thickness(0, 0, 0, 6);
        body.Children.Add(rollback);

        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var done = IconButton(Glyphs.Check, T("Fechar", "Close"), primary: true);
        done.Click += (_, _) => CloseAdvisorGuide();
        DockPanel.SetDock(done, Dock.Right); footer.Children.Add(done);
        var levels = Label(T($"Ganho {LevelName(rec.Gain)} · Risco {LevelName(rec.Risk)} · Impacto térmico {ThermalName(rec.Thermal)}", $"Gain {LevelName(rec.Gain)} · Risk {LevelName(rec.Risk)} · Thermal impact {ThermalName(rec.Thermal)}"), 11.5, true);
        levels.VerticalAlignment = VerticalAlignment.Center; levels.Margin = new Thickness(0);
        footer.Children.Add(levels);
        body.Children.Add(footer);
        OpenAdvisorLayer();
    }

    private TextBlock SectionLabel(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 18, 0, 8) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return t;
    }

    private Border StepRow(string text, bool last)
    {
        var t = new TextBlock { Text = text, FontSize = 12.5, FontWeight = last ? FontWeights.Bold : FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Tag = last ? null : Translator.SystemDataTag };
        t.SetResourceReference(TextBlock.ForegroundProperty, last ? "OnAccentBrush" : "TextBrush");
        var b = new Border { Child = t, Padding = new Thickness(12, 7, 12, 7), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
        if (last) b.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        else b.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        b.SetResourceReference(Border.BorderBrushProperty, last ? "AccentBrush" : "BorderSubtleBrush");
        return b;
    }

    private void OpenAdvisorLayer()
    {
        AdvisorLayer.Visibility = Visibility.Visible;
        PreviewKeyDown -= AdvisorLayerKey;
        PreviewKeyDown += AdvisorLayerKey;
    }

    private void AdvisorLayerKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && AdvisorLayer.Visibility == Visibility.Visible) { CloseAdvisorGuide(); e.Handled = true; }
    }

    private void CloseAdvisorGuide()
    {
        AdvisorLayer.Visibility = Visibility.Collapsed;
        AdvisorLayer.Children.Clear();
        PreviewKeyDown -= AdvisorLayerKey;
    }
}
