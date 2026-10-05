using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.BiosAdvisor;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// BIOS Advisor: detecta o hardware, analisa com o motor de regras e mostra o que conferir/alterar na BIOS.
/// Só leitura: nada aqui grava na BIOS, faz flash ou muda tensões. A lógica fica em BiosAdvisor/; esta página só desenha.
/// </summary>
public partial class MainWindow
{
    private BiosAdvisorService? _advisor;
    private BiosAdvisorService Advisor => _advisor ??= new BiosAdvisorService(_log);
    private AdvisorReport? _advisorReport;
    private bool _advisorShowNotApplicable;

    private string L(LocalizedText text) => text.Get(_loc.IsEnglish);
    private string T(string pt, string en) => _loc.T(pt, en);

    private void ShowBiosAdvisor()
    {
        PageTitle.Text = "BIOS Advisor"; PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        root.Children.Add(AdvisorHero());

        var detection = Advisor.DetectAsync();
        if (!detection.IsCompleted)
        {
            root.Children.Add(Surface(EmptyState(Glyphs.Chip, T("Detectando o hardware…", "Detecting hardware…"),
                T("Lendo placa-mãe, BIOS, processador, memória e placa de vídeo pelo Windows. Leva alguns segundos.",
                  "Reading motherboard, BIOS, CPU, memory and graphics card from Windows. Takes a few seconds."))));
            _ = RedrawWhenDetectedAsync(detection);
            return;
        }
        if (detection.IsFaulted || detection.IsCanceled)
        {
            var retry = IconButton(Glyphs.Refresh, T("Tentar de novo", "Try again"), primary: true);
            retry.HorizontalAlignment = HorizontalAlignment.Center;
            retry.Click += (_, _) => { Advisor.DetectAsync(refresh: true); _biosUpdateCheck = null; ShowBiosAdvisor(); };
            var failed = new StackPanel();
            failed.Children.Add(EmptyState(Glyphs.Warning, T("Não foi possível detectar o hardware", "Couldn't detect the hardware"),
                detection.Exception?.GetBaseException().Message ?? T("Detecção cancelada.", "Detection canceled.")));
            failed.Children.Add(retry);
            root.Children.Add(Surface(failed));
            return;
        }

        var report = _advisorReport = Advisor.Analyze(detection.Result);
        root.Children.Add(AdvisorHardwareCard(report));
        root.Children.Add(AdvisorBiosReadCard(report));
        _ = RefreshAdvisorDatabaseAsync();
        foreach (var w in report.Warnings) root.Children.Add(Notice(L(w), "Warning"));
        var presets = AdvisorPresetTabs(report);
        presets.Margin = new Thickness(0, 16, 0, 16);
        root.Children.Add(presets);
        root.Children.Add(AdvisorScoreCard(report));

        var row = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -14, 0) }, 420, 2);
        row.Children.Add(Spaced(AdvisorBiosVersionCard(report)));
        row.Children.Add(Spaced(AdvisorMemoryCard(report)));
        root.Children.Add(row);
        root.Children.Add(AdvisorMonitorCard(report));
        root.Children.Add(AdvisorRecommendations(report));
        root.Children.Add(AdvisorBenchmarkCard(report));
        var disclaimer = Label(T("O BIOS Advisor fornece recomendações baseadas no hardware detectado. Configurações disponíveis podem variar conforme versão da BIOS. Nada é alterado automaticamente: o app não grava na BIOS, não faz atualização (flash) e não muda tensões.",
            "BIOS Advisor gives recommendations based on the detected hardware. Available settings may vary with the BIOS version. Nothing is changed automatically: the app never writes to the BIOS, never flashes it and never changes voltages."), 12, true);
        disclaimer.Margin = new Thickness(4, 4, 0, 8);
        root.Children.Add(disclaimer);
    }

    private static FrameworkElement Spaced(Border card) { card.Margin = new Thickness(0, 0, 14, 16); return card; }

    private async Task RedrawWhenDetectedAsync(Task<HardwareProfile> detection)
    {
        try { await detection; }
        catch (Exception ex) { _log.Write("ERROR", "BIOS Advisor: falha na detecção: " + ex.GetBaseException().Message); }
        if (_currentPage == "biosadvisor" && IsLoaded) KeepScroll(ShowBiosAdvisor);
    }

    private Border AdvisorHero()
    {
        var hero = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var detect = IconButton(Glyphs.Refresh, T("Detectar de novo", "Detect again"));
        detect.Click += (_, _) => { Advisor.DetectAsync(refresh: true); _advisorReport = null; _biosUpdateCheck = null; ShowBiosAdvisor(); };
        var restart = IconButton(Glyphs.Power, T("Reiniciar na BIOS/UEFI", "Restart into BIOS/UEFI"), primary: true);
        restart.Margin = new Thickness(8, 0, 0, 0);
        restart.Click += (_, _) => RestartIntoFirmware();
        actions.Children.Add(detect); actions.Children.Add(restart);
        actions.Margin = new Thickness(16, 0, 0, 0);
        DockPanel.SetDock(actions, Dock.Right); hero.Children.Add(actions);
        var chip = IconChip(Glyphs.Chip, "Accent", 52); DockPanel.SetDock(chip, Dock.Left); hero.Children.Add(chip);
        var text = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var eyebrow = new TextBlock { Text = "BIOS ADVISOR", FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) };
        eyebrow.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        text.Children.Add(eyebrow);
        var title = Label(T("Onde ainda existe margem de desempenho na sua BIOS", "Where your BIOS still has performance headroom"), 20); title.Margin = new Thickness(0, 0, 0, 4);
        text.Children.Add(title);
        var sub = Label(T("Analisa placa, processador, memória e vídeo e mostra exatamente o que conferir ou mudar na BIOS — com o caminho, o risco e como desfazer. Você aplica à mão; o app não altera a BIOS.",
            "Analyzes board, CPU, memory and graphics and shows exactly what to check or change in the BIOS — with the path, the risk and how to undo it. You apply it by hand; the app never changes the BIOS."), 12.5, true);
        sub.Margin = new Thickness(0);
        text.Children.Add(sub);
        hero.Children.Add(text);
        var card = Surface(hero); card.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        return card;
    }

    private void RestartIntoFirmware()
    {
        var answer = Msg(
            "O computador será reiniciado AGORA e abrirá diretamente as configurações de firmware (BIOS/UEFI).\n\nSalve seus trabalhos e feche outros programas antes de continuar.\n\nDeseja reiniciar agora?",
            "Reiniciar na BIOS/UEFI", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        if (_operationRunning) { OperationStatus.Text = "Aguarde a operação em andamento terminar antes de reiniciar."; return; }
        try
        {
            _log.Write("INFO", "Reinício para a BIOS/UEFI solicitado pelo usuário.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/r /fw /t 5") { UseShellExecute = true, CreateNoWindow = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Msg("Não foi possível reiniciar na BIOS: " + ex.Message, "BIOS / UEFI", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    // ================= Hardware detectado =================
    private Border AdvisorHardwareCard(AdvisorReport report)
    {
        var p = report.Profile;
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var profilePill = report.SpecificProfileAvailable
            ? Pill(T("Perfil específico: ", "Specific profile: ") + report.SpecificProfileName, "Success")
            : Pill(T("Perfil específico ainda não disponível", "Specific profile not available yet"), "Warning");
        DockPanel.SetDock(profilePill, Dock.Right); head.Children.Add(profilePill);
        var title = Label(T("Hardware detectado", "Detected hardware"), 16); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0);
        head.Children.Add(title);
        panel.Children.Add(head);

        var gb = p.Memory.TotalBytes / 1024d / 1024 / 1024;
        var memType = p.Memory.Type switch { MemoryType.Ddr5 => "DDR5", MemoryType.Ddr4 => "DDR4", MemoryType.Ddr3 => "DDR3", _ => "" };
        var memory = MemoryAnalyzer.Analyze(p.Memory, report.Platform);
        var channel = memory.Channels switch { ChannelMode.Dual => "Dual Channel", ChannelMode.Single => "Single Channel", ChannelMode.Multi => "Multi Channel", _ => "" };
        var gpu = p.PrimaryGpu;
        var cells = new (string Label, string Value, string? Detail, string Glyph)[]
        {
            (T("PLACA-MÃE", "MOTHERBOARD"), $"{BrandName(p.Motherboard.Manufacturer)} {p.Motherboard.Product}".Trim(), report.Platform.Chipset is { } cs ? T($"Chipset {cs}", $"{cs} chipset") : null, Glyphs.Chip),
            (T("PROCESSADOR", "CPU"), CleanCpuName(p.Cpu.Name), p.Cpu.Cores > 0 ? T($"{p.Cpu.Cores} núcleos · {p.Cpu.Threads} threads · base {(p.Cpu.BaseClockMhz / 1000d).ToString("0.0#", new System.Globalization.CultureInfo("pt-BR"))} GHz", $"{p.Cpu.Cores} cores · {p.Cpu.Threads} threads · {(p.Cpu.BaseClockMhz / 1000d).ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)} GHz base") : null, Glyphs.Speed),
            (T("MEMÓRIA", "MEMORY"), gb > 0 ? $"{gb:0.#} GB {memType}".Trim() : "", string.Join(" · ", new[] { channel, p.Memory.ConfiguredMhz > 0 ? $"{p.Memory.ConfiguredMhz} MHz" : "", T($"{p.Memory.Modules.Count} pente(s)", $"{p.Memory.Modules.Count} module(s)") }.Where(s => s.Length > 0)), Glyphs.Memory),
            (T("PLACA DE VÍDEO", "GRAPHICS"), gpu?.Name ?? "", gpu is null ? null : string.Join(" · ", new[] { gpu.VramBytes is { } v ? $"{v / 1024d / 1024 / 1024:0.#} GB VRAM" : "", gpu.DriverVersion.Length > 0 ? "Driver " + gpu.DriverVersion : "" }.Where(s => s.Length > 0)), Glyphs.Monitor),
            ("BIOS", p.Bios.Version.Length > 0 ? "BIOS " + p.Bios.Version : "", string.Join(" · ", new[] { p.Bios.ReleaseDate?.ToString(_loc.IsEnglish ? "yyyy-MM-dd" : "dd/MM/yyyy") ?? "", p.Bios.Firmware switch { FirmwareMode.Uefi => "UEFI", FirmwareMode.Legacy => "Legacy (CSM)", _ => "" } }.Where(s => s.Length > 0)), Glyphs.Settings),
        };
        var grid = Responsive(new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, -10, 0) }, 180, 5);
        foreach (var (label, value, detail, glyph) in cells)
        {
            var cell = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var icon = GlyphIcon(glyph, 12, "AccentBrush"); icon.Margin = new Thickness(0, 0, 7, 0);
            top.Children.Add(icon);
            var l = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold };
            l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            top.Children.Add(l);
            cell.Children.Add(top);
            var known = !string.IsNullOrWhiteSpace(value);
            var valueText = new TextBlock { Text = known ? value : T("Não identificado", "Not identified"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Tag = known ? Translator.SystemDataTag : null };
            valueText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            cell.Children.Add(valueText);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                var d = new TextBlock { Text = detail, FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, Tag = Translator.SystemDataTag };
                d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                cell.Children.Add(d);
            }
            var box = ListRow(cell); box.Margin = new Thickness(0, 0, 10, 10);
            grid.Children.Add(box);
        }
        panel.Children.Add(grid);
        if (BiosDatabase.Current.BoardFor(p.Motherboard) is { } board)
            foreach (var fact in board.Facts)
            {
                var f = Label("• " + L(fact), 12, true); f.Margin = new Thickness(2, 2, 0, 0);
                panel.Children.Add(f);
            }
        return Surface(panel);
    }

    private static string BrandName(string manufacturer) => manufacturer.Trim() switch
    {
        var m when m.StartsWith("ASUSTeK", StringComparison.OrdinalIgnoreCase) => "ASUS",
        var m when m.StartsWith("Micro-Star", StringComparison.OrdinalIgnoreCase) => "MSI",
        var m when m.StartsWith("Gigabyte", StringComparison.OrdinalIgnoreCase) => "GIGABYTE",
        var m => m,
    };

    private static string CleanCpuName(string name) => System.Text.RegularExpressions.Regex.Replace(name.Replace("(R)", "").Replace("(TM)", ""), @"\s+(CPU\s*)?@.*$|\s+\d+-Core Processor$", "").Replace("  ", " ").Trim();

    // ================= Presets =================
    private Border AdvisorPresetTabs(AdvisorReport report)
    {
        var tabs = new[]
        {
            (Glyphs.Shield, T("Seguro", "Safe")),
            (Glyphs.Lightning, T("Desempenho", "Performance")),
            (Glyphs.Game, T("Competitivo", "Competitive")),
        };
        var bar = Tabs(tabs, (int)report.Preset, i =>
        {
            Advisor.SetPreset((AdvisorPreset)i);
            KeepScroll(ShowBiosAdvisor);
        });
        var description = report.Preset switch
        {
            AdvisorPreset.Safe => T("Estabilidade e baixo risco: perfil de memória, Turbo, Speed Shift e o básico.", "Stability and low risk: memory profile, Turbo, Speed Shift and the basics."),
            AdvisorPreset.Performance => T("Máximo desempenho razoável: inclui limites de energia, PBO e ajustes adicionais de memória.", "Maximum reasonable performance: adds power limits, PBO and extra memory settings."),
            _ => T("Jogos competitivos: foco em FPS, 1% lows, frametime e latência. Inclui opções com troca de consumo por resposta.", "Competitive gaming: focused on FPS, 1% lows, frametime and latency. Adds options that trade power for responsiveness."),
        };
        var wrap = new DockPanel();
        DockPanel.SetDock(bar, Dock.Left); bar.Margin = new Thickness(0, 0, 16, 0);
        wrap.Children.Add(bar);
        var d = Label(description, 12, true); d.VerticalAlignment = VerticalAlignment.Center; d.Margin = new Thickness(0);
        wrap.Children.Add(d);
        return new Border { Child = wrap };
    }

    // ================= Nota =================
    private Border AdvisorScoreCard(AdvisorReport report)
    {
        var s = report.Score;
        var dock = new DockPanel();
        var left = new StackPanel { Width = 180, Margin = new Thickness(0, 0, 24, 0) };
        if (s.Overall is { } overall) left.Children.Add(ScoreRing(overall, "/ 100", 140));
        else
        {
            var none = Label("—", 24); none.HorizontalAlignment = HorizontalAlignment.Center;
            left.Children.Add(none);
        }
        var coverage = Label(T($"Baseado em {s.Verifiable} de {s.Total} itens com evidência", $"Based on {s.Verifiable} of {s.Total} items with evidence"), 11, true);
        coverage.TextAlignment = TextAlignment.Center; coverage.Margin = new Thickness(0, 10, 0, 0);
        left.Children.Add(coverage);
        DockPanel.SetDock(left, Dock.Left); dock.Children.Add(left);

        var right = new StackPanel();
        var title = Label("BIOS Optimization Score", 16); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 0, 4);
        right.Children.Add(title);
        var explain = Label(T("A nota só conta o que tem evidência: lido do Windows, lido da BIOS (SCEWIN) e conferido por você valem inteiro; deduzido vale metade. O que só dá para ver na BIOS fica fora da nota até ser lido ou conferido.",
            "The score only counts what has evidence: read from Windows, read from the BIOS (SCEWIN) and checked by you count fully; inferred counts half. What can only be seen in the BIOS stays out of the score until it is read or checked."), 12, true);
        right.Children.Add(explain);
        var grid = Responsive(new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, -10, 0) }, 190, 3);
        foreach (var cat in s.Categories)
        {
            var cell = new StackPanel();
            var row = new DockPanel();
            var value = new TextBlock { Text = cat.Score is { } v ? $"{v}" : T("verificar na BIOS", "check in BIOS"), FontSize = cat.Score is null ? 11 : 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            value.SetResourceReference(TextBlock.ForegroundProperty, cat.Score is null ? "WarningBrush" : cat.Score >= 80 ? "SuccessBrush" : cat.Score >= 50 ? "WarningBrush" : "DangerBrush");
            DockPanel.SetDock(value, Dock.Right); row.Children.Add(value);
            var name = new TextBlock { Text = CategoryName(cat.Category, scoreLabel: true), FontSize = 12.5, FontWeight = FontWeights.SemiBold };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            row.Children.Add(name);
            cell.Children.Add(row);
            var bar = new ProgressBar { Height = 4, Maximum = 100, Value = cat.Score ?? 0, Margin = new Thickness(0, 8, 0, 6) };
            cell.Children.Add(bar);
            var detail = new TextBlock { Text = T($"{cat.Verifiable} de {cat.Total} com evidência", $"{cat.Verifiable} of {cat.Total} with evidence"), FontSize = 11 };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            cell.Children.Add(detail);
            var box = ListRow(cell); box.Margin = new Thickness(0, 0, 10, 10);
            grid.Children.Add(box);
        }
        right.Children.Add(grid);
        var legend = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var e in new[] { Evidence.Detected, Evidence.ReadFromBios, Evidence.Inferred, Evidence.NeedsBiosCheck, Evidence.UserConfirmed })
        {
            var (text, tone) = EvidenceLabel(e);
            var pill = Pill(text, tone); pill.Margin = new Thickness(0, 0, 6, 6);
            legend.Children.Add(pill);
        }
        right.Children.Add(legend);
        dock.Children.Add(right);
        return Surface(dock);
    }

    private string CategoryName(AdvisorCategory c, bool scoreLabel = false) => c switch
    {
        AdvisorCategory.Cpu => scoreLabel ? "CPU Performance" : "CPU",
        AdvisorCategory.Ram => scoreLabel ? T("Memória", "Memory") : "RAM",
        AdvisorCategory.Power => scoreLabel ? T("Energia", "Power") : "POWER",
        AdvisorCategory.Latency => scoreLabel ? T("Latência", "Latency") : "LATENCY",
        AdvisorCategory.Pcie => "PCIe",
        AdvisorCategory.Security => T("Segurança", "Security"),
        AdvisorCategory.Virtualization => T("Virtualização", "Virtualization"),
        AdvisorCategory.Boot => "Boot",
        AdvisorCategory.Thermal => scoreLabel ? T("Térmico", "Thermal") : "THERMAL",
        _ => c.ToString(),
    };

    private (string Text, string Tone) EvidenceLabel(Evidence e) => e switch
    {
        Evidence.Detected => (T("DETECTADO", "DETECTED"), "Success"),
        Evidence.ReadFromBios => (T("LIDO DA BIOS", "READ FROM BIOS"), "Success"),
        Evidence.Inferred => (T("INFERIDO", "INFERRED"), "Info"),
        Evidence.NeedsBiosCheck => (T("PRECISA SER VERIFICADO NA BIOS", "NEEDS BIOS CHECK"), "Warning"),
        Evidence.UserConfirmed => (T("CONFERIDO POR VOCÊ", "CHECKED BY YOU"), "Accent"),
        _ => (T("NÃO SE APLICA", "NOT APPLICABLE"), "Info"),
    };

    private string LevelName(Level level) => level switch { Level.Low => T("Baixo", "Low"), Level.Medium => T("Médio", "Medium"), _ => T("Alto", "High") };
    private string ThermalName(ThermalImpact t) => t switch { ThermalImpact.None => T("Nenhum", "None"), ThermalImpact.Low => T("Baixo", "Low"), ThermalImpact.Medium => T("Médio", "Medium"), _ => T("Alto", "High") };

    // ================= Recomendações =================
    private StackPanel AdvisorRecommendations(AdvisorReport report)
    {
        var host = new StackPanel();
        var applicable = report.Recommendations.Where(r => r.Evidence != Evidence.NotApplicable).ToList();
        var attention = applicable.Count(r => r.Compliance == Compliance.Attention);
        var check = applicable.Count(r => r.Evidence == Evidence.NeedsBiosCheck);
        host.Children.Add(SectionHeader(T("Recomendações", "Recommendations"),
            T($"{applicable.Count} itens para este hardware · {attention} pedem ajuste · {check} precisam ser vistos na BIOS.",
              $"{applicable.Count} items for this hardware · {attention} need changes · {check} must be checked in the BIOS.")));

        // Primeiro o que precisa de ajuste, depois o que falta conferir, por fim o que já está certo
        var order = applicable.OrderBy(r => r.Compliance switch { Compliance.Attention => 0, Compliance.Unknown => 1, Compliance.Info => 2, _ => 3 }).ThenBy(r => r.Category);
        var grid = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -14, 0) }, 440, 2);
        foreach (var rec in order) grid.Children.Add(Spaced(AdvisorRecommendationCard(report, rec)));
        host.Children.Add(grid);

        var notApplicable = report.Recommendations.Where(r => r.Evidence == Evidence.NotApplicable).ToList();
        if (notApplicable.Count > 0)
        {
            var toggle = new Button { Content = (_advisorShowNotApplicable ? T("Ocultar", "Hide") : T("Mostrar", "Show")) + T($" itens que não se aplicam a este PC ({notApplicable.Count})", $" items that don't apply to this PC ({notApplicable.Count})"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
            toggle.SetResourceReference(StyleProperty, "GhostButton");
            toggle.Click += (_, _) => { _advisorShowNotApplicable = !_advisorShowNotApplicable; KeepScroll(ShowBiosAdvisor); };
            host.Children.Add(toggle);
            if (_advisorShowNotApplicable)
                foreach (var rec in notApplicable)
                {
                    var row = new StackPanel();
                    var t = Label(L(rec.Name), 13); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 0, 0, 2);
                    row.Children.Add(t);
                    var d = Label(L(rec.Description), 12, true); d.Margin = new Thickness(0);
                    row.Children.Add(d);
                    host.Children.Add(ListRow(row));
                }
        }
        return host;
    }

    private Border AdvisorRecommendationCard(AdvisorReport report, AdvisorRecommendation rec)
    {
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var category = Pill(CategoryName(rec.Category), "Info"); DockPanel.SetDock(category, Dock.Right); category.VerticalAlignment = VerticalAlignment.Top;
        head.Children.Add(category);
        var title = Label(L(rec.Name), 14); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(title);
        panel.Children.Add(head);

        var status = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var (evText, evTone) = EvidenceLabel(rec.Evidence);
        var ev = Pill(evText, evTone); ev.Margin = new Thickness(0, 0, 6, 4); status.Children.Add(ev);
        var compliance = rec.Compliance switch
        {
            Compliance.Ok => (T("OK", "OK"), "Success"),
            Compliance.Attention => (T("AJUSTAR", "NEEDS CHANGE"), "Danger"),
            Compliance.Info => (T("INFORMATIVO", "INFO"), "Info"),
            _ => ((string?)null, ""),
        };
        if (compliance.Item1 is { } ct) { var cp = Pill(ct, compliance.Item2); cp.Margin = new Thickness(0, 0, 6, 4); status.Children.Add(cp); }
        panel.Children.Add(status);

        var description = Label(L(rec.Description), 12, true); description.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(description);

        void Field(string label, string value, bool systemData = false)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var l = new TextBlock { Text = label, Width = 120, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
            l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            DockPanel.SetDock(l, Dock.Left); row.Children.Add(l);
            var v = new TextBlock { Text = value, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = systemData ? Translator.SystemDataTag : null };
            v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            row.Children.Add(v);
            panel.Children.Add(row);
        }
        Field(T("RECOMENDADO", "RECOMMENDED"), L(rec.RecommendedValue));
        if (rec.CurrentValue is { } current) Field(T("ATUAL", "CURRENT"), L(current));
        if (rec.BiosValue is { } bv && rec.Evidence == Evidence.Detected) Field(T("NA BIOS", "IN BIOS"), $"{bv.Question}: {bv.Value}", systemData: true);
        Field(T("BENEFÍCIO", "BENEFIT"), L(rec.ExpectedBenefit));

        var levels = new WrapPanel { Margin = new Thickness(0, 6, 0, 12) };
        void LevelPill(string name, string value, string tone) { var p = Pill($"{name}: {value}", tone); p.Margin = new Thickness(0, 0, 6, 4); levels.Children.Add(p); }
        LevelPill(T("GANHO", "GAIN"), LevelName(rec.Gain), rec.Gain == Level.High ? "Success" : rec.Gain == Level.Medium ? "Accent" : "Info");
        LevelPill(T("RISCO", "RISK"), LevelName(rec.Risk), rec.Risk == Level.High ? "Danger" : rec.Risk == Level.Medium ? "Warning" : "Success");
        LevelPill(T("IMPACTO TÉRMICO", "THERMAL IMPACT"), ThermalName(rec.Thermal), rec.Thermal is ThermalImpact.High ? "Danger" : rec.Thermal is ThermalImpact.Medium ? "Warning" : "Info");
        panel.Children.Add(levels);

        var actions = new WrapPanel();
        if (rec.SettingId != null)
        {
            var guide = IconButton(Glyphs.ChevronRight, T("VER COMO CONFIGURAR", "HOW TO CONFIGURE"), primary: rec.Compliance == Compliance.Attention);
            guide.Margin = new Thickness(0, 0, 8, 0);
            guide.Click += (_, _) => ShowAdvisorGuide(report, rec);
            actions.Children.Add(guide);
        }
        if (rec.Evidence is Evidence.NeedsBiosCheck or Evidence.UserConfirmed && rec.Compliance is Compliance.Unknown or Compliance.Ok)
        {
            var confirmed = rec.Evidence == Evidence.UserConfirmed;
            var mark = IconButton(confirmed ? Glyphs.Undo : Glyphs.Check, confirmed ? T("Desmarcar conferido", "Unmark as checked") : T("Já conferi na BIOS", "I checked it in the BIOS"));
            mark.ToolTip = T("Marque só depois de ver na BIOS que a opção está no valor recomendado. Vale para esta versão de BIOS.",
                "Only mark it after seeing in the BIOS that the option has the recommended value. Valid for this BIOS version.");
            mark.Click += (_, _) => { Advisor.SetConfirmed(report.Profile, rec.Id, !confirmed); KeepScroll(ShowBiosAdvisor); };
            actions.Children.Add(mark);
        }
        panel.Children.Add(actions);

        var card = Surface(panel);
        if (rec.Compliance == Compliance.Attention) card.SetResourceReference(Border.BorderBrushProperty, "DangerSoftBrush");
        return card;
    }
}
