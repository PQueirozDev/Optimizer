using System.IO;
using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Dispositivos: fantasmas (lembrados mas desconectados), aparelhos com problema e reservas de portas COM.</summary>
public partial class MainWindow
{
    private string _deviceFilter = "";
    private bool _deviceShowAll;

    private static string ComBackupPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PQueirozOptimizer", "comdb-backup.bin");

    private void ShowDevices()
    {
        PageTitle.Text = "Dispositivos";
        PageBadge.Visibility = Visibility.Collapsed;
        PageActions.Children.Clear();
        PageActions.Children.Add(RoundIcon(Glyphs.Refresh, "Ler de novo", () => NavigateTo("devices")));
        var root = new StackPanel();
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        root.Children.Add(LoadingCard(L("Lendo os dispositivos que o Windows conhece…", "Reading the devices Windows knows about…")));
        _ = Task.Run(() => (Devices: DeviceCleanupService.List(), Com: DeviceCleanupService.ComStatus())).ContinueWith(t =>
        {
            root.Children.Clear();
            if (!t.IsCompletedSuccessfully) { root.Children.Add(EmptyState(Glyphs.Warning, L("Não foi possível ler os dispositivos", "Could not read the devices"), t.Exception?.InnerException?.Message ?? "")); return; }
            RenderDevices(root, t.Result.Devices, t.Result.Com);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void RenderDevices(StackPanel root, List<DeviceEntry> devices, (List<int> Reserved, List<int> InUse) com)
    {
        var ghosts = devices.Where(d => d.Removable).ToList();
        var problems = devices.Where(d => d.HasProblem).ToList();

        // ---------- Resumo ----------
        var stats = HomeGrid(200);
        stats.Children.Add(StatCard(devices.Count(d => d.Present).ToString(), L("Conectados agora", "Connected now")));
        stats.Children.Add(StatCard(ghosts.Count.ToString(), L("Fantasmas (removíveis)", "Ghosts (removable)")));
        stats.Children.Add(StatCard(problems.Count.ToString(), L("Com problema no driver", "With a driver problem")));
        stats.Children.Add(StatCard(com.Reserved.Count.ToString(), L("Portas COM reservadas", "Reserved COM ports")));
        root.Children.Add(stats);

        // ---------- Problemas ----------
        if (problems.Count > 0)
        {
            var list = new StackPanel();
            list.Children.Add(CardHeader(Glyphs.Warning, L("DRIVERS", "DRIVERS"), L("Dispositivos com problema", "Devices with a problem"), Pill(problems.Count.ToString(), "Warning"), titleIsData: false));
            foreach (var d in problems) list.Children.Add(DeviceRow(d, null));
            var open = SecondaryButton(Glyphs.Devices, L("Abrir o Gerenciador de Dispositivos", "Open Device Manager"));
            open.HorizontalAlignment = HorizontalAlignment.Left; open.Margin = new Thickness(0, 12, 0, 0);
            open.Click += (_, _) => StartSystemTool("devmgmt.msc");
            list.Children.Add(open);
            root.Children.Add(Surface(list));
        }

        // ---------- Fantasmas ----------
        var body = new StackPanel();
        body.Children.Add(CardHeader(Glyphs.Delete, L("LIMPEZA", "CLEANUP"), L("Dispositivos fantasmas", "Ghost devices"), Pill(ghosts.Count.ToString(), ghosts.Count > 0 ? "Info" : "Success"), titleIsData: false));
        var intro = Label(L("Aparelhos que já foram conectados e o Windows continua lembrando: pendrives, controles, fones, placas trocadas e portas COM antigas. Remover só apaga o registro; se o aparelho voltar, o Windows instala de novo sozinho. Os conectados e os do sistema nunca entram na lista.",
            "Devices that were connected once and Windows still remembers: USB sticks, controllers, headsets, replaced cards and old COM ports. Removing only deletes the record; if the device comes back, Windows installs it again on its own. Connected and system devices never show up here."), 12.5, true);
        intro.Tag = Translator.SystemDataTag;
        body.Children.Add(intro);
        if (ghosts.Count == 0)
        {
            var ok = Label(L("Nenhum dispositivo fantasma: nada a limpar.", "No ghost devices: nothing to clean."), 13);
            ok.Tag = Translator.SystemDataTag;
            body.Children.Add(ok);
            root.Children.Add(Surface(body));
        }
        else
        {
            var search = new TextBox { Width = 300, Height = 34, Padding = new Thickness(12, 7, 12, 7), FontSize = 12.5, Text = _deviceFilter, Margin = new Thickness(0, 4, 0, 10), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = L("Filtrar por nome ou classe", "Filter by name or class"), Tag = L("Filtrar por nome ou classe", "Filter by name or class") };
            body.Children.Add(search);
            var checks = new List<(CheckBox Box, DeviceEntry Device)>();
            var listHost = new StackPanel();
            var summary = Label("", 12.5, true); summary.Margin = new Thickness(0); summary.Tag = Translator.SystemDataTag;
            void UpdateSummary() => summary.Text = L($"{checks.Count(c => c.Box.IsChecked == true)} de {ghosts.Count} selecionados", $"{checks.Count(c => c.Box.IsChecked == true)} of {ghosts.Count} selected");
            foreach (var d in ghosts)
            {
                var box = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
                box.Checked += (_, _) => UpdateSummary(); box.Unchecked += (_, _) => UpdateSummary();
                checks.Add((box, d));
            }
            void Render()
            {
                listHost.Children.Clear();
                var query = search.Text.Trim();
                var visible = checks.Where(c => query.Length == 0 || c.Device.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || c.Device.Class.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var (box, d) in visible.Take(_deviceShowAll ? int.MaxValue : 40))
                {
                    if (box.Parent is Panel old) old.Children.Remove(box);
                    listHost.Children.Add(DeviceRow(d, box));
                }
                if (!_deviceShowAll && visible.Count > 40)
                {
                    var more = ChevronButton(L($"Mostrar todos ({visible.Count})", $"Show all ({visible.Count})"));
                    more.Margin = new Thickness(0, 10, 0, 0);
                    more.Click += (_, _) => { _deviceShowAll = true; Render(); };
                    listHost.Children.Add(more);
                }
            }
            search.TextChanged += (_, _) => { _deviceFilter = search.Text; Render(); };
            Render();
            body.Children.Add(listHost);
            var remove = IconButton(Glyphs.Delete, L("Remover selecionados", "Remove selected"), primary: true);
            var none = new Button { Content = L("Desmarcar todos", "Clear selection") };
            none.SetResourceReference(StyleProperty, "GhostButton");
            none.Click += (_, _) => { foreach (var c in checks) c.Box.IsChecked = false; };
            remove.Click += async (_, _) =>
            {
                var ids = checks.Where(c => c.Box.IsChecked == true).Select(c => c.Device.InstanceId).ToList();
                if (ids.Count == 0) return;
                if (Msg(L($"Remover {ids.Count} dispositivo(s) desconectado(s)? Se algum voltar a ser conectado, o Windows reinstala sozinho.", $"Remove {ids.Count} disconnected device(s)? If any is plugged in again, Windows reinstalls it on its own."),
                    L("Dispositivos", "Devices"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                (int Removed, List<string> Failed) result = (0, new());
                await ExecuteTrackedAsync(L("Remover dispositivos fantasmas", "Remove ghost devices"), _ => Task.Run(() => result = DeviceCleanupService.Remove(ids)));
                _log.Write(result.Failed.Count == 0 ? "INFO" : "WARN", $"Dispositivos fantasmas: {result.Removed} removidos, {result.Failed.Count} falhas" + (result.Failed.Count > 0 ? ": " + string.Join("; ", result.Failed.Take(5)) : ""));
                ShowToast(L("Dispositivos", "Devices"), L($"{result.Removed} removido(s)", $"{result.Removed} removed") + (result.Failed.Count > 0 ? L($", {result.Failed.Count} falha(s)", $", {result.Failed.Count} failed") : "."), result.Failed.Count == 0 ? "Success" : "Warning");
                ShowDevices();
            };
            UpdateSummary();
            root.Children.Add(Surface(body));
            root.Children.Add(ActionBar(summary, none, remove));
        }

        // ---------- Portas COM ----------
        root.Children.Add(ComPortsCard(com));
    }

    private Border StatCard(string value, string caption) => HomeCard(BigStat(value, caption));

    /// <summary>Linha de dispositivo: seleção (opcional), nome, classe e porta, separadas por fio.</summary>
    private Border DeviceRow(DeviceEntry d, CheckBox? box)
    {
        var dock = new DockPanel();
        if (box != null) { box.Margin = new Thickness(0, 0, 12, 0); DockPanel.SetDock(box, Dock.Left); dock.Children.Add(box); }
        var tag = Pill(d.Class.Length > 0 ? d.Class : L("Sem driver", "No driver"), d.Class.Length > 0 ? "Info" : "Warning"); tag.Tag = Translator.SystemDataTag;
        DockPanel.SetDock(tag, Dock.Right); dock.Children.Add(tag);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = d.Name + (d.Port != null ? $" ({d.Port})" : ""), FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Tag = Translator.SystemDataTag };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var id = new TextBlock { Text = d.InstanceId, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Tag = Translator.SystemDataTag, ToolTip = d.InstanceId };
        id.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        id.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        text.Children.Add(name); text.Children.Add(id);
        dock.Children.Add(text);
        var row = new Border { Child = dock, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 9, 0, 9) };
        row.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return row;
    }

    private Border ComPortsCard((List<int> Reserved, List<int> InUse) com)
    {
        var body = new StackPanel();
        var stale = com.Reserved.Except(com.InUse).ToList();
        body.Children.Add(CardHeader(Glyphs.Usb, L("PORTAS COM", "COM PORTS"), L("Reservas de portas seriais", "Serial port reservations"), Pill(stale.Count > 0 ? L($"{stale.Count} sem uso", $"{stale.Count} unused") : L("Em ordem", "Clean"), stale.Count > 0 ? "Warning" : "Success"), titleIsData: false));
        var intro = Label(L("Cada Arduino, ESP32 ou adaptador USB-serial ganha uma porta COM, e o Windows nunca a devolve: depois de um tempo os novos aparelhos aparecem como COM15, COM23… Liberar solta só as reservas que não pertencem a nenhum aparelho instalado (remova antes os fantasmas para liberar as deles); dá para desfazer.",
            "Every Arduino, ESP32 or USB-serial adapter gets a COM port and Windows never gives it back: after a while new devices show up as COM15, COM23… Releasing frees only reservations that belong to no installed device (remove ghosts first to free theirs); it can be undone."), 12.5, true);
        intro.Tag = Translator.SystemDataTag;
        body.Children.Add(intro);
        string Ports(IEnumerable<int> ports) => ports.Any() ? string.Join(", ", ports.Select(p => "COM" + p)) : "—";
        body.Children.Add(DetailRow(Glyphs.Usb, L("Com aparelho instalado", "Owned by an installed device"), Ports(com.InUse)));
        body.Children.Add(DetailRow(Glyphs.Usb, L("Reservadas sem dono", "Reserved, no owner"), Ports(stale)));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var release = IconButton(Glyphs.Broom, L("Liberar portas sem uso", "Release unused ports"), primary: stale.Count > 0);
        release.IsEnabled = stale.Count > 0;
        release.Click += (_, _) =>
        {
            try
            {
                // Backup gravado antes de mexer: se não der para gravar, nada é alterado
                var backup = DeviceCleanupService.ReadComDb() ?? throw new InvalidOperationException(L("O Windows não tem o registro de portas COM.", "Windows has no COM port database."));
                var folder = Path.GetDirectoryName(ComBackupPath)!;
                RegistryTweakStore.EnsureProtectedDirectory(folder);
                File.WriteAllBytes(ComBackupPath, backup);
                DeviceCleanupService.ReleaseOrphanPorts();
                _log.Write("INFO", $"Portas COM liberadas: {Ports(stale)} (backup em {ComBackupPath})");
                ShowToast(L("Portas COM", "COM ports"), L($"{stale.Count} porta(s) liberada(s).", $"{stale.Count} port(s) released."));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or System.Security.SecurityException)
            {
                ShowToast(L("Portas COM", "COM ports"), ex.Message, "Danger");
            }
            ShowDevices();
        };
        buttons.Children.Add(release);
        if (File.Exists(ComBackupPath))
        {
            var undo = SecondaryButton(Glyphs.Undo, L("Desfazer última liberação", "Undo last release"));
            undo.Margin = new Thickness(8, 0, 0, 0);
            undo.Click += (_, _) =>
            {
                try
                {
                    DeviceCleanupService.RestoreComDb(File.ReadAllBytes(ComBackupPath));
                    File.Delete(ComBackupPath);
                    ShowToast(L("Portas COM", "COM ports"), L("Reservas restauradas.", "Reservations restored."));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or System.Security.SecurityException)
                {
                    ShowToast(L("Portas COM", "COM ports"), ex.Message, "Danger");
                }
                ShowDevices();
            };
            buttons.Children.Add(undo);
        }
        body.Children.Add(buttons);
        return Surface(body);
    }

    private void StartSystemTool(string file)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SystemTools.Resolve("mmc.exe"), file) { UseShellExecute = false }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException) { ShowToast(file, ex.Message, "Danger"); }
    }
}
