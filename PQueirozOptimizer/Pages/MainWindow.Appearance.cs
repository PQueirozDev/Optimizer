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
        panel.Children.Add(SectionHeader("Aparência", "Personalize o visual do Qrztweaks. Só muda a aparência: nenhuma função é alterada."));

        void Update(Action<AppearanceSettings> change)
        {
            var next = AppearanceService.Current.Clone();
            change(next);
            SaveAppearance(next);
        }

        var themeTitle = Label("Tema", 13.5); themeTitle.FontWeight = FontWeights.SemiBold; themeTitle.Margin = new Thickness(0);
        panel.Children.Add(themeTitle);
        var themeHint = Label("Automático segue o modo de aplicativo do Windows (claro ou escuro).", 12, true); themeHint.Margin = new Thickness(0, 2, 0, 10);
        panel.Children.Add(themeHint);
        panel.Children.Add(ThemeGallery(current.Theme, theme => Update(s => s.Theme = theme)));

        panel.Children.Add(SettingRow("Cor principal", "Botões, seleção da barra lateral e gráficos.",
            ColorPicker(current.AccentColor, AccentChoices, hex => Update(s => s.AccentColor = hex))));
        panel.Children.Add(SettingRow("Cor secundária", "Cor do anel de saúde e dos indicadores.",
            ColorPicker(current.SecondaryColor, SecondaryChoices, hex => Update(s => s.SecondaryColor = hex))));
        panel.Children.Add(SettingRow("Intensidade da cor", "Suave deixa as cores mais discretas; Vibrante, mais vivas.",
            Segmented(new[] { "Suave", "Padrão", "Vibrante" }, (int)current.Accent, i => Update(s => s.Accent = (AccentIntensity)i))));
        panel.Children.Add(SettingRow("Densidade", "Espaço entre os elementos e dentro dos cards.",
            Segmented(new[] { "Compacta", "Padrão", "Confortável" }, (int)current.Density, i => Update(s => s.Density = (Density)i))));
        panel.Children.Add(SettingRow("Tamanho dos cards", "Cards de monitoramento, hardware e das outras páginas.",
            Segmented(new[] { "Compacto", "Médio", "Grande" }, (int)current.CardSize, i => Update(s => s.CardSize = (CardSize)i))));

        var animations = new CheckBox { IsChecked = current.Animations, Content = "" };
        animations.SetResourceReference(StyleProperty, "SwitchCheckBox");
        animations.Checked += (_, _) => Update(s => s.Animations = true);
        animations.Unchecked += (_, _) => Update(s => s.Animations = false);
        const string animationsHint = "Entrada das páginas e transições. O monitor ao vivo e os gráficos continuam atualizando.";
        panel.Children.Add(SettingRow("Animações da interface", animationsHint, animations));
        panel.Children.Add(SettingRow("Seguir a redução de movimento do Windows",
            "Desliga as animações quando \"Mostrar animações no Windows\" estiver desligado. Fica desligado por padrão porque o ajuste de efeitos visuais do próprio app desliga essa opção do Windows.",
            Switch(current.FollowSystemMotion, on => Update(s => s.FollowSystemMotion = on))));
        panel.Children.Add(SettingRow("Barra lateral compacta", "Mostra só os ícones; o nome de cada página aparece ao passar o mouse. Também alterna com Ctrl+B.",
            Switch(current.CompactSidebar, on => Update(s => s.CompactSidebar = on))));

        var dashTitle = Label("Command Center", 13.5); dashTitle.FontWeight = FontWeights.SemiBold; dashTitle.Margin = new Thickness(0, 8, 0, 2);
        panel.Children.Add(dashTitle);
        panel.Children.Add(Label("Escolha o que aparece na página inicial.", 12, true));
        panel.Children.Add(SettingRow("Status e alertas", "Otimizações ativas, última análise, último teste e alertas.", Switch(current.DashboardStatus, on => Update(s => s.DashboardStatus = on))));
        panel.Children.Add(SettingRow("Ações rápidas", "Smart Optimize, Modo Jogo, Performance Lab, Restaurar e Diagnóstico.", Switch(current.DashboardQuickActions, on => Update(s => s.DashboardQuickActions = on))));
        panel.Children.Add(SettingRow("Monitor em tempo real", "Gráficos de CPU, GPU, memória e rede.", Switch(current.DashboardLive, on => Update(s => s.DashboardLive = on))));
        panel.Children.Add(SettingRow("Cards de hardware", "Processador, memória, disco, placa de vídeo e temperaturas.", Switch(current.DashboardHardware, on => Update(s => s.DashboardHardware = on))));
        panel.Children.Add(SettingRow("Atividade recente", "Leitura rápida e as últimas operações.", Switch(current.DashboardActivity, on => Update(s => s.DashboardActivity = on))));

        var translucent = new CheckBox { IsChecked = current.Translucent && ThemeService.TranslucencySupported, Content = "", IsEnabled = ThemeService.TranslucencySupported };
        translucent.SetResourceReference(StyleProperty, "SwitchCheckBox");
        var reverting = false;
        translucent.Checked += (_, _) =>
        {
            if (reverting) return;
            if (!ConfirmSystemTransparency()) { reverting = true; translucent.IsChecked = false; reverting = false; return; }
            Update(s => s.Translucent = true);
        };
        translucent.Unchecked += (_, _) => { if (!reverting) Update(s => s.Translucent = false); };
        panel.Children.Add(SettingRow("Janela translúcida", ThemeService.TranslucencySupported
            ? "O fundo da janela fica desfocado e deixa ver o que está atrás, como nos apps do Windows 11. Se precisar, liga também os efeitos de transparência do Windows (o app avisa antes)."
            : "Disponível só no Windows 11 (versão 22H2 ou mais nova).", translucent));
        if (ThemeService.TranslucencySupported && current.Translucent && !ThemeService.SystemTransparencyEnabled)
        {
            // Os efeitos foram desligados depois (no Windows ou por outro programa): explica e oferece religar
            var warning = new DockPanel { Margin = new Thickness(0, -4, 0, AppearanceService.Space(14)) };
            var enable = IconButton(Glyphs.Check, "Ligar efeitos de transparência");
            enable.Margin = new Thickness(12, 0, 0, 0);
            enable.Click += (_, _) => ConfirmSystemTransparency();
            DockPanel.SetDock(enable, Dock.Right); warning.Children.Add(enable);
            var icon = GlyphIcon(Glyphs.Warning, 14, "WarningBrush"); icon.Margin = new Thickness(0, 0, 10, 0); icon.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(icon, Dock.Left); warning.Children.Add(icon);
            var text = Label("Os efeitos de transparência estão desligados no Windows, então a janela continua sólida.", 12, true);
            text.Margin = new Thickness(0);
            warning.Children.Add(text);
            panel.Children.Add(warning);
        }

        panel.Children.Add(Divider());
        var previewTitle = Label("Pré-visualização", 13); previewTitle.FontWeight = FontWeights.SemiBold; previewTitle.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(previewTitle);
        panel.Children.Add(AppearancePreview());
        panel.Children.Add(Divider());

        var restore = IconButton(Glyphs.Undo, "Restaurar padrão");
        restore.HorizontalAlignment = HorizontalAlignment.Left;
        restore.IsEnabled = !IsDefaultAppearance(current);
        restore.Click += (_, _) => { SaveAppearance(AppearanceService.Defaults()); ShowToast("Aparência", "Padrão restaurado: tema Escuro, cores originais, densidade Padrão, cards Médios e animações ligadas.", "Success"); };
        panel.Children.Add(restore);

        var card = Surface(panel);
        card.Margin = new Thickness(0, 0, 0, 24);
        return card;
    }

    /// <summary>
    /// O translúcido depende dos efeitos de transparência do Windows. Se estiverem desligados, avisa que
    /// a opção vale para o sistema todo e só liga com a confirmação. Devolve se pode seguir.
    /// </summary>
    private bool ConfirmSystemTransparency()
    {
        if (ThemeService.SystemTransparencyEnabled) return true;
        var answer = Msg("Para a janela ficar translúcida, o app vai ligar os \"Efeitos de transparência\" do Windows (Personalização → Cores).\n\n"
            + "Isso vale para o Windows todo: a barra de tarefas, o menu Iniciar e outros apps também ficam translúcidos. Dá para desligar depois em Personalização → Cores.\n\nLigar agora?",
            "Efeitos de transparência", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return false;
        if (ThemeService.EnableSystemTransparency()) return true;
        Msg("Não foi possível ligar os efeitos de transparência. Ligue manualmente em Configurações do Windows → Personalização → Cores.",
            "Efeitos de transparência", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private CheckBox Switch(bool value, Action<bool> changed)
    {
        var box = new CheckBox { IsChecked = value, Content = "" };
        box.SetResourceReference(StyleProperty, "SwitchCheckBox");
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        return box;
    }

    private static bool IsDefaultAppearance(AppearanceSettings s) =>
        s.Theme == ThemeMode.Dark && s.Accent == AccentIntensity.Default && s.Density == Density.Default && s.CardSize == CardSize.Medium && s.Animations && !s.Translucent
        && string.IsNullOrEmpty(s.AccentColor) && string.IsNullOrEmpty(s.SecondaryColor);

    // ================= Temas e cores =================
    /// <summary>Ordem e nomes dos temas na galeria.</summary>
    internal static readonly (ThemeMode Mode, string Name)[] Themes =
    {
        (ThemeMode.Dark, "Escuro"), (ThemeMode.Oled, "OLED"), (ThemeMode.Light, "Claro"), (ThemeMode.Graphite, "Grafite"),
        (ThemeMode.Ocean, "Oceano"), (ThemeMode.Forest, "Floresta"), (ThemeMode.Plum, "Ameixa"), (ThemeMode.Midnight, "Meia-noite"), (ThemeMode.Sand, "Areia"), (ThemeMode.Auto, "Automático"),
    };

    // A primeira de cada lista (null) é a cor original do app
    private static readonly (string? Hex, string Name, string Preview)[] AccentChoices =
    {
        (null, "Roxo (padrão)", "#8B5CF6"), ("#3B82F6", "Azul", "#3B82F6"), ("#06B6D4", "Ciano", "#06B6D4"), ("#10B981", "Verde", "#10B981"),
        ("#F97316", "Laranja", "#F97316"), ("#EC4899", "Rosa", "#EC4899"), ("#EF4444", "Vermelho", "#EF4444"), ("#EAB308", "Dourado", "#EAB308"),
    };
    private static readonly (string? Hex, string Name, string Preview)[] SecondaryChoices =
    {
        (null, "Ciano (padrão)", "#22D3EE"), ("#60A5FA", "Azul", "#60A5FA"), ("#A78BFA", "Lilás", "#A78BFA"), ("#34D399", "Verde", "#34D399"),
        ("#F472B6", "Rosa", "#F472B6"), ("#FB923C", "Laranja", "#FB923C"), ("#FACC15", "Amarelo", "#FACC15"),
    };

    /// <summary>Galeria de temas: miniatura com fundo, cartão, texto e a cor principal de cada tema.</summary>
    private FrameworkElement ThemeGallery(ThemeMode selected, Action<ThemeMode> onSelect)
    {
        var grid = Responsive(new System.Windows.Controls.Primitives.UniformGrid { Margin = new Thickness(0, 0, -10, AppearanceService.Space(16)) }, 132, 9);
        var accent = (Color)(Application.Current.Resources["AccentColor"] ?? Color.FromRgb(0x8B, 0x5C, 0xF6));
        foreach (var (mode, name) in Themes)
        {
            var isSelected = mode == selected;
            var preview = new Grid { Height = 64, ClipToBounds = true };
            if (mode == ThemeMode.Auto)
            {
                // Metade clara, metade escura
                var (lb, ls, _) = ThemeService.Swatch(ThemeMode.Light); var (db, dsurf, _) = ThemeService.Swatch(ThemeMode.Dark);
                var split = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                split.GradientStops.Add(new GradientStop(lb, 0.5)); split.GradientStops.Add(new GradientStop(db, 0.5));
                preview.Children.Add(new Border { Background = split, CornerRadius = new CornerRadius(9) });
                preview.Children.Add(new Border { Background = new SolidColorBrush(ls), CornerRadius = new CornerRadius(5), Width = 34, Height = 22, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 10, 0, 0) });
                preview.Children.Add(new Border { Background = new SolidColorBrush(dsurf), CornerRadius = new CornerRadius(5), Width = 34, Height = 22, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 10) });
            }
            else
            {
                var (bg, surface, text) = ThemeService.Swatch(mode);
                preview.Children.Add(new Border { Background = new SolidColorBrush(bg), CornerRadius = new CornerRadius(9) });
                var card = new Border { Background = new SolidColorBrush(surface), CornerRadius = new CornerRadius(6), Margin = new Thickness(10, 10, 10, 10), Padding = new Thickness(8, 7, 8, 7) };
                var lines = new StackPanel();
                lines.Children.Add(new Border { Height = 5, Width = 40, CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(text), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.9 });
                lines.Children.Add(new Border { Height = 4, Width = 56, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(text), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.35, Margin = new Thickness(0, 5, 0, 0) });
                lines.Children.Add(new Border { Height = 7, Width = 28, CornerRadius = new CornerRadius(3.5), Background = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) });
                card.Child = lines;
                preview.Children.Add(card);
            }
            if (isSelected)
            {
                var tick = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0), Child = GlyphIcon(Glyphs.Check, 9, "OnAccentBrush") };
                tick.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                preview.Children.Add(tick);
            }
            var label = new TextBlock { Text = name, FontSize = 12.5, FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium, Margin = new Thickness(2, 8, 0, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, isSelected ? "TextBrush" : "MutedBrush");
            var content = new StackPanel(); content.Children.Add(preview); content.Children.Add(label);
            var tile = new Button { Content = content, Padding = new Thickness(6, 6, 6, 8), Margin = new Thickness(0, 0, 10, 10), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = name };
            tile.SetResourceReference(Button.BackgroundProperty, isSelected ? "AccentSoftBrush" : "SurfaceSecondaryBrush");
            tile.SetResourceReference(Button.BorderBrushProperty, isSelected ? "AccentBrush" : "BorderSubtleBrush");
            tile.BorderThickness = new Thickness(isSelected ? 1.5 : 1);
            var chosen = mode;
            tile.Click += (_, _) => { if (chosen != selected) onSelect(chosen); };
            grid.Children.Add(tile);
        }
        return grid;
    }

    /// <summary>Amostras de cor prontas e um campo para uma cor própria (#RRGGBB).</summary>
    private FrameworkElement ColorPicker(string? current, (string? Hex, string Name, string Preview)[] choices, Action<string?> onPick)
    {
        var normalized = ThemeService.ParseColor(current) is { } parsed ? ThemeService.Hex(parsed) : null;
        var isPreset = normalized is null || choices.Any(c => string.Equals(c.Hex, normalized, StringComparison.OrdinalIgnoreCase));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        Button Swatch(Color color, string name, bool selected, Action click)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(color) };
            var ring = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(2), Child = dot };
            ring.SetResourceReference(Border.BorderBrushProperty, selected ? "TextBrush" : "SurfaceSecondaryBrush");
            var b = new Button { Content = ring, Padding = new Thickness(1), Margin = new Thickness(0, 0, 4, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = name };
            b.Click += (_, _) => click();
            return b;
        }
        foreach (var (hex, name, preview) in choices)
        {
            var selected = isPreset && string.Equals(hex, normalized, StringComparison.OrdinalIgnoreCase);
            var value = hex;
            row.Children.Add(Swatch(ThemeService.ParseColor(preview)!.Value, name, selected, () => { if (!selected) onPick(value); }));
        }
        // Cor própria: aparece como amostra extra quando escolhida
        if (!isPreset && ThemeService.ParseColor(normalized) is { } custom)
            row.Children.Add(Swatch(custom, "Personalizada " + normalized, true, () => { }));

        var input = new TextBox { Width = 92, Height = 32, Padding = new Thickness(8, 0, 8, 0), Text = isPreset ? "" : normalized ?? "", Margin = new Thickness(8, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Código da cor, por exemplo #FF6A00", MaxLength = 7 };
        var hint = new TextBlock { Text = "#RRGGBB", IsHitTestVisible = false, Margin = new Thickness(17, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Tag = Translator.SystemDataTag };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        hint.Visibility = string.IsNullOrEmpty(input.Text) ? Visibility.Visible : Visibility.Collapsed;
        input.TextChanged += (_, _) => hint.Visibility = string.IsNullOrEmpty(input.Text) ? Visibility.Visible : Visibility.Collapsed;
        var field = new Grid(); field.Children.Add(input); field.Children.Add(hint);
        var apply = IconButton(Glyphs.Check, "Usar");
        apply.Margin = new Thickness(0);
        void Submit()
        {
            if (ThemeService.ParseColor(input.Text) is { } color) onPick(ThemeService.Hex(color));
            else ShowToast("Cor personalizada", "Use o formato #RRGGBB, por exemplo #FF6A00.", "Warning");
        }
        apply.Click += (_, _) => Submit();
        input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Submit(); };
        row.Children.Add(field);
        row.Children.Add(apply);
        return row;
    }

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
        var l = new TextBlock { Text = "SAÚDE", FontSize = 11, FontWeight = FontWeights.SemiBold }; l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var v = new TextBlock { Text = "100", FontSize = 24 * AppearanceService.CardScale, FontWeight = FontWeights.Bold, Tag = Translator.SystemDataTag };
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
        var label = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        top.Children.Add(label);
        stack.Children.Add(top);
        var v = new TextBlock { Text = value, FontSize = 24 * scale, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0), Tag = Translator.SystemDataTag };
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
