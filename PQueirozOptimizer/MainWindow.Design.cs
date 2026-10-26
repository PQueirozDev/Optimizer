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

/// <summary>Camada visual: cabeçalho de cada página, busca rápida (Ctrl+K), notificações e abas.</summary>
public partial class MainWindow
{
    // ================= Cabeçalho de cada página =================
    // Linha discreta acima do título = categoria da barra lateral (no Início, o nome do computador)
    private static readonly Dictionary<string, (string Eyebrow, string Glyph, string Subtitle)> PageHeaders = new()
    {
        ["dashboard"] = ("Principal", Glyphs.Home, "Visão geral do desempenho e da saúde do seu computador."),
        ["mypc"] = ("Principal", Glyphs.MyPc, "Hardware, sistema e estado do seu computador, lidos agora."),
        ["perflab"] = ("Desempenho", Glyphs.Flask, "FPS, tempo de quadro e sensores reais, com histórico e comparação antes e depois."),
        ["profiles"] = ("Desempenho", Glyphs.Person, "Perfis por jogo ou uso, com ativação automática do Modo Jogo."),
        ["smart"] = ("Otimização", Glyphs.Lightning, "Análise do PC e ajustes recomendados pelo objetivo, com risco e reversão."),
        ["restore"] = ("Ferramentas", Glyphs.Restore2, "Crie e restaure pontos de restauração para proteger o sistema."),
        ["resources"] = ("Ferramentas", Glyphs.Library, "Automação, atalhos, downloads e testes para preparar qualquer PC."),
        ["fixes"] = ("Ferramentas", Glyphs.Repair, "Correções rápidas para problemas comuns do Windows."),
        ["services"] = ("Otimização", Glyphs.Services, "Desligue serviços e tarefas em segundo plano para liberar o sistema."),
        ["apps"] = ("Otimização", Glyphs.Apps, "Ajuste Discord, navegadores e outros aplicativos e remova o que não usa."),
        ["optimization"] = ("Otimização", Glyphs.Sliders, "Ajustes revisados item por item, com backup e reversão."),
        ["startup"] = ("Otimização", Glyphs.Power, "Programas, tarefas e serviços que iniciam com o Windows."),
        ["drivers"] = ("Otimização", Glyphs.Devices, "Drivers oficiais, instalação limpa com DDU e utilitários."),
        ["isos"] = ("Otimização", Glyphs.Disc, "Imagens personalizadas do Windows."),
        ["tools"] = ("Ferramentas", Glyphs.Settings, "Atalhos e utilitários do dia a dia."),
        ["customize"] = ("Otimização", Glyphs.Personalize, "Barra de tarefas, Explorador, área de trabalho e outras preferências do Windows."),
        ["history"] = ("Ferramentas", Glyphs.History, "Tudo o que foi feito e como desfazer."),
        ["diagnostics"] = ("Ferramentas", Glyphs.Diagnostic, "Uma leitura guiada dos principais componentes do Windows."),
        ["gaming"] = ("Desempenho", Glyphs.Game, "Sessão de jogo, perfis, NVIDIA, configurações dos jogos e Defender."),
        ["network"] = ("Desempenho", Glyphs.Network, "Velocidade, DNS, latência e reparos de conexão."),
        ["library"] = ("Desempenho", Glyphs.Library, "Todos os jogos instalados, de qualquer loja, num só lugar."),
        ["overlay"] = ("Desempenho", Glyphs.Monitor, "FPS, tempo de quadro e uso do hardware por cima do jogo."),
        ["stress"] = ("Desempenho", Glyphs.Lightning, "Teste de estabilidade de CPU e memória com conferência de erros."),
        ["health"] = ("Ferramentas", Glyphs.Health, "Discos, bateria e erros de hardware lidos do Windows."),
        ["devices"] = ("Ferramentas", Glyphs.Devices, "Dispositivos fantasmas, drivers com problema e portas COM."),
        ["settings"] = ("Sistema", Glyphs.Settings, "Aparência, licença, idioma, tutoriais e atualizações."),
        ["bios"] = ("Ferramentas", Glyphs.Chip, "Editor de BIOS pelo SCEWIN e ajustes recomendados."),
        ["biosadvisor"] = ("Ferramentas", Glyphs.Diagnostic, "Análise do hardware e recomendações de BIOS, sem alterar nada automaticamente."),
        ["patchnotes"] = ("Sistema", Glyphs.Document, "Novidades de cada versão."),
        ["about"] = ("Sistema", Glyphs.Info, "Sobre o Qrztweaks."),
    };

