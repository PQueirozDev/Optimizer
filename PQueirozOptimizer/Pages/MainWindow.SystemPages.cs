using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Páginas Pontos de restauração, Recursos, Correções, Serviços e Apps.</summary>
public partial class MainWindow
{
    private int _resourcesTab;
    private int _appsTab;
    private bool _appsStore;
    private string _appsSearch = "";

    // ================= Pontos de restauração =================
    private void ShowRestorePoints()
    {
        PageTitle.Text = "Pontos de restauração";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();

        // Criar
        var create = new StackPanel();
        create.Children.Add(SectionHeader("Criar ponto de restauração", "Um retrato das configurações do Windows para voltar caso algo dê errado."));
        var name = LabeledBox("Nome", "Ex.: Antes de otimizar");
        var description = LabeledBox("Descrição (opcional)", "O que você vai mudar");
        var tags = LabeledBox("Etiquetas (separadas por vírgula)", "Ex.: drivers, jogos");
        var fields = Responsive(new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -12, 0) }, 260, 3);
        fields.Children.Add(name.Host); fields.Children.Add(description.Host); fields.Children.Add(tags.Host);
        create.Children.Add(fields);
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var colorLabel = Label("Cor:", 12, true); colorLabel.Margin = new Thickness(0, 0, 10, 0); colorLabel.VerticalAlignment = VerticalAlignment.Center;
        colorRow.Children.Add(colorLabel);
        var color = "Accent";
        var swatches = new List<Border>();
        foreach (var tone in new[] { "Accent", "Info", "Success", "Warning", "Danger" })
        {
            var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(2), Cursor = System.Windows.Input.Cursors.Hand, Tag = tone, ToolTip = tone };
            swatch.SetResourceReference(Border.BackgroundProperty, tone + "Brush");
            swatch.BorderBrush = System.Windows.Media.Brushes.Transparent;
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                color = tone;
                foreach (var s in swatches) s.BorderBrush = s.Tag as string == tone ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
            };
            swatches.Add(swatch); colorRow.Children.Add(swatch);
        }
        swatches[0].BorderBrush = System.Windows.Media.Brushes.White;
        var createBtn = IconButton(Glyphs.Add, "Criar ponto", primary: true);
        createBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Box.Text)) { ShowToast("Ponto de restauração", "Dê um nome antes de criar.", "Warning"); return; }
            var tagList = tags.Box.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (await ExecuteTrackedAsync("Criando ponto de restauração", async _ => await new RestorePointService(_log).CreateAsync(name.Box.Text, description.Box.Text, tagList, color)) && _currentPage == "restore")
                ShowRestorePoints();
        };
        create.Children.Add(ActionBar(colorRow, createBtn));

        // Por que criar
        var learn = new StackPanel();
        learn.Children.Add(SectionHeader("Por que criar um ponto de restauração?"));
        foreach (var benefit in new[]
        {
            "Desfazer qualquer mudança no sistema, inclusive as feitas pelo app, a qualquer momento.",
            "Proteger as configurações caso a energia caia no meio de uma alteração.",
            "Testar drivers e ajustes novos sabendo que dá para voltar ao estado anterior.",
        })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(GlyphIcon(Glyphs.Check, 13, "SuccessBrush"));
            var t = Label(benefit, 12.5); t.Margin = new Thickness(10, 0, 0, 0); t.MaxWidth = 380;
            row.Children.Add(t);
            learn.Children.Add(row);
        }
        root.Children.Add(TwoColumns(Surface(create), Surface(learn)));

        // Catálogo
        var catalog = new StackPanel();
        catalog.Children.Add(SectionHeader("Catálogo", "Pontos de restauração existentes neste PC, do mais recente ao mais antigo."));
        var list = new StackPanel();
        list.Children.Add(Label("Lendo os pontos de restauração...", 12.5, true));
        catalog.Children.Add(list);
        root.Children.Add(Surface(catalog));
        _ = FillRestoreCatalogAsync(list);

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private (Grid Host, TextBox Box) LabeledBox(string label, string placeholder)
    {
        var host = new Grid { Margin = new Thickness(0, 0, 12, 10) };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = Label(label, 11.5, true); l.Margin = new Thickness(0, 0, 0, 5);
        host.Children.Add(l);
        var box = new TextBox { Height = 38 };
        var hint = new TextBlock { Text = placeholder, IsHitTestVisible = false, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var field = new Grid(); field.Children.Add(box); field.Children.Add(hint);
        Grid.SetRow(field, 1); host.Children.Add(field);
        return (host, box);
    }

    private async Task FillRestoreCatalogAsync(StackPanel list)
    {
        IReadOnlyList<RestorePoint> points;
        try { points = await RestorePointService.ListAsync(); }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        {
            list.Children.Clear();
            list.Children.Add(Label("Não foi possível ler os pontos de restauração: " + ex.Message, 12.5, true));
            return;
        }
        list.Children.Clear();
        if (points.Count == 0) { list.Children.Add(EmptyState(Glyphs.Restore2, "Nenhum ponto de restauração ainda", "Crie o primeiro acima. A proteção do sistema é ativada automaticamente no disco do Windows.")); return; }
        foreach (var p in points)
        {
            var row = new DockPanel();
            var restore = IconButton(Glyphs.Undo, "Restaurar");
            restore.Click += async (_, _) =>
            {
                if (Msg($"Restaurar o sistema para \"{p.Description}\"?\n\nO PC vai reiniciar agora para concluir. Salve seus trabalhos antes.", "Restaurar sistema", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                await ExecuteTrackedAsync("Restaurando o sistema", async _ => await new RestorePointService(_log).RestoreAsync(p.Sequence));
            };
            DockPanel.SetDock(restore, Dock.Right); row.Children.Add(restore);
            var bar = new Border { Width = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 14, 0) };
            bar.SetResourceReference(Border.BackgroundProperty, p.Color + "Brush");
            DockPanel.SetDock(bar, Dock.Left); row.Children.Add(bar);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = Label(p.Description, 13); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0); title.Tag = Translator.SystemDataTag;
            text.Children.Add(title);
            var meta = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var when = Label($"{p.CreatedAt:dd/MM/yyyy HH:mm} · nº {p.Sequence}", 11.5, true); when.Margin = new Thickness(0, 0, 10, 0); when.Tag = Translator.SystemDataTag;
            meta.Children.Add(when);
            foreach (var tag in p.Tags) { var pill = Pill(tag, p.Color); pill.Margin = new Thickness(0, 0, 6, 0); ((TextBlock)pill.Child).Tag = Translator.SystemDataTag; meta.Children.Add(pill); }
            text.Children.Add(meta);
            row.Children.Add(text);
            list.Children.Add(ListRow(row));
        }
    }

    private Border ListRow(UIElement content)
    {
        var border = new Border { Child = content, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return border;
    }

    /// <summary>Estado vazio padrão: ícone, título e explicação centralizados.</summary>
    private StackPanel EmptyState(string glyph, string title, string detail)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 16) };
        var chip = IconChip(glyph, "Accent", 48); chip.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(chip);
        var t = Label(title, 14); t.FontWeight = FontWeights.SemiBold; t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 0, 0, 4);
        panel.Children.Add(t);
        var d = Label(detail, 12, true); d.HorizontalAlignment = HorizontalAlignment.Center; d.TextAlignment = TextAlignment.Center; d.MaxWidth = 460;
        panel.Children.Add(d);
        return panel;
    }

    // ================= Recursos =================
    private void ShowResources()
    {
        PageTitle.Text = "Recursos";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        root.Children.Add(Tabs(new[] { (Glyphs.Rocket2, "Automação"), (Glyphs.Lightning, "Atalhos"), (Glyphs.Download, "Downloads"), (Glyphs.Speed, "Benchmark") }, _resourcesTab, tab =>
        {
            _resourcesTab = tab; ShowResources(); AnimatePageIn();
        }));
        switch (_resourcesTab)
        {
            case 1: root.Children.Add(ShortcutsCard()); break;
            case 2: root.Children.Add(LinksCard("Downloads recomendados", "Ferramentas gratuitas e confiáveis para drivers, monitoramento e manutenção. Todos os links levam ao site oficial.", Downloads)); break;
            case 3: root.Children.Add(BenchmarkCard()); break;
            default: root.Children.Add(AutomationCards()); break;
        }
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private UIElement AutomationCards()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Manutenção sem esforço", "Siga a ordem: verifique o sistema, instale os runtimes e reinstale o driver de vídeo."));
        var grid = Responsive(new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -14, 0) }, 260, 3);

        Border Automation(string key, string glyph, string tone, string step, string title, string subtitle, string description, string[] features, string action, Action run)
        {
            var body = new DockPanel();
            var button = IconButton(Glyphs.Play, action, primary: true);
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(0, 16, 0, 0);
            button.Click += (_, _) => run();
            DockPanel.SetDock(button, Dock.Bottom); body.Children.Add(button);
            var stack = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var pill = Pill(step, tone); DockPanel.SetDock(pill, Dock.Right); pill.VerticalAlignment = VerticalAlignment.Top; head.Children.Add(pill);
            head.Children.Add(IconChip(glyph, tone, 44));
            stack.Children.Add(head);
            var t = Label(title, 15); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 0, 0, 2); stack.Children.Add(t);
            var s = Label(subtitle, 11.5, true); s.Margin = new Thickness(0, 0, 0, 10); stack.Children.Add(s);
            var d = Label(description, 12, true); d.Margin = new Thickness(0, 0, 0, 10); stack.Children.Add(d);
            foreach (var f in features)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
                row.Children.Add(GlyphIcon(Glyphs.Check, 11, tone + "Brush"));
                var ft = Label(f, 11.5); ft.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(ft);
                stack.Children.Add(row);
            }
            body.Children.Add(stack);
            var card = Surface(body); card.Margin = new Thickness(0, 0, 14, 14);
            return Mark(card, key);
        }

        grid.Children.Add(Automation("resources.check", Glyphs.Shield, "Info", "Passo 1", "Verificação de corrupção", "Verifique e repare os arquivos do Windows",
            "Roda ChkDsk, SFC e DISM para encontrar e reparar arquivos corrompidos do Windows, e uma verificação final do SFC.",
            new[] { "Verificação do disco (ChkDsk)", "Arquivos do sistema (SFC)", "Imagem do Windows (DISM)" }, "Iniciar verificação", () =>
            {
                if (Msg("A verificação completa pode levar 30 minutos ou mais. Você pode continuar usando o PC, mas evite jogos pesados enquanto ela roda. Iniciar?", "Verificação de corrupção", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    _ = RunLiveAsync("corrupcao");
            }));
        grid.Children.Add(Automation("resources.runtimes", Glyphs.Package, "Accent", "Passo 2", "Runtimes e DirectX", "Tudo o que jogos e apps precisam",
            "Baixa e instala o Visual C++, o DirectX e o .NET Desktop Runtime pelo winget. Corrige erros de DLL faltando.",
            new[] { "Visual C++ 2010–2022", "DirectX (runtime)", ".NET Desktop Runtime" }, "Instalar runtimes", async () =>
            {
                if (!GamingService.IsWingetAvailable()) { Msg("O winget (Instalador de Aplicativos) não foi encontrado. Instale o \"Instalador de Aplicativos\" pela Microsoft Store e tente de novo.", "Runtimes", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                var ids = GamingService.Redistributables.Where(r => r.WingetId != "Microsoft.XNARedist").Select(r => r.WingetId).ToList();
                var progress = new Progress<string>(line => { OperationStatus.Text = line; _log.Write(line.StartsWith("[ERRO]") ? "ERROR" : "INFO", line); });
                await ExecuteTrackedAsync("Instalando runtimes", async token => await Gaming.InstallRedistributablesAsync(ids, progress, token));
            }));
        grid.Children.Add(Automation("resources.driver", Glyphs.Monitor, "Success", "Passo 3", "Reinstalação limpa do driver", "Driver de vídeo do zero, com DDU",
            "Remove o driver de vídeo atual com o DDU, reinicia e abre o instalador do driver novo assim que você entrar no Windows.",
            new[] { "Remoção completa com DDU", "Ponto de restauração antes", "Instalador abre sozinho" }, "Abrir instalação limpa", () => NavigateTo("drivers")));
        panel.Children.Add(grid);

        var note = Label("Depois de reinstalar o driver, os jogos recompilam os shaders: as primeiras partidas podem travar um pouco até o cache ser refeito.", 12, true);
        note.Margin = new Thickness(4, 0, 0, 0);
        panel.Children.Add(note);
        return panel;
    }

    private UIElement ShortcutsCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Essenciais para preparar qualquer PC", "Atalhos de um clique para as configurações e ferramentas do Windows que mais importam para desempenho."));
        var items = new (string Glyph, string Title, string Detail, Action Run)[]
        {
            (Glyphs.Restore2, "Criar ponto de restauração", "Antes de qualquer mudança grande", () => NavigateTo("restore")),
            (Glyphs.Broom, "Limpeza rápida", "Arquivos temporários e caches", () => _ = PrepareOperationAsync("quickclean")),
            (Glyphs.Game, "Modo de Jogo do Windows", "Configurações → Jogos", () => Launch("ms-settings:gaming-gamemode")),
            (Glyphs.Monitor, "Gráficos (GPU por app)", "Configurações → Vídeo → Gráficos", () => Launch("ms-settings:display-advancedgraphics")),
            (Glyphs.Monitor, "Taxa de atualização do monitor", "Configurações → Vídeo avançado", () => Launch("ms-settings:display-advanced")),
            (Glyphs.Battery, "Opções de energia", "Planos de energia do Windows", () => Launch("powercfg.cpl")),
            (Glyphs.Speed, "Opções de desempenho", "Efeitos visuais e memória virtual", () => Launch("SystemPropertiesPerformance.exe")),
            (Glyphs.Drive, "Otimizar unidades", "TRIM e desfragmentação", () => Launch("dfrgui.exe")),
            (Glyphs.Broom, "Limpeza de disco", "Ferramenta do Windows", () => Launch("cleanmgr.exe")),
            (Glyphs.Refresh, "Windows Update", "Verificar atualizações", () => Launch("ms-settings:windowsupdate")),
            (Glyphs.Apps, "Programas instalados", "Desinstalar programas", () => NavigateTo("apps")),
            (Glyphs.Power, "Aplicativos de inicialização", "O que abre com o Windows", () => NavigateTo("startup")),
        };
        var grid = Responsive(new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -12, 0) }, 260, 3);
        foreach (var (glyph, title, detail, run) in items) grid.Children.Add(ActionTile(glyph, title, detail, run));
        panel.Children.Add(grid);
        return Surface(panel);
    }

    private Button ActionTile(string glyph, string title, string detail, Action run)
    {
        var row = new DockPanel();
        var chip = IconChip(glyph, "Accent", 36); DockPanel.SetDock(chip, Dock.Left); row.Children.Add(chip);
        var arrow = GlyphIcon(Glyphs.ChevronRight, 11, "MutedBrush"); DockPanel.SetDock(arrow, Dock.Right); row.Children.Add(arrow);
        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var d = new TextBlock { Text = detail, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        text.Children.Add(t); text.Children.Add(d);
        row.Children.Add(text);
        var tile = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 12, 12) };
        tile.SetResourceReference(Button.BackgroundProperty, "PanelBrush");
        tile.SetResourceReference(Button.BorderBrushProperty, "BorderSubtleBrush");
        tile.Click += (_, _) => run();
        return tile;
    }

    private void Launch(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { ShowToast("Não foi possível abrir", ex.Message, "Danger"); }
    }

    private static readonly (string Name, string Category, string Description, string Url)[] Downloads =
    {
        ("DDU (Display Driver Uninstaller)", "Drivers", "Remove completamente drivers de vídeo e áudio.", "https://www.wagnardsoft.com/display-driver-uninstaller-ddu-"),
        ("NVCleanstall", "Drivers", "Instalador do driver NVIDIA sem telemetria e componentes extras.", "https://www.techpowerup.com/nvcleanstall/"),
        ("NVIDIA Profile Inspector", "Drivers", "Configurações avançadas e ocultas do driver NVIDIA por jogo.", "https://github.com/Orbmu2k/nvidiaProfileInspector/releases"),
        ("LatencyMon", "Diagnóstico", "Mede a latência DPC e aponta drivers que causam travadas e estalos.", "https://www.resplendence.com/latencymon"),
        ("HWiNFO", "Monitoramento", "Sensores de temperatura, clocks e voltagens de todo o hardware.", "https://www.hwinfo.com/download/"),
        ("MSI Afterburner", "Monitoramento", "Overclock da placa de vídeo e contador de FPS na tela (com o RTSS).", "https://www.msi.com/Landing/afterburner/graphics-cards"),
        ("CPU-Z", "Diagnóstico", "Detalhes do processador, da memória e da placa-mãe.", "https://www.cpuid.com/softwares/cpu-z.html"),
        ("CrystalDiskInfo", "Diagnóstico", "Saúde e temperatura de SSDs e HDs.", "https://crystalmark.info/en/software/crystaldiskinfo/"),
        ("CrystalDiskMark", "Diagnóstico", "Velocidade de leitura e gravação do disco.", "https://crystalmark.info/en/software/crystaldiskmark/"),
        ("Process Explorer", "Sistema", "Gerenciador de tarefas avançado da Microsoft (Sysinternals).", "https://learn.microsoft.com/sysinternals/downloads/process-explorer"),
        ("7-Zip", "Sistema", "Compactador de arquivos gratuito e leve.", "https://www.7-zip.org/"),
        ("Rufus", "Sistema", "Cria pendrives de instalação do Windows.", "https://rufus.ie/"),
    };

    private static readonly (string Name, string Category, string Description, string Url)[] Benchmarks =
    {
        ("Prime95", "CPU", "Teste de estresse da CPU e da memória: estabilidade e temperatura máxima.", "https://www.mersenne.org/download/"),
        ("OCCT", "Sistema", "Estresse de CPU, GPU, memória e fonte com detecção de erros.", "https://www.ocbase.com/"),
        ("Linpack Xtreme", "CPU", "Teste curto e pesado para validar overclock da CPU e da memória.", "https://www.techpowerup.com/download/linpack-xtreme/"),
        ("AIDA64", "Sistema", "Benchmark de memória e cache, estresse e sensores.", "https://www.aida64.com/downloads"),
        ("Cinebench", "CPU", "Pontuação de desempenho em um e em vários núcleos.", "https://www.maxon.net/cinebench"),
        ("3DMark", "GPU", "Benchmark de placa de vídeo usado como referência mundial.", "https://store.steampowered.com/app/223850/3DMark/"),
    };

    private Border LinksCard(string title, string subtitle, (string Name, string Category, string Description, string Url)[] links)
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader(title, subtitle));
        var grid = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -12, 0) }, 360, 2);
        foreach (var (name, category, description, url) in links)
        {
            var row = new DockPanel();
            var open = IconButton(Glyphs.OpenInNew, "Site oficial");
            open.Click += (_, _) => OpenUrl(url);
            DockPanel.SetDock(open, Dock.Right); row.Children.Add(open);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var t = Label(name, 13); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0); t.Tag = Translator.SystemDataTag;
            head.Children.Add(t);
            var pill = Pill(category, "Info"); pill.Margin = new Thickness(8, 0, 0, 0); head.Children.Add(pill);
            text.Children.Add(head);
            var d = Label(description, 11.5, true); d.Margin = new Thickness(0, 3, 0, 0); text.Children.Add(d);
            row.Children.Add(text);
            var item = ListRow(row); item.Margin = new Thickness(0, 0, 12, 10);
            grid.Children.Add(item);
        }
        panel.Children.Add(grid);
        return Surface(panel);
    }

    private UIElement BenchmarkCard()
    {
        var panel = new StackPanel();
        var builtIn = new DockPanel();
        var run = IconButton(Glyphs.Play, "Rodar benchmark", primary: true);
        run.Click += (_, _) => _ = PrepareOperationAsync("benchmark");
        DockPanel.SetDock(run, Dock.Right); builtIn.Children.Add(run);
        builtIn.Children.Add(FeatureHeader(Glyphs.Speed, "Accent", "Benchmark do PQueiroz Optimizer", "Mede CPU, disco e tempo de inicialização para comparar antes e depois das otimizações. Os resultados ficam em Atividade e reversão.", "Integrado", "Success"));
        var hero = Surface(builtIn); hero.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        panel.Children.Add(hero);
        panel.Children.Add(LinksCard("Ferramentas de estresse e benchmark", "Use para testar estabilidade depois de overclock ou para comparar com outros PCs.", Benchmarks));
        return panel;
    }

    // ================= Correções =================
    private void ShowFixes()
    {
        PageTitle.Text = "Correções";
        PageBadge.Visibility = Visibility.Visible;
        PageBadgeText.Text = $"{FixesService.Fixes.Length} correções";
        var root = new StackPanel();
        var grid = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -14, 0) }, 360, 2);
        foreach (var fix in FixesService.Fixes)
        {
            var body = new DockPanel();
            var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var runBtn = IconButton(Glyphs.Play, "Executar correção");
            runBtn.Click += async (_, _) =>
            {
                if (fix.NeedsRestart && Msg($"\"{fix.Name}\" precisa reiniciar o PC para terminar. Executar agora?", "Correções", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                FixResult? result = null;
                var progress = new Progress<string>(line => OperationStatus.Text = line);
                await ExecuteTrackedAsync(fix.Name, async _ => result = await new FixesService(_log).RunAsync(fix, progress));
                if (result is { } r && r.Succeeded < r.Total)
                    ShowToast(fix.Name, $"{r.Succeeded}/{r.Total} etapas concluídas. Erro: {r.FirstError}", r.Succeeded == 0 ? "Danger" : "Warning");
            };
            DockPanel.SetDock(runBtn, Dock.Right); footer.Children.Add(runBtn);
            var pills = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            pills.Children.Add(Pill(fix.Category, "Info"));
            if (fix.NeedsRestart) { var p = Pill("Reinicia o PC", "Warning"); p.Margin = new Thickness(6, 0, 0, 0); pills.Children.Add(p); }
            footer.Children.Add(pills);
            DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
            var head = new DockPanel();
            var chip = IconChip(fix.Glyph, "Accent", 40); chip.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(chip, Dock.Left); head.Children.Add(chip);
            var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            var t = Label(fix.Name, 14); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 1, 0, 4); text.Children.Add(t);
            var d = Label(fix.Description, 12, true); d.Margin = new Thickness(0); text.Children.Add(d);
            head.Children.Add(text);
            body.Children.Add(head);
            var card = Surface(body); card.Margin = new Thickness(0, 0, 14, 14);
            grid.Children.Add(card);
        }
        root.Children.Add(grid);
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    // ================= Serviços =================
    private void ShowServices()
    {
        PageTitle.Text = "Serviços";
        var disabledCount = ServiceGroupsService.Groups.Count(ServiceGroupsService.IsDisabled);
        PageBadge.Visibility = Visibility.Visible;
        PageBadgeText.Text = $"{disabledCount} de {ServiceGroupsService.Groups.Length} desligados";
        var root = new StackPanel();
        var service = new ServiceGroupsService(_log);
        var host = new StackPanel();
        foreach (var category in ServiceGroupsService.Groups.Select(g => g.Category).Distinct())
        {
            var section = new StackPanel();
            section.Children.Add(SectionHeader(category));
            foreach (var group in ServiceGroupsService.Groups.Where(g => g.Category == category))
            {
                var existing = ServiceGroupsService.Existing(group);
                var disabled = ServiceGroupsService.IsDisabled(group);
                var check = new CheckBox { IsChecked = disabled, IsEnabled = existing.Length > 0 };
                var detail = group.Description + (group.Warning != null ? "\n⚠ " + group.Warning : "") + $"\nServiços: {string.Join(", ", existing.DefaultIfEmpty("nenhum neste Windows"))}";
                var row = ChoiceRow(check, group.Name, detail, disabled ? "Desligado" : "Ligado", disabled ? "Success" : "Info");
                check.Checked += async (_, _) => { if (!await ExecuteTrackedAsync("Desligando: " + group.Name, async _ => await service.DisableAsync(group))) check.IsChecked = false; if (_currentPage == "services") ShowServices(); };
                check.Unchecked += async (_, _) => { if (!await ExecuteTrackedAsync("Restaurando: " + group.Name, async _ => await service.RestoreAsync(group))) check.IsChecked = true; if (_currentPage == "services") ShowServices(); };
                section.Children.Add(row);
            }
            host.Children.Add(Surface(section));
        }
        root.Children.Add(Mark(host, "services.list"));

        var defender = new DockPanel();
        var open = IconButton(Glyphs.ChevronRight, "Abrir");
        open.Click += (_, _) => OpenGamingTab(3);
        DockPanel.SetDock(open, Dock.Right); defender.Children.Add(open);
        defender.Children.Add(FeatureHeader(Glyphs.Shield, "Warning", "Windows Defender", "Exclusões de pastas de jogos e liga/desliga da proteção em tempo real ficam em Modo Jogo → Sistema.", "Em Modo Jogo", "Info"));
        root.Children.Add(Surface(defender));
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    // ================= Apps =================
    private void ShowApps()
    {
        PageTitle.Text = "Apps";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        root.Children.Add(Tabs(new[] { (Glyphs.Lightning, "Otimizar apps"), (Glyphs.Delete, "Desinstalar") }, _appsTab, tab => { _appsTab = tab; ShowApps(); AnimatePageIn(); }));
        if (_appsTab == 0) root.Children.Add(AppOptimizerCard());
        else root.Children.Add(UninstallCard());
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private Border AppOptimizerCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Otimizador de apps", "Apps que ficam abertos enquanto você joga também usam GPU e CPU. Cada ajuste é reversível: desmarque para voltar ao que era."));
        var service = new AppOptimizerService(_log);
        foreach (var tweak in AppOptimizerService.Tweaks)
        {
            var installed = AppOptimizerService.IsInstalled(tweak);
            var applied = installed && service.IsApplied(tweak);
            var check = new CheckBox { IsChecked = applied, IsEnabled = installed };
            var row = ChoiceRow(check, $"{tweak.App} — {tweak.Title}", tweak.Description, !installed ? "Não instalado" : applied ? "Aplicado" : "Padrão", !installed ? "Warning" : applied ? "Success" : "Info");
            void Run(bool apply)
            {
                try
                {
                    if (apply) service.Apply(tweak); else service.Revert(tweak);
                    ShowToast(tweak.App, apply ? "Ajuste aplicado. Reabra o app para valer." : "Configuração original restaurada.", "Success");
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    ShowToast(tweak.App, ex.Message, "Danger");
                }
                ShowApps();
            }
            check.Checked += (_, _) => Run(true);
            check.Unchecked += (_, _) => Run(false);
            panel.Children.Add(row);
        }
        return Surface(panel);
    }

    private Border UninstallCard()
    {
        var panel = new StackPanel();
        var head = new DockPanel();
        var auto = IconButton(Glyphs.Shield, "Debloat automático", primary: true);
        auto.VerticalAlignment = VerticalAlignment.Top;
        auto.Click += (_, _) => _ = PrepareOperationAsync("debloat");
        DockPanel.SetDock(auto, Dock.Right); head.Children.Add(auto);
        head.Children.Add(SectionHeader("Desinstalar programas", "Programas da área de trabalho abrem o desinstalador do fabricante; apps da Loja são removidos direto. O Debloat automático remove os apps que vêm com o Windows e quase ninguém usa."));
        panel.Children.Add(head);

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var searchField = new Grid { Width = 280, Margin = new Thickness(12, 0, 0, 0) };
        var search = new TextBox { Text = _appsSearch, Height = 38 };
        var hint = new TextBlock { Text = "Buscar programa…", IsHitTestVisible = false, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Visibility = _appsSearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        searchField.Children.Add(search); searchField.Children.Add(hint);
        DockPanel.SetDock(searchField, Dock.Right); bar.Children.Add(searchField);
        var types = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (store, title) in new[] { (false, "Área de trabalho"), (true, "Microsoft Store") })
        {
            var b = new Button { Content = title, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), FontSize = 12 };
            if (_appsStore == store) Primary(b);
            b.Click += (_, _) => { _appsStore = store; ShowApps(); };
            types.Children.Add(b);
        }
        bar.Children.Add(types);
        panel.Children.Add(bar);

        var list = new StackPanel();
        list.Children.Add(Label("Lendo os programas instalados...", 12.5, true));
        panel.Children.Add(list);
        List<InstalledApp>? apps = null;
        void Render()
        {
            list.Children.Clear();
            if (apps is null) return;
            var q = _appsSearch.Trim();
            var visible = apps.Where(a => q.Length == 0 || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (visible.Count == 0) { list.Children.Add(EmptyState(Glyphs.Search, "Nada encontrado", "Tente outro nome ou troque entre Área de trabalho e Microsoft Store.")); return; }
            foreach (var app in visible.Take(150))
            {
                var row = new DockPanel();
                var remove = IconButton(Glyphs.Delete, "Desinstalar");
                remove.Click += async (_, _) =>
                {
                    if (Msg($"Desinstalar \"{app.Name}\"?", "Desinstalar", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    if (await ExecuteTrackedAsync("Desinstalando " + app.Name, async _ => await new AppOptimizerService(_log).UninstallAsync(app)) && _currentPage == "apps") ShowApps();
                };
                DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var t = Label(app.Name, 13); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0); t.Tag = Translator.SystemDataTag;
                text.Children.Add(t);
                var d = Label(string.Join(" · ", new[] { app.Publisher, app.Version }.Where(s => s.Length > 0)), 11.5, true); d.Margin = new Thickness(0, 2, 0, 0); d.Tag = Translator.SystemDataTag;
                text.Children.Add(d);
                row.Children.Add(text);
                list.Children.Add(ListRow(row));
            }
            if (visible.Count > 150) list.Children.Add(Label($"Mostrando 150 de {visible.Count}. Use a busca para encontrar os outros.", 12, true));
        }
        search.TextChanged += (_, _) => { _appsSearch = search.Text; hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Render(); };
        _ = LoadApps();
        async Task LoadApps()
        {
            try { apps = _appsStore ? await AppOptimizerService.StoreAppsAsync() : await Task.Run(AppOptimizerService.DesktopApps); }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Text.Json.JsonException) { list.Children.Clear(); list.Children.Add(Label("Não foi possível listar os programas: " + ex.Message, 12.5, true)); return; }
            Render();
        }
        return Surface(panel);
    }

    // ================= Otimização completa (Visão geral) =================
    /// <summary>Passo a passo das otimizações recomendadas, na ordem certa (como o "Fix All" do Paragon).</summary>
    private Border FixAllCard()
    {
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var pill = Pill("6 passos", "Accent"); DockPanel.SetDock(pill, Dock.Right); pill.VerticalAlignment = VerticalAlignment.Top; head.Children.Add(pill);
        var titles = new StackPanel();
        var t = Label("Otimização completa", 16); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 0, 0, 2); titles.Children.Add(t);
        var d = Label("Siga na ordem para revisar, aplicar e conferir as otimizações recomendadas.", 12, true); d.Margin = new Thickness(0); titles.Children.Add(d);
        head.Children.Add(titles);
        panel.Children.Add(head);

        var steps = new (string Glyph, string Title, string Detail, Action Run)[]
        {
            (Glyphs.Broom, "Limpeza", "Arquivos temporários seguros", () => _ = PrepareOperationAsync("quickclean")),
            (Glyphs.Monitor, "Driver de vídeo", "Instalação limpa recomendada", () => NavigateTo("drivers")),
            (Glyphs.Lightning, "Ajustes recomendados", "Revisados item por item", () => _ = PrepareOperationAsync("padrao")),
            (Glyphs.Services, "Serviços", "Segundo plano mais leve", () => NavigateTo("services")),
            (Glyphs.Game, "Perfis de jogos", "Presets dos jogos instalados", () => OpenGamingTab(1)),
            (Glyphs.Chip, "BIOS", "Grupos de ajustes da placa", () => NavigateTo("bios")),
        };
        var grid = Responsive(new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, -10, 0) }, 150, 6);
        for (var i = 0; i < steps.Length; i++)
        {
            var (glyph, title, detail, run) = steps[i];
            var stack = new StackPanel();
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var number = new TextBlock { Text = (i + 1).ToString("00"), FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Tag = Translator.SystemDataTag };
            number.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            DockPanel.SetDock(number, Dock.Right); top.Children.Add(number);
            top.Children.Add(IconChip(glyph, "Accent", 30));
            stack.Children.Add(top);
            var st = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            st.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var sd = new TextBlock { Text = detail, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            sd.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            stack.Children.Add(st); stack.Children.Add(sd);
            var tile = new Button { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(12, 12, 12, 12), Margin = new Thickness(0, 0, 10, 0), ToolTip = title };
            tile.SetResourceReference(Button.BackgroundProperty, "PanelBrush");
            tile.SetResourceReference(Button.BorderBrushProperty, "BorderSubtleBrush");
            tile.Click += (_, _) => run();
            grid.Children.Add(tile);
        }
        panel.Children.Add(grid);
        return Surface(panel);
    }

    // ================= Win32 Priority (Modo Jogo → Sistema) =================
    private Border Win32PriorityCard()
    {
        var panel = new StackPanel();
        var service = new Win32PriorityService(_log);
        var current = Win32PriorityService.Current();
        var head = new DockPanel();
        if (service.IsChanged)
        {
            var restore = IconButton(Glyphs.Undo, "Restaurar original");
            restore.VerticalAlignment = VerticalAlignment.Top;
            restore.Click += (_, _) => { try { service.Restore(); ShowGaming(); } catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { ShowToast("Win32 Priority", ex.Message, "Danger"); } };
            DockPanel.SetDock(restore, Dock.Right); head.Children.Add(restore);
        }
        head.Children.Add(SectionHeader("Win32 Priority", $"Quanto o Windows favorece o programa em foco (o jogo). Valor atual: 0x{current:X2}. Vale na hora, sem reiniciar."));
        panel.Children.Add(head);
        var grid = Responsive(new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -10, 0) }, 260, 3);
        foreach (var level in Win32PriorityService.Levels)
        {
            var selected = level.Value == current;
            var tile = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 10, 10) };
            var stack = new StackPanel();
            var top = new DockPanel();
            var code = Pill($"0x{level.Value:X2}", selected ? "Success" : "Info"); DockPanel.SetDock(code, Dock.Right); top.Children.Add(code);
            ((TextBlock)code.Child).Tag = Translator.SystemDataTag;
            var t = new TextBlock { Text = level.Label, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            top.Children.Add(t);
            stack.Children.Add(top);
            var d = new TextBlock { Text = level.Description, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            stack.Children.Add(d);
            tile.Content = stack;
            tile.SetResourceReference(Button.BackgroundProperty, selected ? "AccentSoftBrush" : "PanelBrush");
            tile.SetResourceReference(Button.BorderBrushProperty, selected ? "AccentBrush" : "BorderSubtleBrush");
            tile.Click += (_, _) =>
            {
                try { service.Set(level); ShowToast("Win32 Priority", $"{level.Label} aplicado.", "Success"); ShowGaming(); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException) { ShowToast("Win32 Priority", ex.Message, "Danger"); }
            };
            grid.Children.Add(tile);
        }
        panel.Children.Add(grid);
        return Surface(panel);
    }
}
