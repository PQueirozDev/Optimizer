using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Recursos: automação, atalhos, downloads e benchmarks.</summary>
public partial class MainWindow
{
    private int _resourcesTab;

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
        builtIn.Children.Add(FeatureHeader(Glyphs.Speed, "Accent", "Benchmark do Qrztweaks", "Mede CPU, disco e tempo de inicialização para comparar antes e depois das otimizações. Os resultados ficam em Atividade e reversão.", "Integrado", "Success"));
        var hero = Surface(builtIn); hero.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        panel.Children.Add(hero);
        panel.Children.Add(LinksCard("Ferramentas de estresse e benchmark", "Use para testar estabilidade depois de overclock ou para comparar com outros PCs.", Benchmarks));
        return panel;
    }
}
