using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PQueirozOptimizer.Services;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer;

public partial class MainWindow
{
    private readonly ObservableCollection<string> _activity = new();
    private static string AppVersion => "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
    private static readonly string SnapshotDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "Snapshots");
    private readonly UpdateService _updates = new();
    private StackPanel? _updateSlot;

    private void History_Click(object sender, RoutedEventArgs e) => NavigateTo("history");

    private async Task ExecuteTrackedAsync(string title, Func<Task> action)
    {
        if (_operationRunning) { OperationStatus.Text = "Aguarde a operação em andamento."; return; }
        _operationRunning = true;
        OperationProgress.Visibility = Visibility.Visible;
        OperationStatus.Text = title;
        TitleOptChip.Text = "Em execução";
        try
        {
            await action();
            _snapshot = null;
            OperationStatus.Text = title + " — concluído. Confira os resultados na atividade.";
            TitleOptChip.Text = "Concluído";
        }
        catch (Exception ex)
        {
            _log.Write("ERROR", ex.Message);
            OperationStatus.Text = ex.Message;
            TitleOptChip.Text = "Verificar resultado";
        }
        finally { _operationRunning = false; OperationProgress.Visibility = Visibility.Collapsed; }
    }

    /// <summary>Linha selecionável (checkbox) com título, descrição e etiqueta de impacto.</summary>
    private Border ChoiceRow(CheckBox check, string title, string detail, string pill, string tone)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        var t = new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        text.Children.Add(t);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var d = new TextBlock { Text = detail, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
            d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            text.Children.Add(d);
        }
        grid.Children.Add(text);
        var badge = Pill(pill, tone); badge.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(badge, 1); grid.Children.Add(badge);
        check.Content = grid;
        check.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        check.Margin = new Thickness(0);

        var row = new Border { Child = check, Padding = new Thickness(18, 14, 18, 14), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
        row.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        row.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        void Highlight() => row.SetResourceReference(Border.BorderBrushProperty, check.IsChecked == true ? "AccentBrush" : "BorderSubtleBrush");
        check.Checked += (_, _) => Highlight();
        check.Unchecked += (_, _) => Highlight();
        // A linha inteira funciona como área de clique
        row.MouseLeftButtonUp += (_, e) => { if (check.IsEnabled && e.OriginalSource == row) check.IsChecked = check.IsChecked != true; };
        Highlight();
        return row;
    }

    /// <summary>Barra fixa no rodapé da lista com resumo à esquerda e ações à direita.</summary>
    private Border ActionBar(UIElement summary, params Button[] buttons)
    {
        var dock = new DockPanel { LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var b in buttons) { b.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(b); }
        DockPanel.SetDock(actions, Dock.Right);
        dock.Children.Add(actions);
        dock.Children.Add(summary);
        var bar = new Border { Child = dock, Padding = new Thickness(18, 14, 18, 14), CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 8, 0, 8) };
        bar.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        return bar;
    }

    private async Task PrepareOperationAsync(string operation)
    {
        if (_operationRunning) { OperationStatus.Text = "Aguarde a operação em andamento."; return; }
        try
        {
            if (operation == "quickclean") { await ShowCleanPreviewAsync(); return; }
            if (operation == "reverter") { NavigateTo("history"); return; }
            var steps = _powershell.GetSteps(operation);
            if (steps.Count == 0)
            {
                await RunLiveAsync(operation);
                return;
            }
            PageTitle.Text = "Revisar ajustes";
            PageBadge.Visibility = Visibility.Visible;
            PageBadgeText.Text = $"{steps.Count} ajustes disponíveis";
            var root = new StackPanel();

            var intro = new StackPanel();
            intro.Children.Add(SectionHeader("Escolha exatamente o que deseja aplicar",
                "Um ponto de restauração é criado antes dos ajustes; se ele falhar, nada é alterado. Alterações de energia, registro e serviços têm backup e podem ser revertidas em “Atividade e reversão”."));
            var notice = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            notice.Children.Add(GlyphIcon(Glyphs.Warning, 13, "WarningBrush"));
            var noticeText = Label("Remoções de apps e arquivos não são desfeitas pelo backup. Alguns ajustes só valem depois de reiniciar o Windows.", 12, true);
            noticeText.Margin = new Thickness(8, 0, 0, 0);
            notice.Children.Add(noticeText);
            intro.Children.Add(notice);
            root.Children.Add(Surface(intro));

            var checks = new List<CheckBox>();
            foreach (var step in steps)
            {
                var irreversible = step.Contains("removid", StringComparison.OrdinalIgnoreCase) || step.Contains("Limp", StringComparison.OrdinalIgnoreCase);
                var check = new CheckBox { Tag = step, IsChecked = false };
                checks.Add(check);
                root.Children.Add(ChoiceRow(check, step, "", irreversible ? "Não reversível" : "Com backup", irreversible ? "Warning" : "Success"));
            }

            var selectedLabel = Label("Nenhum ajuste selecionado", 13, true);
            selectedLabel.Margin = new Thickness(0); selectedLabel.VerticalAlignment = VerticalAlignment.Center;
            var selectAll = IconButton(Glyphs.Check, "Selecionar todos");
            var apply = IconButton(Glyphs.Lightning, "Aplicar selecionados", primary: true);
            apply.IsEnabled = false;
            void Refresh()
            {
                var count = checks.Count(c => c.IsChecked == true);
                apply.IsEnabled = count > 0;
                selectedLabel.Text = count == 0 ? "Nenhum ajuste selecionado" : $"{count} de {checks.Count} ajustes selecionados";
            }
            foreach (var check in checks) { check.Checked += (_, _) => Refresh(); check.Unchecked += (_, _) => Refresh(); }
            selectAll.Click += (_, _) => { var all = checks.All(c => c.IsChecked == true); foreach (var c in checks) c.IsChecked = !all; };
            apply.Click += async (_, _) =>
            {
                var selected = checks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray();
                await RunLiveAsync(operation, selected);
            };
            root.Children.Add(ActionBar(selectedLabel, selectAll, apply));
            ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        }
        catch (Exception ex) { _log.Write("ERROR", ex.Message); OperationStatus.Text = ex.Message; }
    }

    private async Task ShowCleanPreviewAsync()
    {
        PageTitle.Text = "Limpeza rápida";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        var loading = new StackPanel();
        loading.Children.Add(SectionHeader("Analisando arquivos temporários...", "Isso leva poucos segundos. Nada é apagado nesta etapa."));
        loading.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 5 });
        root.Children.Add(Surface(loading));
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        await ExecuteTrackedAsync("Analisar temporários", async () =>
        {
            var categories = await _cleaner.AnalyzeAsync();
            root.Children.Clear();

            var totalBytes = categories.Sum(c => c.Bytes);
            var hero = new Grid();
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hero.Children.Add(IconChip(Glyphs.Broom, "Accent", 52));
            var heroText = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var big = new TextBlock { Text = $"{totalBytes / 1048576d:N1} MB podem ser liberados", FontSize = 22, FontWeight = FontWeights.SemiBold };
            big.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            heroText.Children.Add(big);
            heroText.Children.Add(Label("Arquivos alterados nas últimas 48 horas, links e itens sem acesso são preservados. A exclusão é permanente.", 12, true));
            Grid.SetColumn(heroText, 1); hero.Children.Add(heroText);
            var heroCard = Surface(hero); heroCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
            root.Children.Add(heroCard);

            var choices = new List<(CheckBox Check, CleanCategory Category)>();
            foreach (var category in categories)
            {
                var check = new CheckBox { IsChecked = category.Files.Count > 0, IsEnabled = category.Files.Count > 0 };
                root.Children.Add(ChoiceRow(check, category.Name, $"{category.Files.Count} arquivos · {category.Root}", $"{category.Bytes / 1048576d:N1} MB", category.Files.Count > 0 ? "Accent" : "Success"));
                choices.Add((check, category));
            }
            var total = Label("", 13, true);
            total.Margin = new Thickness(0); total.VerticalAlignment = VerticalAlignment.Center;
            var clean = IconButton(Glyphs.Delete, "Excluir selecionados", primary: true);
            void Refresh()
            {
                var selected = choices.Where(c => c.Check.IsChecked == true).ToList();
                total.Text = $"Selecionado: {selected.Sum(c => c.Category.Bytes) / 1048576d:N1} MB";
                clean.IsEnabled = selected.Any(c => c.Category.Files.Count > 0);
            }
            foreach (var (check, _) in choices) { check.Checked += (_, _) => Refresh(); check.Unchecked += (_, _) => Refresh(); }
            Refresh();
            clean.Click += async (_, _) =>
            {
                clean.IsEnabled = false;
                foreach (var (check, _) in choices) check.IsEnabled = false;
                await ExecuteTrackedAsync("Limpeza de temporários", async () =>
                {
                    var selected = choices.Where(c => c.Check.IsChecked == true).Select(c => c.Category).ToArray();
                    var before = selected.Sum(c => c.Bytes);
                    var result = await _cleaner.CleanAsync(selected, new Progress<string>(s => OperationStatus.Text = s));
                    total.Text = $"Previsto: {before / 1048576d:N1} MB → liberado: {result.BytesFreed / 1048576d:N1} MB · {result.Removed} removidos · {result.Ignored} preservados";
                    _log.Write("INFO", total.Text);
                });
            };
            root.Children.Add(ActionBar(total, clean));
        });
    }

    private void ShowHistory()
    {
        PageTitle.Text = "Atividade e reversão";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        var backupPanel = new StackPanel();
        var latest = Path.Combine(SnapshotDirectory, "ultima_otimizacao.json");
        try
        {
            if (File.Exists(latest))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(latest));
                var items = json.RootElement.GetProperty("Itens").EnumerateArray().ToArray();

                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
                var revert = IconButton(Glyphs.Undo, "Restaurar configurações", primary: true);
                revert.IsEnabled = items.Length > 0; revert.Margin = new Thickness(12, 0, 0, 0); revert.VerticalAlignment = VerticalAlignment.Center;
                revert.Click += async (_, _) =>
                {
                    await RunLiveAsync("reverter");
                };
                DockPanel.SetDock(revert, Dock.Right); head.Children.Add(revert);
                var chip = IconChip(Glyphs.Shield, "Success", 44); DockPanel.SetDock(chip, Dock.Left); head.Children.Add(chip);
                var headText = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var title = Label("Backup pendente: " + json.RootElement.GetProperty("Nome").GetString(), 15);
                title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0);
                headText.Children.Add(title);
                var sub = Label($"{json.RootElement.GetProperty("Data").GetString()} · {items.Length} itens registrados", 12, true); sub.Margin = new Thickness(0, 2, 0, 0);
                headText.Children.Add(sub);
                head.Children.Add(headText);
                backupPanel.Children.Add(head);

                var pills = new WrapPanel();
                foreach (var item in items)
                {
                    var type = item.GetProperty("Tipo").GetString();
                    var detail = item.TryGetProperty("Nome", out var n) ? n.ToString() : item.TryGetProperty("Descricao", out var d) ? d.ToString() : type ?? "";
                    // Tarefas agendadas: mostra só o nome final (\Microsoft\Windows\...\Nome)
                    if (type == "TarefaAgendada") detail = detail.Split('\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? detail;
                    // Irreversíveis: mostra só o resumo antes do parêntese
                    if (type == "Irreversivel" && detail.IndexOf('(') is > 0 and var cut) detail = detail[..cut].Trim();
                    var pill = Pill((type == "Irreversivel" ? "Não reversível · " : "") + detail, type == "Irreversivel" ? "Warning" : "Accent");
                    pill.Margin = new Thickness(0, 0, 6, 6);
                    ((TextBlock)pill.Child).FontSize = 11;
                    pills.Children.Add(pill);
                }
                backupPanel.Children.Add(pills);
            }
            else
            {
                var empty = new StackPanel { Orientation = Orientation.Horizontal };
                empty.Children.Add(IconChip(Glyphs.Check, "Success", 44));
                var emptyText = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var et = Label("Nenhuma reversão pendente", 15); et.FontWeight = FontWeights.SemiBold; et.Margin = new Thickness(0);
                emptyText.Children.Add(et);
                var es = Label("Quando você aplicar ajustes, o backup aparecerá aqui para ser restaurado com um clique.", 12, true); es.Margin = new Thickness(0, 2, 0, 0);
                emptyText.Children.Add(es);
                empty.Children.Add(emptyText);
                backupPanel.Children.Add(empty);
            }
            if (Directory.Exists(SnapshotDirectory))
            {
                var history = Directory.GetFiles(SnapshotDirectory, "*.json").Where(p => Path.GetFileName(p).StartsWith("historico_") || Path.GetFileName(p).StartsWith("revertido_")).Where(p => !p.Equals(latest, StringComparison.OrdinalIgnoreCase)).OrderByDescending(File.GetLastWriteTime).Take(10).ToList();
                if (history.Count > 0)
                {
                    var archivedTitle = Label("Histórico arquivado", 13); archivedTitle.FontWeight = FontWeights.SemiBold; archivedTitle.Margin = new Thickness(0, 16, 0, 8);
                    backupPanel.Children.Add(archivedTitle);
                }
                foreach (var path in history)
                {
                    try
                    {
                        using var archived = JsonDocument.Parse(File.ReadAllText(path));
                        var reverted = Path.GetFileName(path).StartsWith("revertido_");
                        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                        row.Children.Add(GlyphIcon(reverted ? Glyphs.Undo : Glyphs.History, 12, "MutedBrush"));
                        var line = Label($"{archived.RootElement.GetProperty("Nome")} · {archived.RootElement.GetProperty("Data")}{(reverted ? " · revertido" : "")}", 12, true);
                        line.Margin = new Thickness(10, 0, 0, 0);
                        row.Children.Add(line);
                        backupPanel.Children.Add(row);
                    }
                    catch (JsonException) { }
                }
            }
        }
        catch (Exception ex) { backupPanel.Children.Add(Label("Não foi possível ler o backup: " + ex.Message, 13, true)); }
        root.Children.Add(Surface(backupPanel));

        var activityPanel = new StackPanel();
        var activityHead = new DockPanel();
        var export = IconButton(Glyphs.Download, "Exportar");
        export.Margin = new Thickness(0, 8, 0, 0);
        export.VerticalAlignment = VerticalAlignment.Top;
        export.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "optimizer-atividade.log", Filter = "Log (*.log)|*.log" };
            if (dialog.ShowDialog(this) == true)
            {
                try { File.Copy(_log.Export(), dialog.FileName, true); OperationStatus.Text = "Atividade exportada."; }
                catch (Exception ex) { OperationStatus.Text = ex.Message; }
            }
        };
        DockPanel.SetDock(export, Dock.Right); activityHead.Children.Add(export);
        activityHead.Children.Add(SectionHeader("Atividade recente", "Tudo o que o aplicativo executou, incluindo falhas."));
        activityPanel.Children.Add(activityHead);
        var list = new ListBox { ItemsSource = _activity, Height = 320, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5 };
        activityPanel.Children.Add(list);
        root.Children.Add(Surface(activityPanel));
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
    }

    /// <summary>Pontuação simples de 0 a 100 a partir do espaço livre e do tempo ligado.</summary>
    private static (int Score, string Headline, string Tone) HealthScore(SystemSnapshot snapshot)
    {
        var score = 100.0;
        var free = Percent(snapshot.FreeSpace, snapshot.Storage);
        if (free < 10) score -= 35; else if (free < 20) score -= 20; else if (free < 30) score -= 8;
        var hours = ParseUptimeHours(snapshot.Uptime);
        if (hours > 24 * 7) score -= 20; else if (hours > 24 * 3) score -= 10;
        if (!snapshot.IsAdministrator) score -= 5;
        var value = (int)Math.Round(Math.Clamp(score, 0, 100));
        return value switch
        {
            >= 85 => (value, "Seu PC está em ótima forma", "Success"),
            >= 65 => (value, "Seu PC está bem, com alguns pontos de atenção", "Warning"),
            _ => (value, "Seu PC precisa de atenção", "Danger"),
        };
    }

    private async Task RenderDashboardAsync()
    {
        PageTitle.Text = "Visão geral";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        var loading = new StackPanel();
        loading.Children.Add(SectionHeader("Consultando seu computador...", "Lendo processador, memória, armazenamento e sistema."));
        loading.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 5 });
        root.Children.Add(Surface(loading));
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        try
        {
            var snapshot = await Task.Run(_system.Read);
            _snapshot = snapshot;
            if (_currentPage != "dashboard") return;
            root.Children.Clear();
            TitleAdminChip.Text = snapshot.IsAdministrator ? "Administrador" : "Usuário";
            TitleAdminDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, snapshot.IsAdministrator ? "SuccessBrush" : "WarningBrush");

            _updateSlot = new StackPanel();
            root.Children.Add(_updateSlot);

            // Destaque: pontuação de saúde + ações principais
            var (score, headline, tone) = HealthScore(snapshot);
            var hero = new Grid();
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var status = Pill(tone == "Success" ? "Saudável" : tone == "Warning" ? "Atenção" : "Crítico", tone);
            status.HorizontalAlignment = HorizontalAlignment.Left; status.Margin = new Thickness(0, 0, 0, 10);
            heroText.Children.Add(status);
            var headlineText = new TextBlock { Text = headline, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            headlineText.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            heroText.Children.Add(headlineText);
            var heroSub = Label($"{snapshot.OperatingSystem} · Build {snapshot.Build} · ligado há {snapshot.Uptime}", 12.5, true);
            heroSub.Margin = new Thickness(0, 6, 0, 18);
            heroText.Children.Add(heroSub);
            var actions = new WrapPanel();
            var clean = IconButton(Glyphs.Broom, "Analisar limpeza", primary: true); clean.Tag = "quickclean"; clean.Click += RunOperation_Click;
            var tune = IconButton(Glyphs.Lightning, "Revisar ajustes"); tune.Tag = "padrao"; tune.Click += RunOperation_Click;
            var diag = IconButton(Glyphs.Diagnostic, "Diagnóstico"); diag.Tag = "analisar"; diag.Click += RunOperation_Click;
            actions.Children.Add(clean); actions.Children.Add(tune); actions.Children.Add(diag);
            heroText.Children.Add(actions);
            hero.Children.Add(heroText);
            var ring = ScoreRing(score, "SAÚDE", 136); ring.Margin = new Thickness(24, 0, 8, 0);
            Grid.SetColumn(ring, 1); hero.Children.Add(ring);
            var heroCard = Surface(hero);
            heroCard.Padding = new Thickness(28, 24, 28, 24);
            heroCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
            heroCard.SetResourceReference(Border.BorderBrushProperty, "AccentSoftBrush");
            root.Children.Add(heroCard);

            var stats = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -14, 2) };
            stats.Children.Add(Card("PROCESSADOR", snapshot.Processor, Glyphs.Chip, "AccentBrush"));
            stats.Children.Add(Card("MEMÓRIA", snapshot.Memory, Glyphs.Memory, "InfoBrush"));
            stats.Children.Add(Card("ESPAÇO LIVRE", $"{snapshot.FreeSpace} de {snapshot.Storage}", Glyphs.Drive, "SuccessBrush"));
            stats.Children.Add(Card("PLACA DE VÍDEO", snapshot.Graphics, Glyphs.Monitor, "WarningBrush"));
            root.Children.Add(stats);

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var health = BuildHealthPanel(snapshot);
            columns.Children.Add(health);

            var activityPanel = new StackPanel();
            var activityHead = new DockPanel();
            var seeAll = new Button { Content = "Ver tudo", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Top };
            seeAll.SetResourceReference(StyleProperty, "GhostButton");
            seeAll.Click += History_Click;
            DockPanel.SetDock(seeAll, Dock.Right); activityHead.Children.Add(seeAll);
            activityHead.Children.Add(SectionHeader("Atividade recente"));
            activityPanel.Children.Add(activityHead);
            foreach (var line in _activity.TakeLast(5).Reverse())
            {
                var isError = line.Contains("[ERROR]"); var isSuccess = line.Contains("[SUCCESS]");
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 5, 10, 0), VerticalAlignment = VerticalAlignment.Top };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, isError ? "DangerBrush" : isSuccess ? "SuccessBrush" : "AccentBrush");
                row.Children.Add(dot);
                var text = Label(line, 11.5, true); text.Margin = new Thickness(0); text.TextTrimming = TextTrimming.CharacterEllipsis; text.TextWrapping = TextWrapping.NoWrap;
                text.ToolTip = line;
                row.Children.Add(text);
                activityPanel.Children.Add(row);
            }
            if (_activity.Count == 0) activityPanel.Children.Add(Label("Suas próximas execuções aparecerão aqui.", 13, true));
            var activityCard = Surface(activityPanel);
            Grid.SetColumn(activityCard, 2);
            columns.Children.Add(activityCard);
            root.Children.Add(columns);

            var profile = _configService.GetActiveProfile();
            var profileLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 8) };
            profileLine.Children.Add(GlyphIcon(Glyphs.Settings, 12, "MutedBrush"));
            var pl = Label($"Perfil selecionado: {profile.Name}. Você sempre revisa os ajustes antes de aplicar.", 12, true); pl.Margin = new Thickness(8, 0, 0, 0);
            profileLine.Children.Add(pl);
            root.Children.Add(profileLine);
        }
        catch (Exception ex)
        {
            root.Children.Clear();
            root.Children.Add(Card("NÃO FOI POSSÍVEL CONSULTAR O SISTEMA", ex.Message, Glyphs.Warning, "WarningBrush"));
            _log.Write("ERROR", ex.Message);
        }
    }

    private Border BuildHealthPanel(SystemSnapshot snapshot)
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Leitura rápida", "Indicadores que mais influenciam o desempenho do dia a dia."));
        var free = Percent(snapshot.FreeSpace, snapshot.Storage);
        AddMeter(panel, Glyphs.Drive, "Espaço livre no disco", free, $"{free:N0}% livre", free >= 20 ? "Success" : free >= 10 ? "Warning" : "Danger");
        var hours = ParseUptimeHours(snapshot.Uptime);
        AddMeter(panel, Glyphs.Clock, "Tempo desde o último reinício", Math.Min(100, hours / 168d * 100), hours < 72 ? "Recente" : "Reinicie em breve", hours < 72 ? "Success" : "Warning");
        var memory = ParseNumber(snapshot.Memory);
        AddMeter(panel, Glyphs.Memory, "Memória instalada", Math.Min(100, memory / 32d * 100), memory >= 16 ? "Ideal para jogos" : memory >= 8 ? "Suficiente" : "Limitada", memory >= 16 ? "Success" : memory >= 8 ? "Warning" : "Danger");
        return Surface(panel);
    }

    private void AddMeter(Panel panel, string glyph, string title, double value, string status, string tone)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(IconChip(glyph, tone, 34));
        var body = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel();
        var s = new TextBlock { Text = status, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        s.SetResourceReference(TextBlock.ForegroundProperty, tone + "Brush");
        DockPanel.SetDock(s, Dock.Right); head.Children.Add(s);
        var t = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.Medium };
        head.Children.Add(t);
        body.Children.Add(head);
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(value, 0, 100), Height = 6, Margin = new Thickness(0, 7, 0, 0), IsIndeterminate = false };
        body.Children.Add(bar);
        Grid.SetColumn(body, 1); row.Children.Add(body);
        panel.Children.Add(row);
    }

    private static double ParseNumber(string value)
        => double.TryParse(System.Text.RegularExpressions.Regex.Match(value ?? "", @"[\d,.]+").Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    private static double Percent(string free, string total) => ParseNumber(total) <= 0 ? 0 : ParseNumber(free) / ParseNumber(total) * 100;
    private static double ParseUptimeHours(string value)
    {
        var m = System.Text.RegularExpressions.Regex.Match(value ?? "", @"(?:(\d+)d)?\s*(?:(\d+)h)?\s*(?:(\d+)m)?");
        return (int.TryParse(m.Groups[1].Value, out var d) ? d * 24 : 0) + (int.TryParse(m.Groups[2].Value, out var h) ? h : 0) + (int.TryParse(m.Groups[3].Value, out var min) ? min / 60d : 0);
    }

    private async Task CheckForUpdateAsync(bool showPrompt = false)
    {
        try
        {
            var info = await _updates.CheckAsync(AppVersion);
            if (info.IsAvailable && (!string.IsNullOrWhiteSpace(info.AssetUrl) || !string.IsNullOrWhiteSpace(info.DownloadUrl)))
            {
                var update = IconButton(Glyphs.Download, UpdateService.CanAutoInstall(info) ? $"Atualizar para {info.LatestVersion}" : $"Baixar {info.LatestVersion}", primary: true);
                update.Margin = new Thickness(12, 0, 0, 0); update.VerticalAlignment = VerticalAlignment.Center;
                update.Click += async (_, _) =>
                {
                    if (_operationRunning) { OperationStatus.Text = "Aguarde a operação em andamento terminar antes de atualizar."; return; }
                    update.IsEnabled = false; OperationProgress.Visibility = Visibility.Visible; OperationStatus.Text = "Baixando instalador...";
                    try
                    {
                        if (UpdateService.CanAutoInstall(info))
                        {
                            using var installer = await _updates.DownloadAsync(info, new Progress<(long read, long total)>(p => OperationStatus.Text = p.total > 0 ? $"Baixando instalador... {p.read * 100d / p.total:N0}%" : $"Baixando instalador... {p.read / 1048576d:N1} MB"));
                            OperationStatus.Text = "Instalador verificado. Atualizando — o aplicativo será reaberto automaticamente...";
                            _log.Write("INFO", $"Atualização {info.LatestVersion} verificada por SHA256; iniciando instalação silenciosa.");
                            installer.LaunchSilent();
                            await Task.Delay(500);
                            Application.Current.Shutdown();
                        }
                        else Process.Start(new ProcessStartInfo(info.DownloadUrl!) { UseShellExecute = true });
                    }
                    catch (Exception ex) { OperationStatus.Text = "Falha na atualização: " + ex.Message; _log.Write("ERROR", "Atualização: " + ex.Message); }
                    finally { OperationProgress.Visibility = Visibility.Collapsed; update.IsEnabled = true; }
                };

                var banner = new DockPanel();
                DockPanel.SetDock(update, Dock.Right); banner.Children.Add(update);
                var chip = IconChip(Glyphs.Download, "Info", 40); DockPanel.SetDock(chip, Dock.Left); banner.Children.Add(chip);
                var bannerText = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var bt = Label($"Nova versão disponível: {info.LatestVersion}", 14); bt.FontWeight = FontWeights.SemiBold; bt.Margin = new Thickness(0);
                bannerText.Children.Add(bt);
                var bs = Label(UpdateService.CanAutoInstall(info) ? "O instalador é verificado por SHA256 e o aplicativo reabre sozinho ao terminar." : "Esta versão será baixada pela página de releases.", 12, true); bs.Margin = new Thickness(0, 2, 0, 0);
                bannerText.Children.Add(bs);
                banner.Children.Add(bannerText);
                var bannerCard = Surface(banner); bannerCard.Padding = new Thickness(18, 14, 18, 14);
                bannerCard.SetResourceReference(Border.BorderBrushProperty, "InfoBrush");
                if (_currentPage == "dashboard" && _updateSlot != null) _updateSlot.Children.Add(bannerCard);
                OperationStatus.Text = $"Atualização disponível: {info.LatestVersion}";
                if (showPrompt)
                {
                    var answer = Msg($"A versão {info.LatestVersion} está disponível. Deseja atualizar agora?", "Atualização disponível", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (answer == MessageBoxResult.Yes) update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }
        }
        catch (Exception ex)
        {
            // Atualização é opcional e não deve impedir o dashboard
            _log.Write("WARN", "Não foi possível verificar atualizações: " + ex.Message);
        }
    }
}
