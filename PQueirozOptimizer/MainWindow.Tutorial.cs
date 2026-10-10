using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Tutorial guiado: na primeira abertura um tour pela interface e, na primeira visita de algumas páginas, um
/// tutorial curto delas. A janela escurece e só o elemento explicado fica iluminado, com um balão ao lado.
/// </summary>
public partial class MainWindow
{
    private sealed record TutorialStep(Func<FrameworkElement?> Target, string Title, string Description);

    private List<TutorialStep>? _tutorialSteps;
    private string? _tutorialId;
    private int _tutorialIndex;
    private readonly Dictionary<string, FrameworkElement> _tutorialMarks = new();

    /// <summary>Marca um elemento da página para o tutorial dela encontrar (chamado ao montar a página).</summary>
    private T Mark<T>(T element, string key) where T : FrameworkElement { _tutorialMarks[key] = element; return element; }

    private FrameworkElement? Marked(string key) => _tutorialMarks.TryGetValue(key, out var e) && e.IsVisible ? e : null;

    private bool TutorialDone(string id)
    {
        try { return _configService.IsTutorialCompleted(id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    private void SaveTutorial(string id, bool done)
    {
        try { _configService.SetTutorialCompleted(id, done); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Write("WARN", "Preferências não foram salvas: " + ex.Message); }
    }

    /// <summary>Tour da primeira abertura, pelos principais pontos da interface.</summary>
    private List<TutorialStep> WelcomeTour() => new()
    {
        new(() => null, "Bem-vindo ao Qrztweaks!", "Em menos de um minuto você conhece o essencial. Use Próximo para avançar ou Pular para começar a usar agora."),
        new(() => NavDashboard, "Command Center", "A saúde do PC, o monitor ao vivo, os alertas e as ações rápidas ficam aqui."),
        new(() => NavSmart, "Smart Optimize", "Escolha o objetivo do PC: o app lê o estado de cada ajuste e recomenda só o que se aplica, com risco e reversão."),
        new(() => NavResources, "Recursos — comece por aqui", "Verificação de arquivos corrompidos do Windows, instalação de runtimes para jogos e reinstalação limpa do driver de vídeo."),
        new(() => NavFixes, "Correções", "Soluções rápidas para problemas comuns: Windows Update travado, Loja, áudio, pesquisa, ícones e mais."),
        new(() => NavRestore, "Pontos de restauração", "Crie um ponto de restauração antes de grandes mudanças e volte a ele quando quiser."),
        new(() => NavOpt, "Otimizações", "Os ajustes de desempenho, privacidade e manutenção. Você revisa cada item antes de aplicar, e o que tem backup pode ser revertido."),
        new(() => NavServices, "Serviços", "Desligue grupos de serviços em segundo plano — Windows Update, telemetria, Bluetooth, Xbox — e ligue de volta quando quiser."),
        new(() => NavApps, "Apps", "Ajuste Discord, navegadores e Spotify para não disputarem a placa de vídeo com o jogo, e desinstale o que não usa."),
        new(() => NavGaming, "Modo Jogo", "Sessão de jogo temporária, perfis por jogo, presets dos jogos, perfil NVIDIA, plano de energia Qrz e Defender."),
        new(() => NavBios, "BIOS", "Ajustes de BIOS por grupos (memória, Resizable BAR, energia) aplicados pelo Windows, com cópia original para voltar."),
        new(() => SearchHost, "Busca rápida", "Pressione Ctrl+K e digite o que procura para abrir qualquer página ou recurso."),
        new(() => HudPanel, "Monitor ao vivo", "CPU, placa de vídeo, memória e ping em tempo real, em qualquer página."),
        new(() => NavHistory, "Atividade e reversão", "Tudo o que foi feito fica registrado aqui, com a opção de desfazer."),
        new(() => null, "Tudo pronto!", "Você pode rever os tutoriais quando quiser em Configurações → Tutoriais. Bom jogo!"),
    };

    /// <summary>Tutoriais das páginas: aparecem na primeira visita, depois que a página foi montada.</summary>
    private List<TutorialStep>? PageTutorial(string page) => page switch
    {
        "resources" => new()
        {
            new(() => Marked("resources.check"), "Verificação do sistema", "Comece aqui! A verificação de corrupção encontra e repara arquivos danificados do Windows (ChkDsk, SFC e DISM)."),
            new(() => Marked("resources.runtimes"), "Instalar runtimes", "Depois, instale o DirectX, o Visual C++ e o .NET que os jogos exigem."),
            new(() => Marked("resources.driver"), "Atualizar o driver de vídeo", "Por fim, reinstale o driver de vídeo do zero com o DDU."),
        },
        "gaming" => new()
        {
            new(() => Marked("gaming.tabs"), "Abas do Modo Jogo", "Modo Jogo para a sessão temporária, Jogos para presets e perfis, NVIDIA para o driver e Sistema para energia e Defender."),
            new(() => Marked("gaming.start"), "Ativar o Modo Jogo", "Escolha o que fechar e pausar e clique aqui antes de jogar. Desative quando terminar: tudo volta como estava."),
        },
        "network" => new()
        {
            new(() => Marked("network.speed"), "Teste de velocidade", "Mede download, upload, ping, jitter e perda de pacotes. Rode antes e depois dos ajustes para comparar."),
            new(() => Marked("network.dns"), "DNS", "Meça e troque o servidor DNS por um mais rápido em um clique."),
            new(() => Marked("network.repair"), "Reparos", "Se a internet der problema, limpe o DNS, sincronize o relógio ou redefina a rede."),
        },
        "services" => new()
        {
            new(() => Marked("services.list"), "Grupos de serviços", "Cada grupo desliga um conjunto de serviços. O estado original é guardado: desmarque para voltar ao que era."),
        },
        "bios" => new()
        {
            new(() => Marked("bios.groups"), "Grupos de BIOS", "Escolha quais grupos o app vai modificar. Cada grupo mostra o valor atual e o novo antes de gravar."),
        },
        _ => null,
    };

    /// <summary>Abre o tutorial da página na primeira visita (o tour inicial tem prioridade).</summary>
    private void MaybeShowPageTutorial(string page)
    {
        if (!IsVisible || _tutorialSteps != null || !TutorialDone("inicio")) return;
        var id = "pagina-" + page;
        if (TutorialDone(id) || PageTutorial(page) is not { } steps) return;
        // Espera a página terminar de entrar (cascata) para os elementos estarem no lugar
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_currentPage == page && _tutorialSteps == null) StartTutorial(id, steps.Where(s => s.Target() != null).ToList());
        };
        timer.Start();
    }

    private void MaybeShowWelcomeTour()
    {
        if (TutorialDone("inicio")) return;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) }; // depois da abertura
        timer.Tick += (_, _) => { timer.Stop(); StartTutorial("inicio", WelcomeTour()); };
        timer.Start();
    }

    private void StartTutorial(string id, List<TutorialStep> steps)
    {
        if (steps.Count == 0 || !IsVisible) return;
        _tutorialId = id; _tutorialSteps = steps; _tutorialIndex = 0;
        TutorialLayer.Visibility = Visibility.Visible;
        TutorialLayer.Focusable = true;
        TutorialLayer.Focus();
        ShowTutorialStep(animate: true);
    }

    private void EndTutorial(bool completed)
    {
        if (_tutorialId is { } id) SaveTutorial(id, true);
        var wasWelcome = _tutorialId == "inicio";
        _tutorialSteps = null; _tutorialId = null;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => { TutorialLayer.Visibility = Visibility.Collapsed; TutorialLayer.Children.Clear(); TutorialLayer.BeginAnimation(OpacityProperty, null); };
        TutorialLayer.BeginAnimation(OpacityProperty, fade);
        if (wasWelcome && completed) ShowToast("Tutorial concluído", "Reveja quando quiser em Configurações → Tutoriais.", "Success");
    }

    private void ShowTutorialStep(bool animate = false)
    {
        if (_tutorialSteps is null) return;
        var step = _tutorialSteps[_tutorialIndex];
        var target = step.Target();
        // Garante que o alvo esteja visível na barra lateral ou no conteúdo antes de medir
        target?.BringIntoView();
        TutorialLayer.UpdateLayout();
        TutorialLayer.Children.Clear();

        var w = TutorialLayer.ActualWidth; var h = TutorialLayer.ActualHeight;
        Rect? hole = null;
        // O alvo pode ter saído da tela (página trocada ou redesenhada): sem ele, o passo aparece sem destaque
        if (target != null && target.IsVisible && target.ActualWidth > 0 && target.IsDescendantOf(RootGrid))
        {
            // A camada do tutorial fica por cima da página (irmã, não ancestral do alvo)
            var origin = target.TransformToVisual(TutorialLayer).Transform(new Point(0, 0));
            hole = new Rect(origin.X - 6, origin.Y - 6, target.ActualWidth + 12, target.ActualHeight + 12);
        }

        // Máscara escura com um buraco arredondado no alvo (regra par-ímpar)
        var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
        mask.Children.Add(new RectangleGeometry(new Rect(0, 0, w, h)));
        if (hole is { } r) mask.Children.Add(new RectangleGeometry(r, 12, 12));
        var shade = new System.Windows.Shapes.Path { Data = mask, Fill = new SolidColorBrush(Color.FromArgb(0xC8, 0x02, 0x03, 0x08)) };
        shade.MouseLeftButtonDown += (_, e) => e.Handled = true; // o resto da janela fica bloqueado
        TutorialLayer.Children.Add(shade);

        if (hole is { } ring)
        {
            var glow = new Border { Width = ring.Width, Height = ring.Height, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(ring.X, ring.Y, 0, 0), IsHitTestVisible = false };
            glow.SetResourceReference(Border.BorderBrushProperty, "AccentGradientBrush");
            glow.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.9, Color = Color.FromRgb(0x8B, 0x5C, 0xF6) };
            TutorialLayer.Children.Add(glow);
            // Pulsa suavemente para chamar a atenção
            glow.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }

        var card = TutorialCard(step);
        card.Measure(new Size(360, double.PositiveInfinity));
        var size = card.DesiredSize;
        double x, y;
        if (hole is { } t)
        {
            // À direita do alvo; se não couber, embaixo; senão em cima
            if (t.Right + 16 + size.Width < w) { x = t.Right + 16; y = Math.Clamp(t.Y + t.Height / 2 - size.Height / 2, 12, h - size.Height - 12); }
            else if (t.Bottom + 16 + size.Height < h) { x = Math.Clamp(t.X, 12, w - size.Width - 12); y = t.Bottom + 16; }
            else { x = Math.Clamp(t.X, 12, w - size.Width - 12); y = Math.Max(12, t.Y - size.Height - 16); }
        }
        else { x = (w - size.Width) / 2; y = (h - size.Height) / 2; }
        card.HorizontalAlignment = HorizontalAlignment.Left; card.VerticalAlignment = VerticalAlignment.Top;
        card.Margin = new Thickness(x, y, 0, 0);
        TutorialLayer.Children.Add(card);

        var slide = new TranslateTransform(0, 8);
        card.RenderTransform = slide;
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (animate) TutorialLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    private Border TutorialCard(TutorialStep step)
    {
        var total = _tutorialSteps!.Count;
        var panel = new StackPanel { Width = 340 };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var count = new TextBlock { Text = $"{_tutorialIndex + 1} / {total}", FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Tag = Translator.SystemDataTag };
        count.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        DockPanel.SetDock(count, Dock.Right); top.Children.Add(count);
        var chip = IconChip(Glyphs.Lightning, "Accent", 30); chip.HorizontalAlignment = HorizontalAlignment.Left; top.Children.Add(chip);
        panel.Children.Add(top);
        var title = new TextBlock { Text = Translator.Tr(step.Title), FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        panel.Children.Add(title);
        var text = new TextBlock { Text = Translator.Tr(step.Description), FontSize = 12.5, Margin = new Thickness(0, 6, 0, 14), TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(text);

        // Pontinhos de progresso
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        for (var i = 0; i < total; i++)
        {
            var dot = new Border { Width = i == _tutorialIndex ? 18 : 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 4, 0) };
            dot.SetResourceReference(Border.BackgroundProperty, i == _tutorialIndex ? "AccentGradientBrush" : "BorderBrush");
            dots.Children.Add(dot);
        }
        panel.Children.Add(dots);

        var buttons = new DockPanel();
        var last = _tutorialIndex == total - 1;
        var next = IconButton(last ? Glyphs.Check : Glyphs.ChevronRight, last ? "Concluir" : "Próximo", primary: true);
        next.Margin = new Thickness(0);
        next.Click += (_, _) => NextTutorialStep();
        DockPanel.SetDock(next, Dock.Right); buttons.Children.Add(next);
        if (_tutorialIndex > 0)
        {
            var back = new Button { Content = Translator.Tr("Voltar"), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
            back.SetResourceReference(StyleProperty, "GhostButton");
            back.Click += (_, _) => { _tutorialIndex--; ShowTutorialStep(); };
            DockPanel.SetDock(back, Dock.Right); buttons.Children.Add(back);
        }
        if (!last)
        {
            var skip = new Button { Content = Translator.Tr("Pular tutorial"), Padding = new Thickness(0, 7, 12, 7), Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
            skip.SetResourceReference(StyleProperty, "GhostButton");
            skip.Click += (_, _) => EndTutorial(completed: false);
            buttons.Children.Add(skip);
        }
        panel.Children.Add(buttons);

        var card = new Border { Child = panel, Padding = new Thickness(20, 18, 20, 18), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardHoverBorderBrush");
        card.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.55, Color = Colors.Black };
        return card;
    }

    private void NextTutorialStep()
    {
        if (_tutorialSteps is null) return;
        if (_tutorialIndex >= _tutorialSteps.Count - 1) { EndTutorial(completed: true); return; }
        _tutorialIndex++;
        ShowTutorialStep();
    }

    /// <summary>Teclado durante o tutorial: Enter/→ avança, ← volta, Esc pula.</summary>
    private bool HandleTutorialKey(Key key)
    {
        if (_tutorialSteps is null) return false;
        switch (key)
        {
            case Key.Enter: case Key.Right: NextTutorialStep(); return true;
            case Key.Left when _tutorialIndex > 0: _tutorialIndex--; ShowTutorialStep(); return true;
            case Key.Escape: EndTutorial(completed: false); return true;
            default: return true; // bloqueia atalhos da janela enquanto o tutorial está aberto
        }
    }

    /// <summary>Configurações → Tutoriais: mostra o tour de novo e libera os tutoriais das páginas.</summary>
    private void ReplayTutorials()
    {
        try { _configService.ResetTutorials(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Write("WARN", ex.Message); }
        NavigateTo("dashboard");
        StartTutorial("inicio", WelcomeTour());
    }
}
