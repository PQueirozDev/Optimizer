using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.BiosAdvisor;
using PQueirozOptimizer.BiosAdvisor.Detector;
using PQueirozOptimizer.Engine;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// QRZ Smart Optimize: analisa o PC, lê o estado de cada ajuste, recomenda conforme o objetivo, deixa o usuário
/// revisar item a item e aplica pelo mesmo caminho das otimizações (backup, ponto de restauração, tela ao vivo).
/// Depois de aplicar, lê o estado de novo para confirmar o que mudou de verdade.
/// </summary>
public partial class MainWindow
{
    private OptimizationGoal _smartGoal = OptimizationGoal.CompetitiveGaming;
    private SmartAnalysis? _smartAnalysis;
    private (MachineSummary Machine, HardwareProfile Hardware, TweakContext Context)? _smartReading;
    private readonly HashSet<string> _smartSelection = new();
    private bool _smartShowAll;

    internal static readonly (OptimizationGoal Goal, string Glyph, string Title, string Detail)[] Goals =
    {
        (OptimizationGoal.CompetitiveGaming, Glyphs.Game, "Gaming competitivo", "Frametime estável, responsividade e menos processos em segundo plano."),
        (OptimizationGoal.Laptop, Glyphs.Laptop, "Notebook", "Temperatura, autonomia, consumo e ruído."),
        (OptimizationGoal.Development, Glyphs.Code, "Programação", "Preserva Docker, WSL, Hyper-V e ferramentas de desenvolvimento."),
        (OptimizationGoal.DailyUse, Glyphs.Home, "Uso diário", "Inicialização, limpeza segura, privacidade e estabilidade."),
        (OptimizationGoal.Custom, Glyphs.Settings, "Personalizado", "Nada vem marcado: você escolhe cada ajuste."),
    };

    internal static (string Text, string Tone) StateLabel(TweakState state) => state switch
    {
        TweakState.Recommended => ("Recomendado", "Accent"),
        TweakState.Applied => ("Já aplicado", "Success"),
        TweakState.NotApplicable => ("Não aplicável", "Info"),
        TweakState.Incompatible => ("Incompatível", "Danger"),
        TweakState.NeedsReview => ("Requer revisão", "Warning"),
        _ => ("Falha na leitura", "Danger"),
    };

    private static (string Text, string Tone) RiskLabel(StepRisk risk) => risk switch
    {
        StepRisk.High => ("Avançado", "Danger"),
        StepRisk.Moderate => ("Moderado", "Warning"),
        _ => ("Seguro", "Success"),
    };

    private void ShowSmartOptimize()
    {
        PageTitle.Text = "Smart Optimize";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();

        var intro = new StackPanel();
        var head = new DockPanel();
        var analyze = IconButton(Glyphs.Search, _smartAnalysis is null ? "Analisar este PC" : "Analisar de novo", primary: true);
        analyze.Click += async (_, _) => await RunSmartAnalysisAsync(rereadSystem: true);
        DockPanel.SetDock(analyze, Dock.Right); head.Children.Add(analyze);
        head.Children.Add(SectionHeader("Escolha o objetivo deste PC",
            "O Smart Optimize lê o hardware e o estado de cada ajuste, recomenda só o que se aplica e mostra o risco, a reversão e a evidência de cada um. Nada é aplicado sem a sua revisão."));
        intro.Children.Add(head);
        var goals = new UniformGrid { Columns = 5, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var g in Goals) goals.Children.Add(GoalCard(g.Goal, g.Glyph, g.Title, g.Detail));
        intro.Children.Add(goals);
        root.Children.Add(Surface(intro));

        var results = new StackPanel();
        root.Children.Add(results);
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        if (_smartAnalysis is { } analysis) RenderSmartResults(results, analysis);
        else
        {
            var empty = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
            empty.Children.Add(Label("Clique em \"Analisar este PC\" para detectar o hardware e conferir quais ajustes já estão aplicados. A análise só lê o sistema.", 13, true));
            results.Children.Add(Surface(empty));
        }
    }

