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

    /// <summary>Linha de um ajuste no backup pendente: nome, itens alterados e o botão Desfazer.</summary>
    private Border BackupStepRow(string? step, JsonElement[] items)
    {
        var row = new DockPanel();
        var reversible = items.Any(i => i.GetProperty("Tipo").GetString() != "Irreversivel");
        if (step != null && reversible)
        {
            var undo = IconButton(Glyphs.Undo, "Desfazer");
            undo.Margin = new Thickness(12, 0, 0, 0); undo.VerticalAlignment = VerticalAlignment.Top;
            undo.Click += async (_, _) => await RunLiveAsync("reverter", new[] { step });
            DockPanel.SetDock(undo, Dock.Right); row.Children.Add(undo);
        }
        var text = new StackPanel();
        var title = Label(step ?? "Ajustes de versões anteriores", 13.5); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0);
        text.Children.Add(title);
        if (step is null)
        {
            var hint = Label("Feitos antes do backup por ajuste; voltam todos juntos com \"Restaurar configurações\".", 12, true); hint.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(hint);
        }
        var pills = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var item in items)
        {
            var type = item.GetProperty("Tipo").GetString();
            var detail = item.TryGetProperty("Nome", out var n) ? n.ToString() : item.TryGetProperty("Descricao", out var d) ? d.ToString() : type ?? "";
            // Tarefas agendadas: mostra só o nome final (\Microsoft\Windows\...\Nome)
            if (type == "TarefaAgendada") detail = detail.Split('\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? detail;
            // Irreversíveis: mostra só o resumo antes do parêntese
            if (type == "Irreversivel" && detail.IndexOf('(') is > 0 and var cut) detail = detail[..cut].Trim();
            if (type == "PlanoEnergia") detail = "Plano de energia";
            var pill = Pill((type == "Irreversivel" ? "Não reversível · " : "") + detail, type == "Irreversivel" ? "Warning" : "Accent");
            pill.Margin = new Thickness(0, 0, 6, 6);
            ((TextBlock)pill.Child).FontSize = 11;
            pills.Children.Add(pill);
        }
        text.Children.Add(pills);
        row.Children.Add(text);
        var border = new Border { Child = row, Padding = new Thickness(16, 12, 16, 8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return border;
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

                // Um bloco por ajuste: o que ele mudou e um botão para desfazer só ele.
                // Backups de versões antigas não sabem de qual ajuste veio cada item e só voltam juntos.
                foreach (var group in items.GroupBy(i => i.TryGetProperty("Etapa", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null))
                    backupPanel.Children.Add(BackupStepRow(group.Key, group.ToArray()));
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
        var logSearch = new TextBox { MinWidth = 100, Height = 32, Padding = new Thickness(10, 6, 10, 6), ToolTip = "Buscar na atividade" };
        System.Windows.Automation.AutomationProperties.SetName(logSearch, Translator.Tr("Buscar na atividade"));
        logSearch.SetResourceReference(Control.BackgroundProperty, "CardBgBrush");
        logSearch.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var logFilter = new ComboBox { Width = 130, Height = 32, Margin = new Thickness(8, 0, 0, 0) };
        foreach (var option in new[] { "Todos", "Sucesso", "Avisos", "Erros", "Informações" }) logFilter.Items.Add(option);
        logFilter.SelectedIndex = 0;
        DockPanel.SetDock(logFilter, Dock.Right);
        logTools.Children.Add(logFilter); logTools.Children.Add(logSearch);
        activityPanel.Children.Add(logTools);
        var logRows = new ListBox { Height = 320, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        VirtualizingPanel.SetIsVirtualizing(logRows, true);
        VirtualizingPanel.SetVirtualizationMode(logRows, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(logRows, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(logRows, ScrollBarVisibility.Disabled);
        var itemStyle = new Style(typeof(ListBoxItem), (Style)FindResource(typeof(ListBoxItem)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6)));
        logRows.ItemContainerStyle = itemStyle;
        logRows.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Border x:Name="row" BorderThickness="3,0,0,0" BorderBrush="{DynamicResource InfoBrush}" Padding="10,2" Margin="0,0,0,4" ToolTip="{Binding Raw}">
                <StackPanel>
                  <TextBlock Text="{Binding Heading}" FontSize="11" Foreground="{DynamicResource MutedBrush}" Margin="0,0,0,4"/>
                  <TextBlock Text="{Binding Message}" FontSize="12" Foreground="{DynamicResource TextBrush}" TextWrapping="Wrap"/>
                </StackPanel>
              </Border>
              <DataTemplate.Triggers>
                <DataTrigger Binding="{Binding Level}" Value="ERROR"><Setter TargetName="row" Property="BorderBrush" Value="{DynamicResource DangerBrush}"/></DataTrigger>
                <DataTrigger Binding="{Binding Level}" Value="WARN"><Setter TargetName="row" Property="BorderBrush" Value="{DynamicResource WarningBrush}"/></DataTrigger>
                <DataTrigger Binding="{Binding Level}" Value="SUCCESS"><Setter TargetName="row" Property="BorderBrush" Value="{DynamicResource SuccessBrush}"/></DataTrigger>
              </DataTemplate.Triggers>
            </DataTemplate>
            """);
        var emptyLogs = Label("Nenhum registro corresponde aos filtros.", 12, true);
        activityPanel.Children.Add(logRows);
        activityPanel.Children.Add(emptyLogs);
        void RenderLogs()
        {
            var query = logSearch.Text.Trim();
            var category = logFilter.SelectedIndex switch { 1 => "SUCCESS", 2 => "WARN", 3 => "ERROR", 4 => "INFO", _ => "" };
            var entries = _activity.Reverse().Select(ActivityEntry.Parse)
                .Where(entry => entry.Matches(query, category)).ToArray();
            logRows.ItemsSource = entries;
            emptyLogs.Visibility = entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            logRows.Visibility = entries.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        var searchTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        void ScheduleRefresh() { searchTimer.Stop(); searchTimer.Start(); }
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); RenderLogs(); };
        logSearch.TextChanged += (_, _) => ScheduleRefresh();
        logFilter.SelectionChanged += (_, _) => { searchTimer.Stop(); RenderLogs(); };
        System.Collections.Specialized.NotifyCollectionChangedEventHandler activityChanged = (_, _) => ScheduleRefresh();
        root.Loaded += (_, _) => { _activity.CollectionChanged += activityChanged; RenderLogs(); };
        root.Unloaded += (_, _) => { searchTimer.Stop(); _activity.CollectionChanged -= activityChanged; };
        RenderLogs();
        root.Children.Add(Surface(activityPanel));
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
    }
}
