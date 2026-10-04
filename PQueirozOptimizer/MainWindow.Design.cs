using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Camada visual: fundo aurora, cabeçalho de cada página, busca rápida (Ctrl+K), notificações e abas.</summary>
public partial class MainWindow
{
    // ================= Fundo aurora =================
    private readonly List<(Ellipse Blob, string ColorKey)> _auroraBlobs = new();

    /// <summary>Três manchas de luz desfocadas que flutuam devagar atrás da interface.</summary>
    private void StartAurora()
    {
        AuroraLayer.Children.Clear();
        _auroraBlobs.Clear();
        // Posição por alinhamento + margem negativa: não depende do tamanho da janela já estar calculado
        var specs = new[]
        {
            ("AuroraA", HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -300, 80, 0), 820d, 26d),
            ("AuroraB", HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(-220, 0, 0, -260), 700d, 32d),
            ("AuroraC", HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, -180, -200), 560d, 38d),
        };
        foreach (var (key, horizontal, vertical, margin, size, seconds) in specs)
        {
            var blob = new Ellipse { Width = size, Height = size * 0.8, IsHitTestVisible = false, HorizontalAlignment = horizontal, VerticalAlignment = vertical, Margin = margin };
            var move = new TranslateTransform();
            blob.RenderTransform = move;
            _auroraBlobs.Add((blob, key));
            AuroraLayer.Children.Add(blob);
            if (!AppearanceService.AnimationsEnabled) continue; // fundo estático com as animações desligadas
            var drift = TimeSpan.FromSeconds(seconds);
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-60, 80, drift) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(40, -50, TimeSpan.FromSeconds(seconds * 0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        }
        RefreshAuroraColors();
    }

    private void RefreshAuroraColors()
    {
        foreach (var (blob, key) in _auroraBlobs)
        {
            var color = Application.Current?.TryFindResource(key) is Color c ? c : Colors.Transparent;
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop(color, 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(color.A / 3), color.R, color.G, color.B), 0.45));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            brush.Freeze();
            blob.Fill = brush;
        }
    }

    // ================= Cabeçalho de cada página =================
    private static readonly Dictionary<string, (string Eyebrow, string Glyph, string Subtitle)> PageHeaders = new()
    {
        ["dashboard"] = ("MONITORAR", Glyphs.Home, "Saúde, desempenho e atividade do seu PC em tempo real."),
        ["restore"] = ("MANUTENÇÃO", Glyphs.Restore2, "Crie e restaure pontos de restauração para proteger o sistema."),
        ["resources"] = ("MANUTENÇÃO", Glyphs.Library, "Automação, atalhos, downloads e testes para preparar qualquer PC."),
        ["fixes"] = ("MANUTENÇÃO", Glyphs.Repair, "Correções rápidas para problemas comuns do Windows."),
        ["services"] = ("OTIMIZAÇÕES", Glyphs.Services, "Desligue serviços e tarefas em segundo plano para liberar o sistema."),
        ["apps"] = ("OTIMIZAÇÕES", Glyphs.Apps, "Ajuste Discord, navegadores e outros apps e remova o que não usa."),
        ["optimization"] = ("OTIMIZAÇÕES", Glyphs.Lightning, "Ajustes revisados item por item, com backup e reversão."),
        ["startup"] = ("OTIMIZAÇÕES", Glyphs.Power, "Programas, tarefas e serviços que iniciam com o Windows."),
        ["drivers"] = ("OTIMIZAÇÕES", Glyphs.Monitor, "Drivers oficiais, instalação limpa com DDU e utilitários."),
        ["isos"] = ("OTIMIZAÇÕES", Glyphs.Disc, "Imagens personalizadas do Windows."),
        ["tools"] = ("OTIMIZAÇÕES", Glyphs.Repair, "Atalhos e utilitários do dia a dia."),
        ["history"] = ("OTIMIZAÇÕES", Glyphs.History, "Tudo o que foi feito e como desfazer."),
        ["diagnostics"] = ("MANUTENÇÃO", Glyphs.Diagnostic, "Uma leitura guiada dos principais componentes do Windows."),
        ["gaming"] = ("JOGOS E REDE", Glyphs.Game, "Sessão de jogo, perfis, NVIDIA, configurações dos jogos e Defender."),
        ["network"] = ("JOGOS E REDE", Glyphs.Network, "Velocidade, DNS, latência e reparos de conexão."),
        ["settings"] = ("SISTEMA", Glyphs.Settings, "Perfis, tema, idioma, licença e atualizações."),
        ["bios"] = ("SISTEMA", Glyphs.Chip, "Editor de BIOS pelo SCEWIN e ajustes recomendados."),
        ["patchnotes"] = ("SISTEMA", Glyphs.Document, "Novidades de cada versão."),
        ["about"] = ("SISTEMA", Glyphs.Info, "Sobre o PQueiroz Optimizer."),
    };

    private void UpdatePageHeader(string page)
    {
        if (!PageHeaders.TryGetValue(page, out var header)) return;
        PageEyebrow.Text = header.Eyebrow;
        PageIcon.Text = header.Glyph;
        PageSubtitle.Text = header.Subtitle;
        // O ícone "pulsa" ao trocar de página
        if (!AppearanceService.AnimationsEnabled) { PageIconChip.RenderTransform = null; return; }
        var scale = new ScaleTransform(0.85, 0.85);
        PageIconChip.RenderTransformOrigin = new Point(0.5, 0.5);
        PageIconChip.RenderTransform = scale;
        var pop = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    // ================= Notificações =================
    /// <summary>Notificação discreta no canto inferior direito que some sozinha.</summary>
    private void ShowToast(string title, string message, string tone = "Success")
    {
        var glyph = tone switch { "Success" => Glyphs.Check, "Danger" => Glyphs.Error, "Warning" => Glyphs.Warning, _ => Glyphs.Info };
        var dock = new DockPanel();
        var chip = IconChip(glyph, tone, 34); chip.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(chip, Dock.Left); dock.Children.Add(chip);
        var close = new Button { Content = GlyphIcon(Glyphs.Cancel, 10, "MutedBrush"), Padding = new Thickness(6), Margin = new Thickness(6, -4, -6, 0), VerticalAlignment = VerticalAlignment.Top };
        close.SetResourceReference(StyleProperty, "GhostButton");
        DockPanel.SetDock(close, Dock.Right); dock.Children.Add(close);
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        var t = new TextBlock { Text = Translator.Tr(title), FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var m = new TextBlock { Text = Translator.Tr(message), FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        m.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        text.Children.Add(t); if (!string.IsNullOrWhiteSpace(message)) text.Children.Add(m);
        dock.Children.Add(text);

        var toast = new Border { Child = dock, Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Margin = new Thickness(0, 10, 0, 0), Opacity = 0 };
        toast.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        toast.SetResourceReference(Border.BorderBrushProperty, tone + "Brush");
        toast.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.4, Color = Colors.Black };
        var slide = new TranslateTransform(40, 0);
        toast.RenderTransform = slide;
        ToastHost.Children.Add(toast);
        while (ToastHost.Children.Count > 4) ToastHost.Children.RemoveAt(0);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (!AppearanceService.AnimationsEnabled) { toast.Opacity = 1; slide.X = 0; }
        else toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        if (AppearanceService.AnimationsEnabled) slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        void Dismiss()
        {
            var fade = new DoubleAnimation(toast.Opacity, 0, TimeSpan.FromMilliseconds(200));
            fade.Completed += (_, _) => ToastHost.Children.Remove(toast);
            toast.BeginAnimation(OpacityProperty, fade);
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 40, TimeSpan.FromMilliseconds(200)));
        }
        close.Click += (_, _) => Dismiss();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(tone == "Danger" ? 8 : 4.5) };
        timer.Tick += (_, _) => { timer.Stop(); if (!toast.IsMouseOver) Dismiss(); else timer.Start(); };
        timer.Start();
    }

    // ================= Abas =================
    /// <summary>Controle segmentado (abas em forma de pílula) com a aba ativa em gradiente.</summary>
    private Border Tabs(IReadOnlyList<(string Glyph, string Title)> tabs, int selected, Action<int> onSelect)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < tabs.Count; i++)
        {
            var index = i;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = GlyphIcon(tabs[i].Glyph, 13, i == selected ? "OnAccentBrush" : "MutedBrush"); icon.Margin = new Thickness(0, 0, 8, 0);
            content.Children.Add(icon);
            var label = new TextBlock { Text = tabs[i].Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, i == selected ? "OnAccentBrush" : "MutedBrush");
            content.Children.Add(label);
            var button = new Button { Content = content, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(0) };
            if (i == selected) button.SetResourceReference(StyleProperty, "PrimaryButton");
            else { button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; }
            button.Click += (_, _) => onSelect(index);
            row.Children.Add(button);
        }
        var bar = new Border { Child = row, Padding = new Thickness(5), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) };
        bar.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        bar.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return bar;
    }

    // ================= Busca rápida (Ctrl+K) =================
    private sealed record PaletteEntry(string Title, string Section, string Glyph, string Keywords, Action Run);
    private List<PaletteEntry>? _palette;
    private int _paletteIndex;
    private List<PaletteEntry> _paletteResults = new();

    private List<PaletteEntry> PaletteEntries() => _palette ??= new List<PaletteEntry>
    {
        new("Visão geral", "Monitorar", Glyphs.Home, "dashboard inicio painel saude monitor", () => NavigateTo("dashboard")),
        new("Otimizações", "Otimizações", Glyphs.Lightning, "ajustes perfil", () => NavigateTo("optimization")),
        new("Versão Padrão", "Otimizações", Glyphs.Speed, "otimizar padrao basico", () => _ = PrepareOperationAsync("padrao")),
        new("Versão Avançada (jogos)", "Otimizações", Glyphs.Game, "gamer latencia fps avancada", () => _ = PrepareOperationAsync("gamer")),
        new("Debloat e privacidade", "Otimizações", Glyphs.Shield, "remover apps telemetria privacidade bloat", () => _ = PrepareOperationAsync("debloat")),
        new("Limpeza rápida", "Otimizações", Glyphs.Broom, "limpar temporarios cache lixo", () => _ = PrepareOperationAsync("quickclean")),
        new("Diagnóstico do PC", "Otimizações", Glyphs.Diagnostic, "analisar analise saude dpc temperatura", () => _ = PrepareOperationAsync("analisar")),
        new("Inicialização", "Otimizações", Glyphs.Power, "startup autoruns programas servicos tarefas", () => NavigateTo("startup")),
        new("Drivers", "Otimizações", Glyphs.Monitor, "driver nvidia amd intel chipset", () => NavigateTo("drivers")),
        new("Instalação limpa de driver (DDU)", "Drivers", Glyphs.Refresh, "ddu display driver uninstaller limpa reinstalar", () => NavigateTo("drivers")),
        new("Ferramentas", "Otimizações", Glyphs.Repair, "utilitarios atalhos all black barra", () => NavigateTo("tools")),
        new("Atividade e reversão", "Otimizações", Glyphs.History, "historico reverter desfazer backup log", () => NavigateTo("history")),
        new("Modo Jogo", "Jogos e Rede", Glyphs.Game, "sessao game mode fechar programas pausar servicos ascension", () => OpenGamingTab(0)),
        new("Plano de energia Qrz", "Jogos e Rede", Glyphs.Battery, "energia power plan qrz latencia", () => OpenGamingTab(3)),
        new("Perfis de jogos", "Jogos e Rede", Glyphs.Game, "perfil exe gpu prioridade tela cheia", () => OpenGamingTab(1)),
        new("Configurações dos jogos", "Jogos e Rede", Glyphs.Settings, "fortnite apex cs2 rocket league preset competitivo ini", () => OpenGamingTab(1)),
        new("Perfil NVIDIA", "Jogos e Rede", Glyphs.Monitor, "nvidia painel de controle baixa latencia vsync shader cache inspector", () => OpenGamingTab(2)),
        new("Windows Defender", "Jogos e Rede", Glyphs.Shield, "antivirus defender exclusao protecao tempo real", () => OpenGamingTab(3)),
        new("Runtimes para jogos", "Jogos e Rede", Glyphs.Package, "visual c++ directx net xna redistribuivel dll", () => OpenGamingTab(1)),
        new("Memória em espera", "Jogos e Rede", Glyphs.Memory, "ram standby islc liberar memoria", () => OpenGamingTab(3)),
        new("Rede", "Jogos e Rede", Glyphs.Network, "internet conexao", () => NavigateTo("network")),
        new("Teste de velocidade", "Rede", Glyphs.Speed, "speedtest ping jitter download upload", () => NavigateTo("network")),
        new("Servidor DNS", "Rede", Glyphs.Globe, "dns cloudflare google quad9", () => NavigateTo("network")),
        new("Configurações", "Sistema", Glyphs.Settings, "tema idioma licenca atualizacao perfil", () => NavigateTo("settings")),
        new("Editor de BIOS", "Sistema", Glyphs.Chip, "bios uefi scewin amisce rebar 4g xmp", () => NavigateTo("bios")),
        new("Patch notes", "Sistema", Glyphs.Document, "novidades versao changelog", () => NavigateTo("patchnotes")),
        new("Sobre", "Sistema", Glyphs.Info, "autor contato discord", () => NavigateTo("about")),
    };

    private void OpenGamingTab(int tab) { _gamingTab = tab; NavigateTo("gaming"); }

    private static string Fold(string text)
    {
        var normalized = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderPalette();
    }

    private void SearchBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        SearchBorder.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        RenderPalette();
    }

    private void SearchBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        SearchBorder.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        // Fecha depois do clique num resultado ser processado
        Dispatcher.BeginInvoke(() => { if (!SearchBox.IsKeyboardFocused) SearchPopup.IsOpen = false; }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { SearchBox.Text = ""; SearchPopup.IsOpen = false; ContentScroll.Focus(); e.Handled = true; }
        else if (e.Key == Key.Down && _paletteResults.Count > 0) { _paletteIndex = (_paletteIndex + 1) % _paletteResults.Count; RenderPalette(keepIndex: true); e.Handled = true; }
        else if (e.Key == Key.Up && _paletteResults.Count > 0) { _paletteIndex = (_paletteIndex - 1 + _paletteResults.Count) % _paletteResults.Count; RenderPalette(keepIndex: true); e.Handled = true; }
        else if (e.Key == Key.Enter && _paletteResults.Count > 0) { RunPaletteEntry(_paletteResults[_paletteIndex]); e.Handled = true; }
    }

    private void RunPaletteEntry(PaletteEntry entry)
    {
        SearchBox.Text = "";
        SearchPopup.IsOpen = false;
        ContentScroll.Focus();
        entry.Run();
    }

    private void RenderPalette(bool keepIndex = false)
    {
        var query = Fold(SearchBox.Text.Trim());
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _paletteResults = PaletteEntries()
            .Where(p => words.All(w => Fold(Translator.Tr(p.Title) + " " + p.Title + " " + p.Keywords + " " + p.Section).Contains(w)))
            .Take(8).ToList();
        if (!keepIndex) _paletteIndex = 0;
        SearchResults.Children.Clear();
        if (_paletteResults.Count == 0)
        {
            var none = new TextBlock { Text = "Nada encontrado.", FontSize = 12.5, Margin = new Thickness(12, 10, 12, 10) };
            none.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            SearchResults.Children.Add(none);
        }
        for (var i = 0; i < _paletteResults.Count; i++)
        {
            var entry = _paletteResults[i];
            var row = new DockPanel();
            var section = new TextBlock { Text = entry.Section, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            section.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            DockPanel.SetDock(section, Dock.Right); row.Children.Add(section);
            var chip = IconChip(entry.Glyph, i == _paletteIndex ? "Accent" : "Info", 30); DockPanel.SetDock(chip, Dock.Left); row.Children.Add(chip);
            var title = new TextBlock { Text = entry.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            row.Children.Add(title);
            var item = new Border { Child = row, Padding = new Thickness(10, 8, 12, 8), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand };
            if (i == _paletteIndex) item.SetResourceReference(Border.BackgroundProperty, "NavSelectedBrush");
            else item.Background = Brushes.Transparent;
            var index = i;
            item.MouseEnter += (_, _) => { if (_paletteIndex != index) { _paletteIndex = index; RenderPalette(keepIndex: true); } };
            item.MouseLeftButtonDown += (_, e) => { e.Handled = true; RunPaletteEntry(entry); };
            SearchResults.Children.Add(item);
        }
        SearchPopup.IsOpen = SearchBox.IsKeyboardFocused;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleTutorialKey(e.Key)) { e.Handled = true; return; }
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
    }
}
