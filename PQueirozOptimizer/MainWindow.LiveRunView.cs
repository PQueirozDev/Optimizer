using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Marcação que o script emite no modo do app: o plano de etapas de uma atividade ou o início de uma etapa.</summary>
public sealed record LiveMarker(string Kind, string Activity, IReadOnlyList<string> Steps);

/// <summary>
/// Tela de execução: progresso real por etapa (feitas, em andamento e na fila) e, em outra aba,
/// o registro detalhado do que o script escreveu.
/// </summary>
public partial class MainWindow
{
    private enum LiveStepState { Pending, Running, Done, Failed, NotRun }
    /// <summary>Como uma execução terminou (o Smart Optimize para a sequência se o usuário cancelar).</summary>
    private enum LiveOutcome { NotStarted, Completed, CompletedWithFailures, Cancelled }

    private sealed class LiveStep
    {
        public required string Name { get; init; }
        /// <summary>Etapa de preparação (ponto de restauração): não entra na porcentagem.</summary>
        public required bool Prep { get; init; }
        public required Border Row { get; init; }
        public required Border Marker { get; init; }
        public required TextBlock Title { get; init; }
        public required TextBlock Status { get; init; }
        public LiveStepState State { get; set; }
    }

    private const string PrepActivity = "Ponto de restauracao";