    private Border GoalCard(OptimizationGoal goal, string glyph, string title, string detail)
    {
        var selected = goal == _smartGoal;
        var stack = new StackPanel();
        var chip = IconChip(glyph, selected ? "Accent" : "Text", 36); chip.HorizontalAlignment = HorizontalAlignment.Left; chip.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(chip);
        var t = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(t);
        var d = new TextBlock { Text = detail, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(d);
        var button = new Button { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14), Margin = new Thickness(0, 0, 10, 0), ToolTip = detail };
        button.SetResourceReference(StyleProperty, "GhostButton");
        AutomationProperties(button, title, selected);
        button.Click += async (_, _) =>
        {
            _smartGoal = goal;
            if (_smartAnalysis is null) { ShowSmartOptimize(); return; }
            await RunSmartAnalysisAsync(rereadSystem: false);
        };
        var border = new Border { Child = button, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(selected ? 2 : 1), Margin = new Thickness(0, 0, 10, 0) };
        border.SetResourceReference(Border.BorderBrushProperty, selected ? "AccentBrush" : "BorderSubtleBrush");
        border.SetResourceReference(Border.BackgroundProperty, selected ? "AccentSoftBrush" : "CardBgBrush");
        button.Margin = new Thickness(0);
        return border;
    }

    /// <summary>Nome e estado para leitores de tela (a seleção não depende só da cor da borda).</summary>
    private static void AutomationProperties(FrameworkElement element, string name, bool selected) =>
        System.Windows.Automation.AutomationProperties.SetName(element, selected ? name + " (selecionado)" : name);

