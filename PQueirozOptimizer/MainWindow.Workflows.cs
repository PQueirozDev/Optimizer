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

/// <summary>Operações: execução com progresso, interruptores ligados a ações, revisão das etapas e prévia da limpeza.</summary>
public partial class MainWindow
{
    private readonly ObservableCollection<string> _activity = new();
    private static string AppVersion => "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
    private static readonly string SnapshotDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "Snapshots");

    private CancellationTokenSource? _operationCts;
    private bool _closeAfterOperation;

    /// <summary>
    /// Liga um interruptor a uma ação (true = ligou, false = desligou). A ação roda uma vez por clique e o
    /// interruptor fica bloqueado enquanto ela roda. Se falhar (ou outra operação estiver em andamento), ele
    /// volta ao estado anterior SEM disparar a ação de novo — antes, a volta disparava a ação oposta, que
    /// também falhava e voltava de novo, até o app fechar com estouro de pilha.
    /// </summary>
    private void BindActionToggle(CheckBox check, Func<bool, Task<bool>> apply, Action? after = null)
    {
        var busy = false;
        async void Changed(bool value)
        {
            if (busy) return; // a própria volta abaixo: ignora
            busy = true;
            var wasEnabled = check.IsEnabled;
            check.IsEnabled = false;
            var ok = false;
            try { ok = await apply(value); }
            catch (Exception ex) { _log.Write("ERROR", "Não foi possível concluir a ação: " + ex.Message); }
            finally
            {
                if (!ok) check.IsChecked = !value;
                busy = false;
                check.IsEnabled = wasEnabled;
            }
            if (after != null) AfterToggleAnimation(after);
        }
        check.Checked += (_, _) => Changed(true);
        check.Unchecked += (_, _) => Changed(false);
    }

    /// <summary>Redesenha a página só depois da animação do interruptor; antes, ele era trocado por um novo sem animar.</summary>
    private void AfterToggleAnimation(Action action)
    {
        if (!AppearanceService.AnimationsEnabled) { action(); return; }
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        timer.Tick += (_, _) => { timer.Stop(); action(); };
        timer.Start();
    }

    /// <summary>Executa com progresso, cancelamento e notificação. Retorna true só se terminou sem erro.</summary>
    private async Task<bool> ExecuteTrackedAsync(string title, Func<CancellationToken, Task> action)
    {
        if (_operationRunning) { OperationStatus.Text = "Aguarde a operação em andamento."; return false; }
        _operationRunning = true;
        using var cts = new CancellationTokenSource();
        _operationCts = cts;
        OperationProgress.Visibility = Visibility.Visible;
        OperationStatus.Text = title;
        TitleOptChip.Text = "Em execução";
        try
        {
            await action(cts.Token);
            _snapshot = null;
            OperationStatus.Text = title + " — concluído. Confira os resultados na atividade.";
            TitleOptChip.Text = "Concluído";
            ShowToast(title, "Concluído", "Success");
            return true;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _snapshot = null;
            OperationStatus.Text = title + " — cancelado. O que já foi aplicado aparece em Atividade e reversão.";
            TitleOptChip.Text = "Cancelado";
            ShowToast(title, "Cancelado", "Warning");
        }
        catch (Exception ex)
        {
            _log.Write("ERROR", ex.Message);
            OperationStatus.Text = ex.Message;
            TitleOptChip.Text = "Verificar resultado";
            ShowToast(title, ex.Message, "Danger");
        }
        finally
        {
            _operationRunning = false; _operationCts = null; OperationProgress.Visibility = Visibility.Collapsed;
            // O usuário pediu para fechar durante a operação: fecha assim que ela termina de ser cancelada
            if (_closeAfterOperation) Close();
        }
        return false;
    }

    /// <summary>Confirma com o usuário e cancela a operação em andamento.</summary>
    private bool ConfirmCancelOperation()
    {
        if (_operationCts is not { IsCancellationRequested: false } cts) return false;
        if (Msg("Cancelar a operação agora? O que já foi aplicado continua registrado no backup e pode ser revertido em Atividade e reversão.",
                "Cancelar operação", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
        // A operação pode ter terminado enquanto a pergunta estava aberta
        if (_operationCts != cts) return false;
        OperationStatus.Text = "Cancelando...";
        cts.Cancel();
        return true;
    }

    /// <summary>Linha selecionável (checkbox) com título, descrição e etiqueta de impacto.</summary>
    private Border ChoiceRow(CheckBox check, string title, string detail, string pill, string tone)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        var t = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        text.Children.Add(t);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var d = new TextBlock { Text = detail, FontSize = 12.5, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
            d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            text.Children.Add(d);
        }
        grid.Children.Add(text);
        var badge = Pill(pill, tone); badge.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(badge, 1); grid.Children.Add(badge);
        check.Content = grid;
        check.SetResourceReference(StyleProperty, "SwitchCheckBox");
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
                var (pill, tone, detail) = step.Effect switch
                {
                    StepEffect.Irreversible => ("Não reversível", "Warning", "Remove arquivos ou apps: não é desfeito pela reversão."),
                    StepEffect.OneOff => ("Ação pontual", "Info", "Não altera configurações, então não precisa de backup."),
                    _ => ("Com backup", "Success", ""),
                };
                var check = new CheckBox { Tag = step.Name, IsChecked = false };
                checks.Add(check);
                root.Children.Add(ChoiceRow(check, step.Name, detail, pill, tone));
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
        await ExecuteTrackedAsync("Analisar temporários", async token =>
        {
            var categories = await _cleaner.AnalyzeAsync(token);
            root.Children.Clear();

            var totalBytes = categories.Sum(c => c.Bytes);
            var hero = new Grid();
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hero.Children.Add(IconChip(Glyphs.Broom, "Accent", 52));
            var heroText = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var big = new TextBlock { Text = $"{totalBytes / 1048576d:N1} MB podem ser liberados", FontSize = 24, FontWeight = FontWeights.SemiBold };
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
                await ExecuteTrackedAsync("Limpeza de temporários", async token =>
                {
                    var selected = choices.Where(c => c.Check.IsChecked == true).Select(c => c.Category).ToArray();
                    var before = selected.Sum(c => c.Bytes);
                    var result = await _cleaner.CleanAsync(selected, new Progress<string>(s => OperationStatus.Text = s), token);
                    total.Text = $"Previsto: {before / 1048576d:N1} MB → liberado: {result.BytesFreed / 1048576d:N1} MB · {result.Removed} removidos · {result.Ignored} preservados";
                    _log.Write("INFO", total.Text);
                });
            };
            root.Children.Add(ActionBar(total, clean));
        });
    }
}