    /// <summary>Reconhece "[PLANO] {json}" e "[ETAPA] nome". Retorna null para as demais linhas.</summary>
    public static LiveMarker? ParseLiveMarker(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("[ETAPA] ", StringComparison.Ordinal))
            return new LiveMarker("step", "", new[] { text[8..].Trim() });
        if (!text.StartsWith("[PLANO] ", StringComparison.Ordinal)) return null;
        try
        {
            using var json = JsonDocument.Parse(text[8..]);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var activity = root.TryGetProperty("Atividade", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : "";
            var steps = !root.TryGetProperty("Etapas", out var e) ? new List<string>()
                : e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(s => s.ToString()).Where(s => s.Length > 0).ToList()
                : e.ValueKind == JsonValueKind.String ? new List<string> { e.GetString()! } : new List<string>();
            return new LiveMarker("plan", activity, steps);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>Nome de exibição das atividades que o script anuncia no plano.</summary>
    private static string ActivityTitle(string activity) => activity switch
    {
        PrepActivity => "Ponto de restauração",
        "Otimizacao Padrao" => "Otimização padrão",
        "Otimizacao Avancada" => "Otimização avançada",
        "Otimizacao Inteligente" => "Otimização inteligente",
        "Limpeza de temporarios" => "Limpeza de temporários",
        _ => activity,
    };

    private static TextBlock LiveText(string text, double size, string brush, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (bold) block.FontWeight = FontWeights.SemiBold;
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    /// <summary>Ícone que gira enquanto algo está em andamento (parado se as animações estiverem desligadas).</summary>
    private static TextBlock Spinner(double size, string brush)
    {
        var icon = GlyphIcon(Glyphs.Refresh, size, brush);
        icon.RenderTransformOrigin = new Point(0.5, 0.5);
        var rotate = new RotateTransform();
        icon.RenderTransform = rotate;
        if (AppearanceService.AnimationsEnabled)
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
        return icon;
    }

    /// <summary>Para o giro de um ícone que vai sair da tela (a animação infinita continuaria rodando).</summary>
    private static void StopSpinner(UIElement? icon)
    {
        if (icon is FrameworkElement { RenderTransform: RotateTransform rotate }) rotate.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>Executa uma operação do script mostrando as etapas ao vivo e o registro completo.</summary>
    private async Task<LiveOutcome> RunLiveAsync(string operation, IReadOnlyList<string>? selectedSteps = null)
    {
        if (_operationRunning) { OperationStatus.Text = "Aguarde a operação em andamento."; return LiveOutcome.NotStarted; }
        var (title, icon, description) = OperationInfo(operation);
        PageTitle.Text = title;
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();

        // ---------- Cartão principal: o que está acontecendo agora e quanto falta ----------
        var head = new DockPanel();
        var status = Pill("Em execução", "Accent");
        var elapsed = LiveText("00:00", 12, "MutedBrush"); elapsed.Margin = new Thickness(0, 6, 0, 0); elapsed.HorizontalAlignment = HorizontalAlignment.Right;
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        status.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(status); right.Children.Add(elapsed);
        // SFC/DISM podem levar muito tempo ou travar: a operação pode ser interrompida
        var cancel = IconButton(Glyphs.Cancel, "Cancelar");
        cancel.SetResourceReference(StyleProperty, "GhostButton");
        cancel.Margin = new Thickness(0, 6, 0, 0); cancel.Padding = new Thickness(10, 4, 10, 4); cancel.HorizontalAlignment = HorizontalAlignment.Right;
        cancel.Click += (_, _) => { if (ConfirmCancelOperation()) cancel.IsEnabled = false; };
        right.Children.Add(cancel);
        DockPanel.SetDock(right, Dock.Right); head.Children.Add(right);
        var chip = OutlineChip(icon, 52, primary: true); DockPanel.SetDock(chip, Dock.Left); head.Children.Add(chip);
        var headText = new StackPanel { Margin = new Thickness(18, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        var t = LiveText(title, 18, "TextBrush", bold: true); t.Margin = new Thickness(0, 0, 0, 4);
        headText.Children.Add(t);
        headText.Children.Add(LiveText(selectedSteps is null ? description
            : operation == "reverter" ? "Desfazendo só este ajuste: " + string.Join(", ", selectedSteps) + ". O restante do backup continua guardado."
            : $"{selectedSteps.Count} ajustes selecionados. Um ponto de restauração é criado antes de qualquer alteração.", 12.5, "MutedBrush"));
        head.Children.Add(headText);

        var progressGrid = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var percent = LiveText("", 30, "TextBrush", bold: true);
        percent.Margin = new Thickness(0, 0, 18, 0); percent.Visibility = Visibility.Collapsed;
        progressGrid.Children.Add(percent);
        var nowPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nowLine = new DockPanel();
        var heroIcon = new Border { Width = 18, Margin = new Thickness(0, 0, 8, 0), Child = Spinner(14, "AccentBrush") };
        DockPanel.SetDock(heroIcon, Dock.Left); nowLine.Children.Add(heroIcon);
        var current = LiveText("Iniciando…", 14, "TextBrush", bold: true);
        current.TextTrimming = TextTrimming.CharacterEllipsis; current.TextWrapping = TextWrapping.NoWrap;
        nowLine.Children.Add(current);
        nowPanel.Children.Add(nowLine);
        var summary = LiveText("Preparando a execução", 12.5, "MutedBrush"); summary.Margin = new Thickness(26, 3, 0, 0);
        nowPanel.Children.Add(summary);
        Grid.SetColumn(nowPanel, 1); progressGrid.Children.Add(nowPanel);
        var progress = new ProgressBar { IsIndeterminate = true, Height = 8, Maximum = 100, Margin = new Thickness(0, 14, 0, 0) };

        // Contadores do resultado
        var okCount = 0; var failCount = 0; var warnCount = 0;
        var counters = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var okPill = Pill("Concluídos: 0", "Success"); var failPill = Pill("Falhas: 0", "Danger"); var warnPill = Pill("Avisos: 0", "Warning");
        foreach (var p in new[] { okPill, warnPill, failPill }) { p.Margin = new Thickness(0, 0, 8, 0); counters.Children.Add(p); }
        void UpdateCounters()
        {
            ((TextBlock)okPill.Child).Text = $"Concluídos: {okCount}";
            ((TextBlock)failPill.Child).Text = $"Falhas: {failCount}";
            ((TextBlock)warnPill.Child).Text = $"Avisos: {warnCount}";
        }

        var headPanel = new StackPanel();
        headPanel.Children.Add(head); headPanel.Children.Add(progressGrid); headPanel.Children.Add(progress); headPanel.Children.Add(counters);
        var headCard = Surface(headPanel); headCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        root.Children.Add(headCard);

        // ---------- Aba "Etapas": feito, em andamento e o que falta ----------
        var steps = new List<LiveStep>();
        var stepsList = new StackPanel();
        var stepsScroll = new ScrollViewer { Content = stepsList, MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var emptySteps = LiveText("As etapas aparecem aqui assim que a execução começa.", 12.5, "MutedBrush");
        emptySteps.Margin = new Thickness(0, 4, 0, 4);
        var remaining = Pill("Aguardando", "Accent");
        var stepsHead = new DockPanel();
        remaining.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(remaining, Dock.Right); stepsHead.Children.Add(remaining);
        stepsHead.Children.Add(SectionHeader("O que está sendo feito", "Concluídas, em andamento e o que ainda falta, na ordem em que rodam."));
        var stepsPanel = new StackPanel();
        stepsPanel.Children.Add(stepsHead); stepsPanel.Children.Add(emptySteps); stepsPanel.Children.Add(stepsScroll);
        var stepsCard = Surface(stepsPanel);

        // ---------- Aba "Registro detalhado": tudo o que o script escreveu ----------
        var lines = new ObservableCollection<OutputLine>();
        var output = new ListBox { ItemsSource = lines, Height = 380, ItemTemplate = OutputLineTemplate() };
        VirtualizingPanel.SetIsVirtualizing(output, true);
        var logPanel = new StackPanel();
        var logHead = new DockPanel();
        var copy = IconButton(Glyphs.Document, "Copiar resultado"); copy.Margin = new Thickness(0); copy.VerticalAlignment = VerticalAlignment.Top;
        copy.SetResourceReference(StyleProperty, "GhostButton");
        copy.Click += (_, _) => CopyText(string.Join(Environment.NewLine, lines.Select(l => l.Text)), "Resultado copiado para a área de transferência.");
        DockPanel.SetDock(copy, Dock.Right); logHead.Children.Add(copy);
        logHead.Children.Add(SectionHeader("Registro detalhado", "Cada mensagem da execução, com avisos e falhas destacados."));
        logPanel.Children.Add(logHead);
        logPanel.Children.Add(output);
        var logCard = Surface(logPanel);

        // Rola só a lista interna: ScrollIntoView/BringIntoView também rolariam a página inteira
        void ScrollLogToEnd(bool force = false)
        {
            if (logCard.Visibility != Visibility.Visible || FindVisualChild<ScrollViewer>(output) is not { } viewer) return;
            if (force || viewer.VerticalOffset >= viewer.ScrollableHeight - 40) viewer.ScrollToEnd();
        }
        void ScrollToStep(LiveStep step) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!stepsList.IsAncestorOf(step.Row) || stepsCard.Visibility != Visibility.Visible || stepsScroll.ViewportHeight <= 0) return;
            var y = step.Row.TranslatePoint(new Point(0, 0), stepsList).Y;
            stepsScroll.ScrollToVerticalOffset(Math.Max(0, y - stepsScroll.ViewportHeight / 3));
        });

        var tabHost = new Border();
        void SelectTab(int index)
        {
            tabHost.Child = Tabs(new[] { (Glyphs.Check, "Etapas"), (Glyphs.Document, "Registro detalhado") }, index, SelectTab);
            stepsCard.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            logCard.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (index == 1) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ScrollLogToEnd(force: true));
        }
        SelectTab(0);
        root.Children.Add(tabHost);
        root.Children.Add(stepsCard);
        root.Children.Add(logCard);

        var actions = new WrapPanel();
        root.Children.Add(actions);
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);

        // ---------- Estado das etapas ----------
        void SetState(LiveStep step, LiveStepState state, string? label = null)
        {
            step.State = state;
            var (tone, glyph, text) = state switch
            {
                LiveStepState.Running => ("Accent", null, "Em andamento"),
                LiveStepState.Done => ("Success", Glyphs.Check, "Concluída"),
                LiveStepState.Failed => ("Danger", Glyphs.Cancel, "Falhou"),
                LiveStepState.NotRun => ("Muted", Glyphs.Stop, "Não executada"),
                _ => ("Muted", (string?)null, "Na fila"),
            };
            StopSpinner(step.Marker.Child);
            step.Marker.Child = state == LiveStepState.Running ? Spinner(11, "AccentBrush") : glyph is null ? null : GlyphIcon(glyph, 10, tone + "Brush");
            step.Marker.SetResourceReference(Border.BorderBrushProperty, state == LiveStepState.Pending ? "BorderBrush" : tone + "Brush");
            if (state is LiveStepState.Pending or LiveStepState.NotRun) step.Marker.Background = Brushes.Transparent;
            else step.Marker.SetResourceReference(Border.BackgroundProperty, tone + "SoftBrush");
            step.Title.SetResourceReference(TextBlock.ForegroundProperty, state is LiveStepState.Pending or LiveStepState.NotRun ? "MutedBrush" : "TextBrush");
            step.Title.FontWeight = state == LiveStepState.Running ? FontWeights.SemiBold : FontWeights.Normal;
            step.Status.Text = label ?? text;
            step.Status.SetResourceReference(TextBlock.ForegroundProperty, tone + "Brush");
            if (state == LiveStepState.Running) { step.Row.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush"); ScrollToStep(step); }
            else step.Row.Background = Brushes.Transparent;
        }

        LiveStep AddStep(string name, bool prep)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var marker = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(marker);
            var nameText = LiveText(name, 13, "MutedBrush"); nameText.Margin = new Thickness(12, 0, 12, 0);
            Grid.SetColumn(nameText, 1); grid.Children.Add(nameText);
            var state = LiveText("", 11.5, "MutedBrush", bold: true);
            Grid.SetColumn(state, 2); grid.Children.Add(state);
            var row = new Border { Child = grid, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 2) };
            stepsList.Children.Add(row);
            var step = new LiveStep { Name = name, Prep = prep, Row = row, Marker = marker, Title = nameText, Status = state };
            steps.Add(step);
            SetState(step, LiveStepState.Pending);
            emptySteps.Visibility = Visibility.Collapsed;
            return step;
        }

        void AddGroup(string activity)
        {
            var label = LiveText(ActivityTitle(activity), 12, "MutedBrush", bold: true);
            label.Margin = new Thickness(2, stepsList.Children.Count == 0 ? 2 : 14, 0, 6);
            stepsList.Children.Add(label);
        }

        var hasPlan = false;
        var lastLine = "";
        LiveStep? Running() => steps.FirstOrDefault(s => s.State == LiveStepState.Running);
        void UpdateProgress()
        {
            var main = steps.Where(s => !s.Prep).ToList();
            var finished = main.Count(s => s.State is LiveStepState.Done or LiveStepState.Failed);
            var left = main.Count - finished;
            var running = Running();
            current.Text = running?.Name ?? (lastLine.Length > 0 ? lastLine : current.Text);
            if (hasPlan && main.Count > 0)
            {
                var value = finished * 100.0 / main.Count;
                progress.IsIndeterminate = false;
                progress.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(AppearanceService.AnimationsEnabled ? 350 : 0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                percent.Visibility = Visibility.Visible;
                percent.Text = $"{(int)Math.Round(value)}%";
                summary.Text = $"{finished} de {main.Count} etapas · faltam {left}";
                ((TextBlock)remaining.Child).Text = left == 0 ? "Nada na fila" : left == 1 ? "Falta 1" : $"Faltam {left}";
            }
            else if (main.Count > 0)
            {
                summary.Text = finished == 1 ? "1 etapa concluída" : $"{finished} etapas concluídas";
                ((TextBlock)remaining.Child).Text = summary.Text;
            }
            else if (running is { Prep: true }) summary.Text = "Preparando: ponto de restauração antes de qualquer alteração";
        }

        void HandleMarker(LiveMarker marker)
        {
            if (marker.Kind == "plan")
            {
                var prep = marker.Activity == PrepActivity;
                if (!prep) hasPlan = true;
                if (marker.Steps.Count == 0) return;
                AddGroup(marker.Activity);
                foreach (var name in marker.Steps) AddStep(name, prep);
            }
            else
            {
                var name = marker.Steps[0];
                // Etapa anterior sem linha de resultado: terminou sem falha
                if (Running() is { } previous) SetState(previous, LiveStepState.Done);
                var step = steps.FirstOrDefault(s => s.State == LiveStepState.Pending && s.Name == name) ?? AddStep(name, prep: false);
                SetState(step, LiveStepState.Running);
                if (_operationCts is not { IsCancellationRequested: true }) OperationStatus.Text = name;
            }
            UpdateProgress();
        }

        void HandleLine(OutputLine line)
        {
            lines.Add(line);
            if (line.Tone == "Success") okCount++;
            else if (line.Tone == "Danger") failCount++;
            else if (line.Tone == "Warning") warnCount++;
            UpdateCounters();
            ScrollLogToEnd();
            lastLine = line.Text;
            // Resultado da etapa em andamento: "[OK] nome", "[!!] nome (falhou...)" ou "[FALHA] nome: erro"
            if (Running() is { } running && (line.Text == running.Name || line.Text.StartsWith(running.Name + " (", StringComparison.Ordinal) || line.Text.StartsWith(running.Name + ":", StringComparison.Ordinal)))
            {
                if (line.Tone == "Success") SetState(running, LiveStepState.Done);
                else if (line.Tone == "Danger") SetState(running, LiveStepState.Failed);
            }
            UpdateProgress();
        }

        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => elapsed.Text = clock.Elapsed.ToString(@"mm\:ss");
        timer.Start();

        var failed = false;
        var cancelled = false;
        await ExecuteTrackedAsync(title, async token =>
        {
            try
            {
                await _powershell.RunAsync(operation, selectedSteps, new Progress<string>(raw =>
                {
                    if (ParseLiveMarker(raw) is { } marker) { HandleMarker(marker); return; }
                    if (ClassifyLine(raw) is not { } line) return;
                    if (!cancelled) OperationStatus.Text = line.Text;
                    HandleLine(line);
                }), token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }
            catch
            {
                failed = true;
                throw;
            }
        });
        timer.Stop(); clock.Stop();
        cancel.Visibility = Visibility.Collapsed;
        elapsed.Text = clock.Elapsed.ToString(@"mm\:ss");

        // Fecha a lista: o que estava rodando e o que ficou na fila
        foreach (var step in steps.Where(s => s.State is LiveStepState.Running or LiveStepState.Pending).ToList())
        {
            if (step.State == LiveStepState.Running)
                SetState(step, cancelled ? LiveStepState.NotRun : failed ? LiveStepState.Failed : LiveStepState.Done, cancelled ? "Interrompida" : null);
            else SetState(step, LiveStepState.NotRun);
        }
        var stepFailures = steps.Count(s => s.State == LiveStepState.Failed);
        var tone = cancelled || failed || failCount > 0 || stepFailures > 0 ? "Warning" : "Success";
        var finalText = cancelled ? "Cancelado" : failed || failCount > 0 || stepFailures > 0 ? "Concluído com falhas" : "Concluído";
        UpdateProgress();
        var mainSteps = steps.Where(s => !s.Prep).ToList();
        var reached = mainSteps.Count == 0 ? 0 : mainSteps.Count(s => s.State is LiveStepState.Done or LiveStepState.Failed) * 100.0 / mainSteps.Count;
        progress.BeginAnimation(RangeBase.ValueProperty, null);
        progress.IsIndeterminate = false;
        progress.Value = !cancelled && !failed ? 100 : reached;
        if (percent.Visibility == Visibility.Visible) percent.Text = $"{(int)Math.Round(progress.Value)}%";
        current.Text = finalText;
        var done = steps.Count(s => s.State == LiveStepState.Done);
        summary.Text = steps.Count == 0 ? $"Tempo total: {elapsed.Text}"
            : $"{done} concluídas · {stepFailures} com falha · {steps.Count(s => s.State == LiveStepState.NotRun)} não executadas · {elapsed.Text}";
        ((TextBlock)remaining.Child).Text = finalText;
        remaining.SetResourceReference(Border.BackgroundProperty, tone + "SoftBrush");
        ((TextBlock)remaining.Child).SetResourceReference(TextBlock.ForegroundProperty, tone + "Brush");
        StopSpinner(heroIcon.Child);
        heroIcon.Child = GlyphIcon(tone == "Success" ? Glyphs.Check : Glyphs.Warning, 14, tone + "Brush");
        ((TextBlock)status.Child).Text = finalText;
        ((TextBlock)status.Child).SetResourceReference(TextBlock.ForegroundProperty, tone + "Brush");
        status.SetResourceReference(Border.BackgroundProperty, tone + "SoftBrush");
        chip.SetResourceReference(Border.BackgroundProperty, tone + "SoftBrush");
        // Sem etapas anunciadas (diagnósticos, verificação de corrupção), o registro é o resultado
        if (steps.Count == 0) SelectTab(1);

        if (operation is "padrao" or "gamer" or "gamerservicos" or "debloat" or "reverter")
        {
            var history = IconButton(Glyphs.History, "Ver backup e reversão"); history.Click += History_Click;
            actions.Children.Add(history);
        }
        var back = IconButton(Glyphs.ChevronRight, "Voltar às otimizações", primary: true);
        back.Click += (_, _) => NavigateTo("optimization");
        actions.Children.Add(back);
        // Verificação: o que o catálogo sabe ler é conferido no Windows, não só pela saída do script
        if (!cancelled && selectedSteps is { Count: > 0 } && operation is "padrao" or "gamer" or "debloat")
            await AppendLiveVerificationAsync(root, operation, selectedSteps);
        return cancelled ? LiveOutcome.Cancelled : failed || failCount > 0 || stepFailures > 0 ? LiveOutcome.CompletedWithFailures : LiveOutcome.Completed;
    }
}
