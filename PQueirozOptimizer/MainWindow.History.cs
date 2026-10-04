using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Text.RegularExpressions;
using System.Windows.Media;
using PQueirozOptimizer.Services;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer;

/// <summary>Atividade e reversão: backups, snapshots e o registro de atividade.</summary>
public partial class MainWindow
{
    private void History_Click(object sender, RoutedEventArgs e) => NavigateTo("history");

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
        var logTools = new DockPanel { Margin = new Thickness(0, 8, 0, 10) };
        var logSearch = new TextBox { Width = 240, Height = 32, Padding = new Thickness(10, 6, 10, 6), ToolTip = "Buscar na atividade" };
        logSearch.SetResourceReference(Control.BackgroundProperty, "CardBgBrush");
        logSearch.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var logFilter = new ComboBox { Width = 130, Height = 32, Margin = new Thickness(8, 0, 0, 0) };
        foreach (var option in new[] { "Todos", "Sucesso", "Avisos", "Erros", "Informações" }) logFilter.Items.Add(option);
        logFilter.SelectedIndex = 0;
        logTools.Children.Add(logSearch); logTools.Children.Add(logFilter);
        activityPanel.Children.Add(logTools);
        var logRows = new StackPanel();
        activityPanel.Children.Add(new ScrollViewer { Height = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = logRows });
        void RenderLogs()
        {
            logRows.Children.Clear();
            var query = logSearch.Text.Trim();
            var filter = logFilter.SelectedItem?.ToString() ?? "Todos";
            foreach (var line in _activity.Reverse().Where(line => MatchesLog(line, query, filter)))
            {
                var match = Regex.Match(line, @"^\[(?<time>[^\]]+)\]\s*\[(?<level>[^\]]+)\]\s*(?<message>.*)$");
                var level = match.Success ? match.Groups["level"].Value : "INFO";
                var message = match.Success ? match.Groups["message"].Value : line;
                var tone = level == "ERROR" ? "Danger" : level == "WARN" ? "Warning" : level == "SUCCESS" ? "Success" : "Info";
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), ToolTip = line };
                row.Children.Add(IconChip(level switch { "ERROR" => Glyphs.Error, "WARN" => Glyphs.Warning, "SUCCESS" => Glyphs.Check, _ => Glyphs.Info }, tone, 28));
                var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
                text.Children.Add(Label(match.Success ? match.Groups["time"].Value : "Agora", 10.5, true));
                var friendly = Label(Translator.Tr(message), 12); friendly.TextWrapping = TextWrapping.Wrap;
                text.Children.Add(friendly); row.Children.Add(text); logRows.Children.Add(row);
            }
            if (logRows.Children.Count == 0) logRows.Children.Add(Label("Nenhum registro corresponde aos filtros.", 12, true));
        }
        bool MatchesLog(string line, string query, string filter)
        {
            var level = Regex.Match(line, @"\[(ERROR|WARN|SUCCESS|INFO)\]").Groups[1].Value;
            var category = filter switch { "Erros" => "ERROR", "Avisos" => "WARN", "Sucesso" => "SUCCESS", "Informações" => "INFO", _ => "" };
            return (category.Length == 0 || level == category) && (query.Length == 0 || line.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        logSearch.TextChanged += (_, _) => RenderLogs();
        logFilter.SelectionChanged += (_, _) => RenderLogs();
        RenderLogs();
        root.Children.Add(Surface(activityPanel));
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
    }
}