    private void UpdatePageHeader(string page)
    {
        PageActions.Children.Clear();
        if (!PageHeaders.TryGetValue(page, out var header)) return;
        PageEyebrow.Tag = null;
        // A linha acima do título é a categoria da barra superior em que a página está
        PageEyebrow.Text = CategoryOf(page) is { } category && TopCategories.FirstOrDefault(c => c.Tag == category).Title is { } title ? title : header.Eyebrow;
        PageSubtitle.Text = header.Subtitle;
        // Título e subtítulo entram com um leve deslize (160 ms); sem animação, trocam direto
        if (!AppearanceService.AnimationsEnabled || !IsLoaded) return;
        foreach (var part in new FrameworkElement[] { PageEyebrow, PageTitle, PageSubtitle })
        {
            var slide = new TranslateTransform(0, 4);
            part.RenderTransform = slide;
            part.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(160)));
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    // ================= Notificações =================
    /// <summary>Notificação discreta no canto inferior direito que some sozinha.</summary>
    private void ShowToast(string title, string message, string tone = "Success")
    {
        // Configurações → Notificações: os avisos de sucesso podem ser desligados; erros e alertas aparecem sempre
        if (tone == "Success" && !_configService.Config.SuccessNotifications) return;
        var glyph = tone switch { "Success" => Glyphs.Check, "Danger" => Glyphs.Error, "Warning" => Glyphs.Warning, _ => Glyphs.Info };
        var dock = new DockPanel();
        var chip = IconChip(glyph, tone, 34); chip.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(chip, Dock.Left); dock.Children.Add(chip);
        var close = new Button { Content = GlyphIcon(Glyphs.Cancel, 10, "MutedBrush"), Padding = new Thickness(6), Margin = new Thickness(6, -4, -6, 0), VerticalAlignment = VerticalAlignment.Top };
        close.SetResourceReference(StyleProperty, "GhostButton");
        DockPanel.SetDock(close, Dock.Right); dock.Children.Add(close);
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        var t = new TextBlock { Text = Translator.Tr(title), FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var m = new TextBlock { Text = Translator.Tr(message), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        m.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        text.Children.Add(t); if (!string.IsNullOrWhiteSpace(message)) text.Children.Add(m);
        dock.Children.Add(text);

        var toast = new Border { Child = dock, Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 10, 0, 0), Opacity = 0 };
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
            if (!AppearanceService.AnimationsEnabled) { ToastHost.Children.Remove(toast); return; }
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
            var icon = GlyphIcon(tabs[i].Glyph, 13, i == selected ? "OnPrimaryBrush" : "MutedBrush"); icon.Margin = new Thickness(0, 0, 8, 0);
            content.Children.Add(icon);
            var label = new TextBlock { Text = tabs[i].Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, i == selected ? "OnPrimaryBrush" : "MutedBrush");
            content.Children.Add(label);
            var button = new Button { Content = content, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(0) };
            if (i == selected) button.SetResourceReference(StyleProperty, "PrimaryButton");
            else { button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; }
            button.Click += (_, _) => onSelect(index);
            row.Children.Add(button);
        }
        var bar = new Border { Child = row, Padding = new Thickness(5), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) };
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
        new("Início", "Principal", Glyphs.Home, "dashboard inicio home painel saude monitor visao geral command center", () => NavigateTo("dashboard")),
        new("Meu PC", "Principal", Glyphs.MyPc, "hardware processador placa de video memoria armazenamento placa mae bios sistema especificacoes", () => NavigateTo("mypc")),
        new("Laboratório de desempenho", "Desempenho", Glyphs.Flask, "fps frametime tempo de quadro benchmark presentmon teste comparar 1% low performance lab", () => NavigateTo("perflab")),
        new("Perfis", "Desempenho", Glyphs.Person, "perfil valorant cs2 fortnite streaming importar exportar automatico", () => NavigateTo("profiles")),
        new("Otimização inteligente", "Otimização", Glyphs.Lightning, "inteligente smart optimize recomendacoes objetivo analise perfil gaming notebook programacao", () => NavigateTo("smart")),
        new("Laboratório de otimização", "Desempenho", Glyphs.Diagnostic, "experimento medir ajuste antes depois optimization lab", () => { _perfTab = "lab"; NavigateTo("perflab"); }),
        new("Modo compacto", "Desempenho", Glyphs.Pin, "hud segundo monitor janela pequena compacto", OpenCompactMonitor),
        new("Otimizações", "Otimização", Glyphs.Sliders, "ajustes perfil", () => NavigateTo("optimization")),
        new("Versão Padrão", "Otimização", Glyphs.Speed, "otimizar padrao basico", () => _ = PrepareOperationAsync("padrao")),
        new("Versão Avançada (jogos)", "Otimização", Glyphs.Game, "gamer latencia fps avancada", () => _ = PrepareOperationAsync("gamer")),
        new("Remoção de aplicativos e privacidade", "Otimização", Glyphs.Shield, "debloat remover apps aplicativos telemetria privacidade bloat", () => _ = PrepareOperationAsync("debloat")),
        new("Limpeza rápida", "Otimização", Glyphs.Broom, "limpar temporarios cache lixo", () => _ = PrepareOperationAsync("quickclean")),
        new("Diagnóstico do PC", "Ferramentas", Glyphs.Diagnostic, "analisar analise saude dpc temperatura", () => _ = PrepareOperationAsync("analisar")),
        new("Inicialização", "Otimização", Glyphs.Power, "startup autoruns programas servicos tarefas", () => NavigateTo("startup")),
        new("Drivers", "Otimização", Glyphs.Devices, "driver nvidia amd intel chipset", () => NavigateTo("drivers")),
        new("Instalação limpa de driver (DDU)", "Drivers", Glyphs.Refresh, "ddu display driver uninstaller limpa reinstalar", () => NavigateTo("drivers")),
        new("Utilitários", "Ferramentas", Glyphs.Settings, "ferramentas utilitarios atalhos all black barra", () => NavigateTo("tools")),
        new("Personalizar Windows", "Otimização", Glyphs.Personalize, "barra de tarefas esquerda relogio segundos extensoes ocultos menu contexto classico num lock area de trabalho icones mouse aceleracao teclas aderencia finalizar tarefa", () => NavigateTo("customize")),
        new("Histórico", "Ferramentas", Glyphs.History, "historico atividade reverter desfazer backup log", () => NavigateTo("history")),
        new("Modo Jogo", "Desempenho", Glyphs.Game, "sessao game mode fechar programas pausar servicos ascension", () => OpenGamingTab(0)),
        new("Plano de energia Qrz", "Jogos e Rede", Glyphs.Battery, "energia power plan qrz latencia", () => OpenGamingTab(3)),
        new("Perfis de jogos", "Jogos e Rede", Glyphs.Game, "perfil exe gpu prioridade tela cheia", () => OpenGamingTab(1)),
        new("Configurações dos jogos", "Jogos e Rede", Glyphs.Settings, "fortnite apex cs2 rocket league preset competitivo ini", () => OpenGamingTab(1)),
        new("Perfil NVIDIA", "Jogos e Rede", Glyphs.Monitor, "nvidia painel de controle baixa latencia vsync shader cache inspector", () => OpenGamingTab(2)),
        new("Windows Defender", "Jogos e Rede", Glyphs.Shield, "antivirus defender exclusao protecao tempo real", () => OpenGamingTab(3)),
        new("Biblioteca", "Jogos", Glyphs.Library, "jogos instalados steam epic gog ubisoft jogar launcher library", () => NavigateTo("library")),
        new("Overlay", "Desempenho", Glyphs.Monitor, "fps contador overlay na tela tempo de quadro 1% low hud jogo", () => NavigateTo("overlay")),
        new("Stress test", "Desempenho", Glyphs.Fire, "estresse estabilidade cpu memoria ram overclock undervolt xmp expo whea teste", () => NavigateTo("stress")),
        new("Saúde do hardware", "Ferramentas", Glyphs.Health, "smart disco ssd hd desgaste temperatura bateria whea erros relatorio hardware health", () => NavigateTo("health")),
        new("Relatório do PC", "Ferramentas", Glyphs.Document, "relatorio exportar html suporte hardware report", () => _ = ExportReportAsync()),
        new("Dispositivos", "Ferramentas", Glyphs.Devices, "dispositivos fantasmas usb remover porta com serial arduino drivers com problema devices", () => NavigateTo("devices")),
        new("Runtimes para jogos", "Jogos e Rede", Glyphs.Package, "visual c++ directx net xna redistribuivel dll", () => OpenGamingTab(1)),
        new("Memória em espera", "Jogos e Rede", Glyphs.Memory, "ram standby islc liberar memoria", () => OpenGamingTab(3)),
        new("Rede", "Desempenho", Glyphs.Network, "internet conexao", () => NavigateTo("network")),
        new("Teste de velocidade", "Rede", Glyphs.Speed, "speedtest ping jitter download upload", () => NavigateTo("network")),
        new("Servidor DNS", "Rede", Glyphs.Globe, "dns cloudflare google quad9", () => NavigateTo("network")),
        new("Configurações", "Sistema", Glyphs.Settings, "tema idioma licenca atualizacao perfil aparencia", () => NavigateTo("settings")),
        new("Licença", "Sistema", Glyphs.Key, "licenca chave plano ativar renovar upgrade", () => NavigateTo("admin")),
        new("Atualizações", "Sistema", Glyphs.CloudDownload, "atualizacao atualizar versao nova update", () => NavigateTo("updates")),
        new("Editor de BIOS", "Ferramentas", Glyphs.Chip, "bios uefi scewin amisce rebar 4g xmp", () => NavigateTo("bios")),
        new("Assistente de BIOS", "Ferramentas", Glyphs.Diagnostic, "bios advisor assistente recomendacoes xmp turbo speed shift pl1 pl2 pbo resizable bar pontuacao benchmark fps", () => NavigateTo("biosadvisor")),
        new("Novidades", "Sistema", Glyphs.Document, "novidades versao changelog patch notes", () => NavigateTo("patchnotes")),
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
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control && _paletteResults.Count > 0) { ToggleFavorite(_paletteResults[_paletteIndex]); e.Handled = true; }
    }

    private void ToggleFavorite(PaletteEntry entry)
    {
        try
        {
            if (!_configService.ToggleFavorite(entry.Title))
                ShowToast("Favoritos", string.Format(Translator.Tr("Até {0} favoritos. Tire um para adicionar outro."), ConfigService.MaxFavorites), "Warning");
        }
        catch (System.IO.IOException ex) { ShowToast("Favoritos", ex.Message, "Warning"); }
        RenderPalette(keepIndex: true);
        if (_currentPage == "dashboard") _ = ShowDashboardAsync();
    }

    /// <summary>Faixa do Início com as funções favoritas (some quando não há nenhuma).</summary>
    private UIElement? FavoritesRow()
    {
        var entries = _configService.Config.Favorites.Select(t => PaletteEntries().FirstOrDefault(p => p.Title == t)).OfType<PaletteEntry>().ToList();
        if (entries.Count == 0) return null;
        var body = new StackPanel();
        body.Children.Add(HomeBandTitle("Favoritos", false));
        var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var entry in entries)
        {
            var button = IconButton(entry.Glyph, entry.Title);
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Click += (_, _) => entry.Run();
            wrap.Children.Add(button);
        }
        body.Children.Add(wrap);
        var hint = Label("Marque mais com a estrela na busca (Ctrl+K).", 11.5, true);
        hint.Margin = new Thickness(0, 2, 0, 0);
        body.Children.Add(hint);
        return HomeCard(body);
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
        // Favoritos primeiro (com a busca vazia, a lista abre com eles)
        _paletteResults = PaletteEntries()
            .Where(p => words.All(w => Fold(Translator.Tr(p.Title) + " " + p.Title + " " + p.Keywords + " " + p.Section).Contains(w)))
            .OrderByDescending(p => _configService.IsFavorite(p.Title))
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
            var favorite = _configService.IsFavorite(entry.Title);
            var star = new TextBlock { Text = favorite ? "\u2605" : "\u2606", FontSize = 15, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                ToolTip = Translator.Tr(favorite ? "Tirar dos favoritos (Ctrl+D)" : "Adicionar aos favoritos (Ctrl+D)") };
            star.SetResourceReference(TextBlock.ForegroundProperty, favorite ? "WarningBrush" : "MutedBrush");
            star.MouseLeftButtonDown += (_, e) => { e.Handled = true; ToggleFavorite(entry); };
            DockPanel.SetDock(star, Dock.Right); row.Children.Add(star);
            var chip = IconChip(entry.Glyph, i == _paletteIndex ? "Accent" : "Info", 30); DockPanel.SetDock(chip, Dock.Left); row.Children.Add(chip);
            var title = new TextBlock { Text = entry.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
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
