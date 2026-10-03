using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Configurações → Aparência: tema, intensidade do roxo, densidade, tamanho dos cards e animações.</summary>
public partial class MainWindow
{
    private Border AppearanceSection()
    {
        var current = AppearanceService.Current;
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Aparência", "Personalize o visual do PQueiroz Optimizer. Só muda a aparência: nenhuma função é alterada."));

        void Update(Action<AppearanceSettings> change)
        {
            var next = AppearanceService.Current.Clone();
            change(next);
            SaveAppearance(next);
        }

        panel.Children.Add(SettingRow("Tema", "Automático segue o modo de aplicativo do Windows (claro ou escuro).",
            Segmented(new[] { "Escuro", "OLED", "Automático" }, (int)current.Theme, i => Update(s => s.Theme = (ThemeMode)i))));
        panel.Children.Add(SettingRow("Intensidade do roxo", "Botões, seleção da barra lateral, gráficos, indicadores e brilhos.",
            Segmented(new[] { "Suave", "Padrão", "Vibrante" }, (int)current.Accent, i => Update(s => s.Accent = (AccentIntensity)i))));
        panel.Children.Add(SettingRow("Densidade", "Espaço entre os elementos e dentro dos cards.",
            Segmented(new[] { "Compacta", "Padrão", "Confortável" }, (int)current.Density, i => Update(s => s.Density = (Density)i))));
        panel.Children.Add(SettingRow("Tamanho dos cards", "Cards de monitoramento, hardware e das outras páginas.",
            Segmented(new[] { "Compacto", "Médio", "Grande" }, (int)current.CardSize, i => Update(s => s.CardSize = (CardSize)i))));

        var animations = new CheckBox { IsChecked = current.Animations, Content = "" };
        animations.SetResourceReference(StyleProperty, "SwitchCheckBox");
        animations.Checked += (_, _) => Update(s => s.Animations = true);
        animations.Unchecked += (_, _) => Update(s => s.Animations = false);
        const string animationsHint = "Entrada das páginas, movimento dos cards, brilhos e fundo animado. O monitor ao vivo e os gráficos continuam atualizando.";
        panel.Children.Add(SettingRow("Animações da interface", animationsHint, animations));

        panel.Children.Add(Divider());
        var previewTitle = Label("Pré-visualização", 13); previewTitle.FontWeight = FontWeights.SemiBold; previewTitle.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(previewTitle);
        panel.Children.Add(AppearancePreview());
        panel.Children.Add(Divider());

        var restore = IconButton(Glyphs.Undo, "Restaurar padrão");
        restore.HorizontalAlignment = HorizontalAlignment.Left;
        restore.IsEnabled = !IsDefaultAppearance(current);
        restore.Click += (_, _) => { SaveAppearance(AppearanceService.Defaults()); ShowToast("Aparência", "Padrão restaurado: Escuro, roxo Padrão, densidade Padrão, cards Médios e animações ligadas.", "Success"); };
        panel.Children.Add(restore);

        var card = Surface(panel);
        card.Margin = new Thickness(0, 0, 0, 24);
        return card;
    }

    private static bool IsDefaultAppearance(AppearanceSettings s) =>
        s.Theme == ThemeMode.Dark && s.Accent == AccentIntensity.Default && s.Density == Density.Default && s.CardSize == CardSize.Medium && s.Animations;