    private async Task RunSmartAnalysisAsync(bool rereadSystem)
    {
        if (_operationRunning) { OperationStatus.Text = "Aguarde a operação em andamento."; return; }
        ShowSmartOptimize();
        var results = (StackPanel)((StackPanel)ContentHost.Children[0]).Children[1];
        results.Children.Clear();
        var loading = new StackPanel();
        var status = Label(rereadSystem || _smartReading is null ? "Detectando hardware e lendo o estado dos ajustes..." : "Recalculando para o novo objetivo...", 14);
        loading.Children.Add(status);
        loading.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 5 });
        foreach (var _ in Enumerable.Range(0, 3)) loading.Children.Add(SkeletonLine());
        results.Children.Add(Surface(loading));
        try
        {
            if (rereadSystem || _smartReading is null)
            {
                var snapshot = await ReadSnapshotAsync();
                var hardware = await Task.Run(() => new HardwareDetector().Detect());
                status.Text = "Conferindo apps da Loja e tarefas agendadas...";
                var state = await LiveSystemState.LoadAsync();
                var machine = MachineReader.Read(hardware, snapshot);
                _smartReading = (machine, hardware, MachineReader.Context(state, machine, hardware));
            }
            var reading = _smartReading.Value;
            var steps = SmartOptimizer.OperationOrder.ToDictionary(op => op, op => (IReadOnlyCollection<string>)_powershell.GetSteps(op).Select(s => s.Name).ToHashSet());
            var analysis = SmartOptimizer.Analyze(reading.Machine, reading.Context, _smartGoal, CurrentLicense, op => steps[op]);
            _smartAnalysis = analysis;
            _smartSelection.Clear();
            // Perfil aplicado: os ajustes dele (que ainda fazem sentido neste PC) no lugar da recomendação do objetivo
            var chosen = _smartProfileSelection;
            _smartProfileSelection = null;
            foreach (var item in analysis.Items.Where(i => chosen is null ? i.Preselected : chosen.Contains(i.Tweak.Id) && i.Selectable)) _smartSelection.Add(item.Tweak.Id);
            SmartHistory.Default.RecordAnalysis(analysis);
            _log.Write("INFO", $"Smart Optimize: análise concluída ({analysis.Count(TweakState.Recommended)} recomendados, {analysis.Count(TweakState.Applied)} já aplicados)");
            if (_currentPage != "smart") return;
            ShowSmartOptimize();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Write("ERROR", "Smart Optimize: " + ex.Message);
            results.Children.Clear();
            results.Children.Add(Card("Não foi possível concluir a análise", ex.Message, Glyphs.Warning, "WarningBrush"));
        }
    }

    /// <summary>Linha cinza animada enquanto a análise roda (skeleton loading).</summary>
    private static Border SkeletonLine()
    {
        var line = new Border { Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 12, 0, 0), Opacity = 0.6 };
        line.SetResourceReference(Border.BackgroundProperty, "BorderSubtleBrush");
        if (AppearanceService.AnimationsEnabled)
            line.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.35, 0.8, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        return line;
    }

    private void RenderSmartResults(StackPanel results, SmartAnalysis analysis)
    {
        var m = analysis.Machine;
        // ---------- Hardware detectado ----------
        var hw = new StackPanel();
        hw.Children.Add(SectionHeader("Este PC", $"Análise de {analysis.At:dd/MM/yyyy HH:mm}. Dados lidos do Windows (WMI e registro)."));
        var grid = new UniformGrid { Columns = 3 };
        grid.Children.Add(SystemCard("Processador", m.Cpu, Glyphs.Chip));
        grid.Children.Add(SystemCard("Placa de vídeo", m.Gpu + (m.GpuDriver.Length > 0 ? $" · driver {m.GpuDriver}" : ""), Glyphs.Video));
        grid.Children.Add(SystemCard("Memória", $"{m.RamGb:0.#} GB", Glyphs.Memory));
        grid.Children.Add(SystemCard("Armazenamento", Translator.Tr(m.Storage), Glyphs.Drive));
        grid.Children.Add(SystemCard("Placa-mãe", m.Board, Glyphs.Chip));
        grid.Children.Add(SystemCard("Windows", $"{m.Windows} · build {m.Build}", Glyphs.Monitor));
        grid.Children.Add(SystemCard("Plano de energia", Translator.Tr(m.PowerPlan), Glyphs.Power));
        grid.Children.Add(SystemCard("Tipo", Translator.Tr(m.IsLaptop ? "Notebook" : "Desktop"), m.IsLaptop ? Glyphs.Laptop : Glyphs.Monitor));
        grid.Children.Add(SystemCard("Virtualização", string.Join(", ", new[] { m.HypervisorPresent ? "Hipervisor ativo" : null, m.WslInstalled ? "WSL" : null, m.DockerInstalled ? "Docker" : null, m.HyperVInstalled ? "Hyper-V" : null }
            .OfType<string>().DefaultIfEmpty("Não detectada").Select(Translator.Tr)), Glyphs.Library));
        hw.Children.Add(grid);
        results.Children.Add(Surface(hw));

        // ---------- Resumo dos estados ----------
        var counts = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var state in Enum.GetValues<TweakState>())
        {
            var n = analysis.Count(state);
            if (n == 0) continue;
            var (text, tone) = StateLabel(state);
            var pill = Pill($"{text}: {n}", tone); pill.Margin = new Thickness(0, 0, 8, 8); counts.Children.Add(pill);
        }
        var summary = new StackPanel();
        summary.Children.Add(SectionHeader("Recomendações para " + Goals.First(g => g.Goal == analysis.Goal).Title.ToLowerInvariant(),
            "Os itens marcados são os recomendados para o objetivo. Ajustes já aplicados, sem efeito neste PC ou bloqueados pelo plano ficam ocultos; use o filtro para vê-los."));
        summary.Children.Add(counts);
        var showAll = new CheckBox { Content = "Mostrar também já aplicados, não aplicáveis e bloqueados", IsChecked = _smartShowAll, Margin = new Thickness(0, 4, 0, 0) };
        showAll.Checked += (_, _) => { _smartShowAll = true; KeepScroll(ShowSmartOptimize); };
        showAll.Unchecked += (_, _) => { _smartShowAll = false; KeepScroll(ShowSmartOptimize); };
        summary.Children.Add(showAll);
        results.Children.Add(Surface(summary));

        foreach (var notice in analysis.Notices) results.Children.Add(NoticeCard(notice.Title, notice.Detail, "Info"));
        var conflictHost = new StackPanel();
        results.Children.Add(conflictHost);

        // ---------- Ajustes por categoria ----------
        var visible = analysis.Items.Where(i => _smartShowAll || i.Selectable).ToList();
        var selectedLabel = Label("", 13, true); selectedLabel.Margin = new Thickness(0); selectedLabel.VerticalAlignment = VerticalAlignment.Center;
        var apply = IconButton(Glyphs.Lightning, "Revisar e aplicar", primary: true);
        void Refresh()
        {
            var conflicts = SmartOptimizer.SelectionConflicts(_smartSelection, m);
            conflictHost.Children.Clear();
            foreach (var c in conflicts) conflictHost.Children.Add(NoticeCard(c.Title, c.Detail, c.Blocking ? "Danger" : "Warning"));
            var count = _smartSelection.Count;
            apply.IsEnabled = count > 0 && !conflicts.Any(c => c.Blocking);
            selectedLabel.Text = count == 0 ? "Nenhum ajuste selecionado" : conflicts.Any(c => c.Blocking) ? $"{count} selecionados · resolva o conflito para aplicar" : $"{count} ajustes selecionados";
        }
        if (visible.Count == 0)
        {
            var done = new StackPanel();
            done.Children.Add(Label("Nada a recomendar para este objetivo: os ajustes já estão aplicados ou não se aplicam a este PC.", 14));
            results.Children.Add(Surface(done));
        }
        foreach (var group in visible.GroupBy(i => i.Tweak.Category))
        {
            var panel = new StackPanel();
            panel.Children.Add(SectionHeader(group.Key));
            foreach (var item in group) panel.Children.Add(SmartRow(item, Refresh));
            results.Children.Add(Surface(panel));
        }
        apply.Click += async (_, _) => await ApplySmartAsync(analysis);
        var lab = IconButton(Glyphs.Speed, "Optimization Lab");
        lab.ToolTip = "Medir o efeito de um ajuste por vez, com gravações antes e depois.";
        lab.Click += (_, _) => NavigateTo("perflab");
        results.Children.Add(ActionBar(selectedLabel, lab, apply));
        Refresh();
    }

    private Border SystemCard(string title, string value, string glyph)
    {
        var card = Card(title, value, glyph);
        if (card.Child is Grid g && g.Children.OfType<StackPanel>().FirstOrDefault()?.Children[1] is TextBlock v) v.Tag = Translator.SystemDataTag;
        return card;
    }

    private Border NoticeCard(string title, string detail, string tone)
    {
        var dock = new DockPanel();
        var chip = IconChip(tone == "Info" ? Glyphs.Info : Glyphs.Warning, tone, 34); chip.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(chip, Dock.Left); dock.Children.Add(chip);
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        var t = Label(title, 14); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 0, 0, 2);
        text.Children.Add(t);
        var d = Label(detail, 12.5, true); d.Margin = new Thickness(0);
        text.Children.Add(d);
        dock.Children.Add(text);
        var card = Surface(dock);
        card.SetResourceReference(Border.BorderBrushProperty, tone + "SoftBrush");
        return card;
    }

    private Border SmartRow(TweakAssessment item, Action changed)
    {
        var t = item.Tweak;
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var check = new CheckBox { IsChecked = _smartSelection.Contains(t.Id), IsEnabled = item.Selectable, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
        System.Windows.Automation.AutomationProperties.SetName(check, t.Name);
        check.Checked += (_, _) => { _smartSelection.Add(t.Id); changed(); };
        check.Unchecked += (_, _) => { _smartSelection.Remove(t.Id); changed(); };
        DockPanel.SetDock(check, Dock.Left); row.Children.Add(check);

        var details = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
        var toggle = new Button { Content = GlyphIcon(Glyphs.ChevronRight, 11, "MutedBrush"), Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Top, ToolTip = "Detalhes" };
        toggle.SetResourceReference(StyleProperty, "GhostButton");
        System.Windows.Automation.AutomationProperties.SetName(toggle, "Detalhes de " + t.Name);
        toggle.Click += (_, _) =>
        {
            var open = details.Visibility != Visibility.Visible;
            details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)toggle.Content).RenderTransform = open ? new System.Windows.Media.RotateTransform(90, 5, 6) : null;
        };
        DockPanel.SetDock(toggle, Dock.Right); row.Children.Add(toggle);

        var body = new StackPanel();
        var title = Label(t.Name, 14); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 0, 2);
        body.Children.Add(title);
        var desc = Label(t.Description, 12.5, true); desc.Margin = new Thickness(0, 0, 0, 6);
        body.Children.Add(desc);
        var pills = new WrapPanel();
        void AddPill(string text, string tone) { var p = Pill(text, tone); p.Margin = new Thickness(0, 0, 6, 4); pills.Children.Add(p); }
        var (stateText, stateTone) = StateLabel(item.State);
        AddPill(stateText, stateTone);
        var (riskText, riskTone) = RiskLabel(t.Risk);
        AddPill(riskText, riskTone);
        if (t.RequiresReboot) AddPill("Requer reiniciar", "Info");
        if (t.OneOff) AddPill("Ação pontual", "Info");
        if (t.Revert.StartsWith("Não reversível", StringComparison.Ordinal) || t.Revert.StartsWith("Reinstale", StringComparison.Ordinal)) AddPill("Não reversível", "Warning");
        if (item.Locked) AddPill("Plano " + LicensePlans.TierName(item.RequiredTier), "Warning");
        body.Children.Add(pills);
        if (item.GoalNote is { } note) { var n = Label("Não recomendado para este objetivo: " + note, 12.5, true); n.Margin = new Thickness(0, 2, 0, 0); body.Children.Add(n); }
        var now = Label("Leitura atual: " + item.Detail, 12, true); now.Margin = new Thickness(0, 2, 0, 0);
        body.Children.Add(now);

        // Texto simples "Rótulo: valor" (o tradutor traduz as duas partes; Runs não seriam traduzidos)
        void Detail(string label, string value)
        {
            var line = new TextBlock { Text = label + ": " + value, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 6) };
            line.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            details.Children.Add(line);
        }
        Detail("Efeitos colaterais", t.SideEffects);
        Detail("Como é verificado", t.Verification);
        Detail("Como reverter", t.Revert);
        Detail("Evidência do benefício", t.Evidence);
        Detail("Identificador", t.Id);
        body.Children.Add(details);
        row.Children.Add(body);

        var border = new Border { Child = row, Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8), Opacity = item.Selectable ? 1 : 0.7 };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return border;
    }

    private async Task ApplySmartAsync(SmartAnalysis analysis)
    {
        var selected = _smartSelection.ToList();
        var blocking = SmartOptimizer.SelectionConflicts(selected, analysis.Machine).Where(c => c.Blocking).ToList();
        if (blocking.Count > 0) { Msg(string.Join("\n\n", blocking.Select(b => b.Title + ": " + b.Detail)), "Conflito", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var plan = SmartOptimizer.Plan(selected);
        // Confirmação extra para arriscados e não reversíveis (mesma regra da tela de revisão)
        var scriptSteps = plan.SelectMany(p => _powershell.GetSteps(p.Operation).Where(s => p.Steps.Contains(s.Name))).ToList();
        var reboot = selected.Select(TweakCatalog.Find).OfType<TweakDefinition>().Where(t => t.RequiresReboot).Select(t => t.Name).ToList();
        var message = $"Aplicar {selected.Count} ajustes em {plan.Count} etapa(s)? Um ponto de restauração é criado antes, e cada alteração fica no backup.";
        if (reboot.Count > 0) message += "\n\nPrecisam reiniciar para valer:\n• " + string.Join("\n• ", reboot);
        if (ConfirmationSummary(scriptSteps) is { } extra) message += "\n\n" + extra;
        else message += "\n\nContinuar?";
        if (Msg(message, "Smart Optimize", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        var outcomes = new List<(string Operation, string Outcome)>();
        foreach (var (operation, steps) in plan)
        {
            var outcome = await RunLiveAsync(operation, steps);
            outcomes.Add((operation, outcome.ToString()));
            if (outcome is LiveOutcome.Cancelled or LiveOutcome.NotStarted) break;
        }
        // Verificação: o estado é lido de novo; um ajuste só conta como aplicado se o Windows confirmar
        var reading = _smartReading!.Value;
        var after = MachineReader.Context(await LiveSystemState.LoadAsync(), reading.Machine, reading.Hardware);
        var ran = plan.Where(p => outcomes.Any(o => o.Operation == p.Operation && o.Outcome is nameof(LiveOutcome.Completed) or nameof(LiveOutcome.CompletedWithFailures)))
            .SelectMany(p => TweakCatalog.All.Where(t => t.Operation == p.Operation && p.Steps.Contains(t.Step)).Select(t => t.Id)).ToList();
        var clean = outcomes.Where(o => o.Outcome == nameof(LiveOutcome.Completed)).Select(o => o.Operation).ToHashSet();
        var verification = SmartOptimizer.Verify(ran, after, clean);
        SmartHistory.Default.RecordRun(new SmartHistory.Run(DateTime.Now, analysis.Goal, selected.ToArray(),
            verification.Where(v => v.Verified).Select(v => v.Tweak.Id).ToArray(), verification.Where(v => !v.Verified).Select(v => v.Tweak.Id).ToArray()));
        _log.Write(verification.All(v => v.Verified) ? "SUCCESS" : "WARN", $"Smart Optimize: {verification.Count(v => v.Verified)} de {verification.Count} ajustes confirmados pelo Windows");
        _smartAnalysis = null;
        ShowSmartVerification(verification, selected.Count - ran.Count);
    }

    private void ShowSmartVerification(List<(TweakDefinition Tweak, bool Verified, string Detail)> results, int notRun)
    {
        PageTitle.Text = "Resultado verificado";
        var root = new StackPanel();
        var head = new StackPanel();
        var ok = results.Count(r => r.Verified);
        head.Children.Add(SectionHeader($"{ok} de {results.Count} ajustes confirmados",
            "Cada ajuste foi conferido lendo o estado do Windows depois da execução; a saída do script não basta." + (notRun > 0 ? $" {notRun} ajuste(s) não rodaram porque a execução foi cancelada ou não começou." : "")));
        if (results.Any(r => r.Verified && r.Tweak.RequiresReboot)) head.Children.Add(NoticeCard("Reinicie o PC", "Alguns ajustes só passam a valer depois de reiniciar o Windows.", "Info"));
        root.Children.Add(Surface(head));
        var list = new StackPanel();
        foreach (var (tweak, verified, detail) in results)
        {
            var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var chip = IconChip(verified ? Glyphs.Check : Glyphs.Warning, verified ? "Success" : "Warning", 30); DockPanel.SetDock(chip, Dock.Left); dock.Children.Add(chip);
            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            var t = Label((verified ? "Confirmado · " : "Não confirmado · ") + tweak.Name, 13); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0);
            text.Children.Add(t);
            var d = Label(detail, 12, true); d.Margin = new Thickness(0, 2, 0, 0); text.Children.Add(d);
            dock.Children.Add(text);
            list.Children.Add(dock);
        }
        if (results.Count > 0) root.Children.Add(Surface(list));
        var history = IconButton(Glyphs.History, "Ver backup e reversão"); history.Click += History_Click;
        var again = IconButton(Glyphs.Refresh, "Analisar de novo", primary: true); again.Click += async (_, _) => { NavigateTo("smart"); await RunSmartAnalysisAsync(rereadSystem: true); };
        root.Children.Add(ActionBar(Label("O backup de cada item está em Atividade e reversão.", 12.5, true), history, again));
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
    }
}
