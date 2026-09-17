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

    private TextBlock Label(string text, int size = 13, bool muted = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        label.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush");
        return label;
    }

    private Border Surface(UIElement content) 
    {
        var border = new Border { Child = content, Padding = new Thickness(20), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 16) };
        border.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return border;
    }

    private void Primary(Button button)
    {
        button.SetResourceReference(Button.BackgroundProperty, "AccentBrush");
        button.SetResourceReference(Button.BorderBrushProperty, "AccentBrush");
        button.Foreground = new SolidColorBrush(Color.FromRgb(16, 17, 20));
    }

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
                await ExecuteTrackedAsync(operation, () => _powershell.RunAsync(operation, progress: new Progress<string>(s => OperationStatus.Text = s)));
                return;
            }
            PageTitle.Text = "Revisar ajustes";
            PageBadge.Visibility = Visibility.Collapsed;
            var root = new StackPanel();
            root.Children.Add(Label("Escolha exatamente o que deseja aplicar", 20));
            root.Children.Add(Label("Um ponto de restauração será solicitado antes dos ajustes. Se a criação falhar, a execução será interrompida. A limpeza de arquivos fica na Limpeza Rápida.", 13, true));
            root.Children.Add(Label("Alterações de energia, registro e serviços possuem backup. Exclusões e desinstalações não são reversíveis pelo snapshot. Alguns ajustes exigem reiniciar o Windows; o app não reinicia automaticamente.", 13, true));
            var checks = new List<CheckBox>();
            foreach (var step in steps)
            {
                var irreversible = step.Contains("removid", StringComparison.OrdinalIgnoreCase) || step.Contains("Limp", StringComparison.OrdinalIgnoreCase);
                var check = new CheckBox { Content = Label(step + (irreversible ? "  ·  não reversível pelo snapshot" : "  ·  revisar impacto"), 13), Tag = step, IsChecked = false, Margin = new Thickness(0, 8, 0, 8) };
                checks.Add(check);
                root.Children.Add(Surface(check));
            }
            var apply = new Button { Content = "Aplicar selecionados", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left };
            Primary(apply);
            foreach (var check in checks)
            {
                check.Checked += (_, _) => apply.IsEnabled = checks.Any(c => c.IsChecked == true);
                check.Unchecked += (_, _) => apply.IsEnabled = checks.Any(c => c.IsChecked == true);
            }
            apply.Click += async (_, _) =>
            {
                apply.IsEnabled = false;
                foreach (var check in checks) check.IsEnabled = false;
                var selected = checks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray();
                await ExecuteTrackedAsync("Aplicar ajustes", () => _powershell.RunAsync(operation, selected, new Progress<string>(s => OperationStatus.Text = s)));
                foreach (var check in checks) check.IsEnabled = true;
                apply.IsEnabled = true;
            };
            root.Children.Add(apply);
            ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        }
        catch (Exception ex) { _log.Write("ERROR", ex.Message); OperationStatus.Text = ex.Message; }
    }

    private async Task ShowCleanPreviewAsync()
    {
        PageTitle.Text = "Limpeza rápida";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        root.Children.Add(Label("Analisando arquivos temporários...", 20));
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        await ExecuteTrackedAsync("Analisar temporários", async () =>
        {
            var categories = await _cleaner.AnalyzeAsync();
            root.Children.Clear();
            root.Children.Add(Label("Revise o espaço que pode liberar", 20));
            root.Children.Add(Label("Arquivos alterados nas últimas 48 horas, links e itens sem acesso são preservados. A exclusão é permanente. A estimativa considera o tamanho dos arquivos.", 13, true));
            var choices = new List<(CheckBox Check, CleanCategory Category)>();
            foreach (var category in categories)
            {
                var check = new CheckBox { Content = Label(category.Name + $" — {category.Bytes / 1048576d:N1} MB · {category.Files.Count} arquivos"), IsChecked = category.Files.Count > 0, IsEnabled = category.Files.Count > 0 };
                root.Children.Add(Surface(check)); choices.Add((check, category));
            }
            var total = Label("", 16);
            var clean = new Button { Content = "Excluir selecionados", HorizontalAlignment = HorizontalAlignment.Left };
            Primary(clean);
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
                    total.Text = $"Previsto: {before / 1048576d:N1} MB → liberado: {result.BytesFreed / 1048576d:N1} MB\n{result.Removed} removidos · {result.Ignored} preservados/ignorados";
                    _log.Write("INFO", total.Text);
                });
            };
            root.Children.Add(total); root.Children.Add(clean);
        });
    }

    private void ShowHistory()
    {
        PageTitle.Text = "Atividade e reversão";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        var backupPanel = new StackPanel();
        backupPanel.Children.Add(Label("Última execução com backup", 18));
        var latest = Path.Combine(SnapshotDirectory, "ultima_otimizacao.json");
        try
        {
            if (File.Exists(latest))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(latest));
                backupPanel.Children.Add(Label($"{json.RootElement.GetProperty("Nome").GetString()} · {json.RootElement.GetProperty("Data").GetString()}", 13, true));
                var items = json.RootElement.GetProperty("Itens").EnumerateArray().ToArray();
                backupPanel.Children.Add(Label($"{items.Length} registros no backup. Confira abaixo o que pode ser restaurado.", 13, true));
                foreach (var item in items)
                {
                    var type = item.GetProperty("Tipo").GetString();
                    var detail = item.TryGetProperty("Nome", out var n) ? n.ToString() : item.TryGetProperty("Descricao", out var d) ? d.ToString() : type;
                    backupPanel.Children.Add(Label((type == "Irreversivel" ? "Não reversível: " : "Backup: ") + detail, 12, true));
                }
                var revert = new Button { Content = "Restaurar configurações deste backup", HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = items.Length > 0 };
                revert.Click += async (_, _) =>
                {
                    revert.IsEnabled = false;
                    await ExecuteTrackedAsync("Reverter configurações", () => _powershell.RunAsync("reverter", progress: new Progress<string>(s => OperationStatus.Text = s)));
                    if (_currentPage == "history") ShowHistory();
                };
                backupPanel.Children.Add(revert);
            }
            else backupPanel.Children.Add(Label("Nenhuma reversão pendente.", 13, true));
            if (Directory.Exists(SnapshotDirectory))
            {
                var history = Directory.GetFiles(SnapshotDirectory, "*.json").Where(p => Path.GetFileName(p).StartsWith("historico_") || Path.GetFileName(p).StartsWith("revertido_")).Where(p => !p.Equals(latest, StringComparison.OrdinalIgnoreCase)).OrderByDescending(File.GetLastWriteTime).Take(10);
                foreach (var path in history)
                {
                    try
                    {
                        using var archived = JsonDocument.Parse(File.ReadAllText(path));
                        backupPanel.Children.Add(Label($"Arquivado · {archived.RootElement.GetProperty("Nome")} · {archived.RootElement.GetProperty("Data")}", 12, true));
                    }
                    catch (JsonException) { }
                }
            }
        }
        catch (Exception ex) { backupPanel.Children.Add(Label("Não foi possível ler o backup: " + ex.Message, 13, true)); }
        root.Children.Add(Surface(backupPanel));
        root.Children.Add(Label("Atividade recente", 18));
        var list = new ListBox { ItemsSource = _activity, Height = 300, BorderThickness = new Thickness(0), FontFamily = new FontFamily("Consolas"), FontSize = 11 };
        list.SetResourceReference(Control.BackgroundProperty, "CardBgBrush");
        list.SetResourceReference(Control.ForegroundProperty, "MutedBrush");
        root.Children.Add(list);
        var export = new Button { Content = "Exportar atividade", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
        export.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "optimizer-atividade.log", Filter = "Log (*.log)|*.log" };
            if (dialog.ShowDialog(this) == true)
            {
                try { File.Copy(_log.Export(), dialog.FileName, true); OperationStatus.Text = "Atividade exportada."; }
                catch (Exception ex) { OperationStatus.Text = ex.Message; }
            }
        };
        root.Children.Add(export);
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
    }

    private async Task RenderDashboardAsync()
    {
        PageTitle.Text = "Visão geral";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        root.Children.Add(Label("Consultando seu computador...", 14, true));
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        try
        {
            var snapshot = await Task.Run(_system.Read);
            _snapshot = snapshot;
            if (_currentPage != "dashboard") return;
            root.Children.Clear();
            TitleAdminChip.Text = snapshot.IsAdministrator ? "Administrador" : "Usuário";
            TitleAdminDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MutedBrush");
            var stats = new UniformGrid { Columns = 3 };
            stats.Children.Add(Card("PROCESSADOR", snapshot.Processor, "#969BA6"));
            stats.Children.Add(Card("MEMÓRIA INSTALADA", snapshot.Memory, "#969BA6"));
            stats.Children.Add(Card("ESPAÇO LIVRE", snapshot.FreeSpace, "#969BA6"));
            root.Children.Add(stats);
            root.Children.Add(BuildHealthPanel(snapshot));
            var details = new StackPanel();
            details.Children.Add(Label("Seu sistema", 18));
            details.Children.Add(Label(snapshot.OperatingSystem + " · Build " + snapshot.Build + " · " + snapshot.Architecture, 13, true));
            details.Children.Add(Label("GPU: " + snapshot.Graphics + "  /  Armazenamento: " + snapshot.Storage, 13, true));
            details.Children.Add(Label("Tempo ligado: " + snapshot.Uptime, 13, true));
            root.Children.Add(Surface(details));
            root.Children.Add(Label("O que você quer fazer?", 18));
            var actions = new WrapPanel();
            var clean = ActionButton("Analisar limpeza", "quickclean"); Primary(clean);
            actions.Children.Add(clean);
            actions.Children.Add(ActionButton("Revisar ajustes", "padrao"));
            actions.Children.Add(ActionButton("Diagnóstico", "analisar"));
            var history = new Button { Content = "Atividade e reversão" }; history.Click += History_Click;
            actions.Children.Add(history);
            var refresh = new Button { Content = "Atualizar informações" }; refresh.Click += async (_, _) => await RenderDashboardAsync();
            actions.Children.Add(refresh);
            root.Children.Add(actions);
            var profile = _configService.GetActiveProfile();
            root.Children.Add(Label("Perfil selecionado: " + profile.Name + ". Selecione os ajustes antes de aplicar.", 12, true));
            root.Children.Add(Label("Atividade recente", 18));
            foreach (var line in _activity.TakeLast(4)) root.Children.Add(Label(line, 12, true));
            if (_activity.Count == 0) root.Children.Add(Label("Suas próximas execuções aparecerão aqui.", 13, true));
        }
        catch (Exception ex)
        {
            root.Children.Clear(); root.Children.Add(Label("Não foi possível consultar o sistema. " + ex.Message, 14, true));
            _log.Write("ERROR", ex.Message);
        }
    }

    private Border BuildHealthPanel(SystemSnapshot snapshot)
    {
        var panel = new StackPanel();
        panel.Children.Add(Label("Leitura rápida", 18));
        AddMeter(panel, "Espaço livre", Percent(snapshot.FreeSpace, snapshot.Storage), "Quanto maior, melhor");
        AddMeter(panel, "Tempo ligado", Math.Min(100, ParseUptimeHours(snapshot.Uptime) / 168d * 100), "Últimos 7 dias");
        AddMeter(panel, "Memória instalada", Math.Min(100, ParseNumber(snapshot.Memory) / 64d * 100), "Referência visual");
        return Surface(panel);
    }

    private void AddMeter(Panel panel, string title, double value, string subtitle)
    {
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var text = new StackPanel();
        text.Children.Add(Label(title, 13)); text.Children.Add(Label(subtitle, 11, true));
        DockPanel.SetDock(text, Dock.Left); row.Children.Add(text);
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(value, 0, 100), Height = 7, Width = 260, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        bar.SetResourceReference(ProgressBar.ForegroundProperty, "AccentBrush"); bar.SetResourceReference(ProgressBar.BackgroundProperty, "BorderBrush");
        row.Children.Add(bar); panel.Children.Add(row);
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
                var update = new Button { Content = $"Baixar atualização {info.LatestVersion}", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
                update.Click += async (_, _) =>
                {
                    update.IsEnabled = false; OperationProgress.Visibility = Visibility.Visible; OperationStatus.Text = "Baixando instalador...";
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(info.AssetUrl))
                        {
                            var path = await _updates.DownloadAsync(info, new Progress<(long read, long total)>(p => OperationStatus.Text = p.total > 0 ? $"Baixando instalador... {p.read * 100d / p.total:N0}%" : $"Baixando instalador... {p.read / 1048576d:N1} MB"));
                            OperationStatus.Text = $"Download concluído: {path}";
                            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                        }
                        else Process.Start(new ProcessStartInfo(info.DownloadUrl!) { UseShellExecute = true });
                    }
                    catch (Exception ex) { OperationStatus.Text = "Falha no download: " + ex.Message; }
                    finally { OperationProgress.Visibility = Visibility.Collapsed; update.IsEnabled = true; }
                };
                if (_currentPage == "dashboard") ((Panel)ContentHost.Children[0]).Children.Add(update);
                OperationStatus.Text = $"Atualização disponível: {info.LatestVersion}";
                if (showPrompt)
                {
                    var answer = MessageBox.Show($"A versão {info.LatestVersion} está disponível. Deseja baixar o instalador agora?", "Atualização disponível", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (answer == MessageBoxResult.Yes) update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }
        }
        catch { /* atualização é opcional e não deve impedir o dashboard */ }
    }

}