    /// <summary>Linha de configuração: título e explicação à esquerda, controle à direita.</summary>
    private Grid SettingRow(string title, string detail, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, AppearanceService.Space(14)) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        var t = Label(title, 13.5); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0);
        var d = Label(detail, 12, true); d.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(t); text.Children.Add(d);
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1); grid.Children.Add(control);
        return grid;
    }

    /// <summary>Controle segmentado: opções lado a lado, a escolhida em destaque suave com contorno da cor principal.</summary>
    private Border Segmented(string[] options, int selected, Action<int> onSelect)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < options.Length; i++)
        {
            var index = i;
            var isSelected = i == selected;
            var label = new TextBlock { Text = options[i], FontSize = 12.5, FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium };
            label.SetResourceReference(TextBlock.ForegroundProperty, isSelected ? "TextBrush" : "MutedBrush");
            var button = new Button { Content = label, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, i == options.Length - 1 ? 0 : 2, 0), MinWidth = 86, ToolTip = options[i] };
            button.SetResourceReference(Button.BackgroundProperty, isSelected ? "AccentSoftBrush" : "SurfaceSecondaryBrush");
            button.SetResourceReference(Button.BorderBrushProperty, isSelected ? "AccentBrush" : "SurfaceSecondaryBrush");
            button.Click += (_, _) => { if (index != selected) onSelect(index); };
            row.Children.Add(button);
        }
        var host = new Border { Child = row, Padding = new Thickness(3), CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1) };
        host.SetResourceReference(Border.BackgroundProperty, "SurfaceSecondaryBrush");
        host.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return host;
    }

    private Border Divider()
    {
        var line = new Border { Height = 1, Margin = new Thickness(0, AppearanceService.Space(8), 0, AppearanceService.Space(16)) };
        line.SetResourceReference(Border.BackgroundProperty, "BorderSubtleBrush");
        return line;
    }

    /// <summary>Exemplos fixos (não são leituras reais) desenhados com os mesmos componentes da Visão geral.</summary>
    private UIElement AppearancePreview()
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -AppearanceService.Gap, 0) };
        grid.Children.Add(MetricCard("CPU", "36%", "Exemplo", "Accent", Glyphs.Chip, PreviewSeries(36, 1)));
        grid.Children.Add(MetricCard("MEMÓRIA", "73%", "Exemplo", "Info", Glyphs.Memory, PreviewSeries(73, 2)));
        var health = new StackPanel();
        var head = new DockPanel();
        var ring = ScoreRing(100, "", 70 * AppearanceService.CardScale);
        DockPanel.SetDock(ring, Dock.Right); head.Children.Add(ring);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var l = new TextBlock { Text = "SAÚDE", FontSize = 10.5, FontWeight = FontWeights.SemiBold }; l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var v = new TextBlock { Text = "100", FontSize = 26 * AppearanceService.CardScale, FontWeight = FontWeights.Bold, Tag = Translator.SystemDataTag };
        v.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont"); v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        text.Children.Add(l); text.Children.Add(v);
        head.Children.Add(text);
        health.Children.Add(head);
        grid.Children.Add(MetricShell(health, "Success"));
        return grid;
    }

    private static double[] PreviewSeries(double level, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, 40).Select(i => Math.Clamp(level + Math.Sin(i / 4.0 + seed) * 8 + random.NextDouble() * 6 - 3, 2, 98)).ToArray();
    }

    // ================= Componentes de métrica (MetricCard + Sparkline) =================
    /// <summary>Card de métrica: rótulo, valor em destaque, detalhe e gráfico do último minuto.</summary>
    private Border MetricCard(string title, string value, string detail, string tone, string glyph, IReadOnlyList<double> series, double max = 100)
    {
        var scale = AppearanceService.CardScale;
        var stack = new StackPanel();
        var top = new DockPanel();
        var chip = IconChip(glyph, tone, 26 * scale); DockPanel.SetDock(chip, Dock.Right); top.Children.Add(chip);
        var label = new TextBlock { Text = title, FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        top.Children.Add(label);
        stack.Children.Add(top);
        var v = new TextBlock { Text = value, FontSize = 26 * scale, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0), Tag = Translator.SystemDataTag };
        v.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(v);
        var d = new TextBlock { Text = detail, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(d);
        stack.Children.Add(Sparkline(series, tone, max, 34 * scale));
        return MetricShell(stack, tone);
    }

    private Border MetricShell(UIElement content, string tone)
    {
        var card = new Border { Child = content, Padding = new Thickness(AppearanceService.Space(14) * AppearanceService.CardScale), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, AppearanceService.Gap, 0), Tag = tone };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceSecondaryBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        AddHoverOutline(card);
        return card;
    }

    /// <summary>Gráfico de linha leve (sem grade nem legenda) com área preenchida suave.</summary>
    private static Canvas Sparkline(IReadOnlyList<double> values, string tone, double max, double height)
    {
        var canvas = new Canvas { Height = height, Margin = new Thickness(0, 10, 0, 0), ClipToBounds = true };
        void Draw()
        {
            canvas.Children.Clear();
            var w = canvas.ActualWidth;
            if (w <= 0 || values.Count < 2) return;
            var step = w / (values.Count - 1);
            var line = new System.Windows.Shapes.Polyline { StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
            var area = new System.Windows.Shapes.Polygon();
            area.Points.Add(new Point(0, height));
            for (var i = 0; i < values.Count; i++)
            {
                var p = new Point(i * step, height - Math.Clamp(values[i] / max, 0, 1) * (height - 2) - 1);
                line.Points.Add(p); area.Points.Add(p);
            }
            area.Points.Add(new Point(w, height));
            line.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, tone + "Brush");
            area.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, tone + "SoftBrush");
            canvas.Children.Add(area); canvas.Children.Add(line);
        }
        canvas.SizeChanged += (_, _) => Draw();
        return canvas;
    }

    // ================= Tutoriais =================
    private Border TutorialsSection()
    {
        var dock = new DockPanel();
        var replay = IconButton(Glyphs.Play, "Rever tutoriais");
        replay.Click += (_, _) => ReplayTutorials();
        DockPanel.SetDock(replay, Dock.Right); dock.Children.Add(replay);
        dock.Children.Add(FeatureHeader(Glyphs.Lightning, "Accent", "Tutoriais", "Mostra de novo o tour da primeira abertura e libera os tutoriais curtos de cada página (Recursos, Modo Jogo, Rede, Serviços e BIOS).", "Guia", "Info"));
        var card = Surface(dock);
        card.Margin = new Thickness(0, 0, 0, 24);
        return card;
    }
}
