using System.IO;
using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Engine;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Perfis personalizados (fase 15): criar, editar, duplicar, excluir, exportar, importar, aplicar e restaurar.
/// Ativação por jogo: ao abrir um processo do perfil, liga o Modo Jogo temporário e restaura quando o jogo fecha.
/// </summary>
public partial class MainWindow
{
    private UserProfile? _editingProfile;
    private string[]? _smartProfileSelection;
    private GameWatcher? _gameWatcher;
    private string? _autoSessionProfile;

    private void ShowProfiles()
    {
        PageTitle.Text = "Perfis";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        if (_editingProfile is { } editing) { root.Children.Add(ProfileEditor(editing)); ContentHost.Children.Clear(); ContentHost.Children.Add(root); return; }

        var head = new StackPanel();
        var top = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var create = IconButton(Glyphs.Add, "Novo perfil", primary: true);
        create.Click += (_, _) => { _editingProfile = new UserProfile { Name = "Meu perfil" }; ShowProfiles(); };
        var import = IconButton(Glyphs.Upload, "Importar"); import.Margin = new Thickness(8, 0, 0, 0);
        import.Click += (_, _) => ImportProfile();
        buttons.Children.Add(import); buttons.Children.Add(create);
        DockPanel.SetDock(buttons, Dock.Right); top.Children.Add(buttons);
        top.Children.Add(SectionHeader("Perfis personalizados", "Cada perfil guarda um objetivo, os ajustes do catálogo e os jogos. Perfis só contêm dados: nenhum script é salvo ou executado a partir deles."));
        head.Children.Add(top);
        var watch = new CheckBox { IsChecked = ProfileStore.Default.WatchGames, Content = "" };
        watch.SetResourceReference(StyleProperty, "SwitchCheckBox");
        watch.Checked += (_, _) => { ProfileStore.Default.WatchGames = true; StartGameWatcher(); };
        watch.Unchecked += (_, _) => { ProfileStore.Default.WatchGames = false; StopGameWatcher(); };
        head.Children.Add(SettingRow("Ativar ao abrir um jogo", "Quando um jogo de um perfil com \"Modo Jogo automático\" abrir, o Modo Jogo temporário liga sozinho e é restaurado quando o jogo fechar. O app só lê a lista de processos: nada é injetado no jogo.", watch));
        root.Children.Add(Surface(head));

        foreach (var profile in ProfileStore.Default.Profiles)
        {
            var panel = new StackPanel();
            var line = new DockPanel();
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            void Act(string glyph, string text, bool primary, Action click) { var b = IconButton(glyph, text, primary); b.Margin = new Thickness(6, 0, 0, 6); b.Click += (_, _) => click(); actions.Children.Add(b); }
            Act(Glyphs.Lightning, "Aplicar", true, () => ApplyProfile(profile));
            Act(Glyphs.Edit, "Editar", false, () => { _editingProfile = profile; ShowProfiles(); });
            Act(Glyphs.Copy, "Duplicar", false, () => { ProfileStore.Default.Duplicate(profile.Id); ShowProfiles(); });
            Act(Glyphs.Save, "Exportar", false, () => ExportProfile(profile));
            Act(Glyphs.Delete, "Excluir", false, () =>
            {
                if (Msg($"Excluir o perfil \"{profile.Name}\"? Os ajustes já aplicados continuam no backup.", "Perfis", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                ProfileStore.Default.Delete(profile.Id); ShowProfiles();
            });
            DockPanel.SetDock(actions, Dock.Right); line.Children.Add(actions);
            var text = new StackPanel();
            var name = Label(profile.Name, 14); name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0, 0, 0, 2); name.Tag = Translator.SystemDataTag;
            text.Children.Add(name);
            if (profile.Description.Length > 0) text.Children.Add(Label(profile.Description, 12.5, true));
            var pills = new WrapPanel();
            void P(string t, string tone) { var p = Pill(t, tone); p.Margin = new Thickness(0, 0, 6, 4); pills.Children.Add(p); }
            P(GoalTitle(profile.Goal), "Accent");
            P($"{profile.Tweaks.Length} ajustes", "Info");
            if (profile.Processes.Length > 0) { var procs = Pill(string.Join(", ", profile.Processes), "Info"); procs.Margin = new Thickness(0, 0, 6, 4); ((TextBlock)procs.Child).Tag = Translator.SystemDataTag; pills.Children.Add(procs); }
            if (profile.AutoGameMode) P("Modo Jogo automático", "Success");
            text.Children.Add(pills);
            line.Children.Add(text);
            panel.Children.Add(line);
            root.Children.Add(Surface(panel));
        }
        var restore = new StackPanel();
        restore.Children.Add(SectionHeader("Restaurar", "Os ajustes de um perfil são aplicados pelo Smart Optimize, com backup de cada item. Para desfazer, use Atividade e reversão."));
        var go = IconButton(Glyphs.Undo, "Abrir Atividade e reversão"); go.HorizontalAlignment = HorizontalAlignment.Left; go.Click += History_Click;
        restore.Children.Add(go);
        root.Children.Add(Surface(restore));
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
    }

    private Border ProfileEditor(UserProfile profile)
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader(ProfileStore.Default.Profiles.Any(p => p.Id == profile.Id) ? "Editar perfil" : "Novo perfil"));
        var name = new TextBox { Text = profile.Name, MaxLength = ProfileValidation.MaxName, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(name, "Nome do perfil");
        panel.Children.Add(SettingRow("Nome", "Até 60 caracteres.", name));
        var description = new TextBox { Text = profile.Description, MaxLength = ProfileValidation.MaxDescription, Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(description, "Descrição");
        panel.Children.Add(SettingRow("Descrição", "Opcional.", description));
        var goal = new ComboBox { Width = 220 };
        foreach (var g in Goals) goal.Items.Add(new ComboBoxItem { Content = g.Title, Tag = g.Goal });
        goal.SelectedIndex = Array.FindIndex(Goals, g => g.Goal == profile.Goal);
        panel.Children.Add(SettingRow("Objetivo", "Usado nas recomendações do Smart Optimize.", goal));
        var processes = new TextBox { Text = string.Join(", ", profile.Processes), Width = 420, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Ex.: cs2.exe, VALORANT-Win64-Shipping.exe" };
        System.Windows.Automation.AutomationProperties.SetName(processes, "Processos dos jogos");
        panel.Children.Add(SettingRow("Processos dos jogos", "Nomes dos executáveis, separados por vírgula.", processes));
        CheckBox Option(string title, string hint, bool value) { var c = new CheckBox { IsChecked = value, Content = "" }; c.SetResourceReference(StyleProperty, "SwitchCheckBox"); panel.Children.Add(SettingRow(title, hint, c)); return c; }
        var auto = Option("Modo Jogo automático", "Liga o Modo Jogo temporário quando um dos jogos abrir (requer \"Ativar ao abrir um jogo\").", profile.AutoGameMode);
        var close = Option("Fechar programas em segundo plano", "No Modo Jogo automático, fecha os programas da lista do Modo Jogo que estiverem abertos.", profile.CloseBackgroundApps);
        var pause = Option("Pausar serviços", "Pausa os serviços marcados por padrão no Modo Jogo.", profile.PauseServices);
        var plan = Option("Plano de alto desempenho", "Troca o plano de energia durante o jogo.", profile.HighPerformancePlan);
        var purge = Option("Liberar memória em espera", "Limpa a memória em espera ao iniciar.", profile.PurgeStandby);

        panel.Children.Add(Label("Ajustes do perfil", 13.5));
        var checks = new List<(string Id, CheckBox Box)>();
        foreach (var group in TweakCatalog.All.GroupBy(t => t.Category))
        {
            var g = Label(group.Key, 12.5, true); g.Margin = new Thickness(0, 6, 0, 2); panel.Children.Add(g);
            var wrap = new WrapPanel();
            foreach (var t in group)
            {
                var box = new CheckBox { Content = t.Name, IsChecked = profile.Tweaks.Contains(t.Id), Margin = new Thickness(0, 0, 16, 6), ToolTip = t.Description };
                checks.Add((t.Id, box)); wrap.Children.Add(box);
            }
            panel.Children.Add(wrap);
        }
        var save = IconButton(Glyphs.Save, "Salvar", primary: true);
        var cancel = IconButton(Glyphs.Cancel, "Cancelar"); cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => { _editingProfile = null; ShowProfiles(); };
        save.Click += (_, _) =>
        {
            var procs = processes.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : p + ".exe").ToArray();
            var invalid = procs.Where(p => !ProfileValidation.IsValidProcess(p)).ToList();
            if (invalid.Count > 0) { ShowToast("Perfis", "Nome de processo inválido: " + string.Join(", ", invalid), "Warning"); return; }
            if (string.IsNullOrWhiteSpace(name.Text)) { ShowToast("Perfis", "Dê um nome ao perfil.", "Warning"); return; }
            ProfileStore.Default.Upsert(profile with
            {
                Name = name.Text.Trim(), Description = description.Text.Trim(), Goal = (OptimizationGoal)((ComboBoxItem)goal.SelectedItem).Tag, Processes = procs,
                Tweaks = checks.Where(c => c.Box.IsChecked == true).Select(c => c.Id).ToArray(),
                AutoGameMode = auto.IsChecked == true, CloseBackgroundApps = close.IsChecked == true, PauseServices = pause.IsChecked == true,
                HighPerformancePlan = plan.IsChecked == true, PurgeStandby = purge.IsChecked == true,
            });
            _editingProfile = null;
            ShowToast("Perfis", "Perfil salvo.", "Success");
            ShowProfiles();
        };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        bar.Children.Add(save); bar.Children.Add(cancel);
        panel.Children.Add(bar);
        return Surface(panel);
    }

    private void ExportProfile(UserProfile profile)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = string.Concat(profile.Name.Split(Path.GetInvalidFileNameChars())) + ".qrzprofile.json", Filter = "Perfil do Qrztweaks|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, ProfileValidation.Export(profile)); ShowToast("Perfil exportado", dialog.FileName, "Success"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowToast("Exportação", ex.Message, "Danger"); }
    }

    private void ImportProfile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Perfil do Qrztweaks|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var (profile, warnings) = ProfileValidation.Import(dialog.FileName);
            ProfileStore.Default.Upsert(profile);
            ShowToast("Perfil importado", warnings.Count == 0 ? profile.Name : profile.Name + " · " + string.Join(" ", warnings), warnings.Count == 0 ? "Success" : "Warning");
            ShowProfiles();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { ShowToast("Importação", ex.Message, "Danger"); }
    }

    /// <summary>Aplica pelo Smart Optimize: a análise lê o estado e só os ajustes do perfil que fazem sentido ficam marcados.</summary>
    private void ApplyProfile(UserProfile profile)
    {
        _smartGoal = profile.Goal == OptimizationGoal.Custom ? OptimizationGoal.Custom : profile.Goal;
        _smartProfileSelection = profile.Tweaks;
        NavigateTo("smart");
        _ = RunSmartAnalysisAsync(rereadSystem: _smartReading is null);
    }

    // ---------- Ativação automática por jogo ----------
    private void StartGameWatcher()
    {
        if (_gameWatcher is not null || !PageAllowed("gaming")) return;
        _gameWatcher = new GameWatcher();
        _gameWatcher.GameStarted += profile => Dispatcher.BeginInvoke(async () => await OnProfileGameStarted(profile));
        _gameWatcher.GameExited += id => Dispatcher.BeginInvoke(async () => await OnProfileGameExited(id));
    }

    private void StopGameWatcher() { _gameWatcher?.Dispose(); _gameWatcher = null; }

    private async Task OnProfileGameStarted(UserProfile profile)
    {
        if (GamingService.ActiveSession() is not null || _operationRunning || _autoSessionProfile is not null) return; // sessão manual em andamento: não mexe
        // A posse é registrada antes de esperar: se o jogo fechar enquanto o Modo Jogo liga, a saída não se perde
        _autoSessionProfile = profile.Id;
        _autoSessionStarting = true;
        _autoSessionExitPending = false;
        try
        {
            var apps = profile.CloseBackgroundApps ? GamingService.RunningBackgroundApps().ToList() : new List<string>();
            var services = profile.PauseServices ? GamingService.PausableServices.Where(s => s.Default).Select(s => s.Name).ToList() : new List<string>();
            await Gaming.StartSessionAsync(apps, services, profile.HighPerformancePlan, profile.PurgeStandby);
            UpdateGameModeBadge();
            _log.Write("SUCCESS", $"Perfil {profile.Name}: Modo Jogo ativado ao abrir o jogo");
            ShowToast(profile.Name, "Modo Jogo temporário ativado. Ele é restaurado quando o jogo fechar.", "Success");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _log.Write("WARN", $"Perfil {profile.Name}: não foi possível ativar o Modo Jogo: {ex.Message}");
        }
        finally { _autoSessionStarting = false; }
        // O jogo fechou durante a ativação: restaura agora (a sessão pode ter sido criada parcialmente)
        if (_autoSessionExitPending || GamingService.ActiveSession() is null)
        {
            if (GamingService.ActiveSession() is not null) await OnProfileGameExited(profile.Id);
            else _autoSessionProfile = null;
        }
    }

    private bool _autoSessionStarting, _autoSessionExitPending;

    private async Task OnProfileGameExited(string profileId)
    {
        if (_autoSessionProfile != profileId) return; // só restaura o que ele mesmo ativou
        if (_autoSessionStarting) { _autoSessionExitPending = true; return; } // termina quando a ativação acabar
        if (GamingService.ActiveSession() is null) { _autoSessionProfile = null; return; }
        try
        {
            var restored = await Gaming.EndSessionAsync();
            _autoSessionProfile = null;
            UpdateGameModeBadge();
            _log.Write("SUCCESS", $"Jogo fechado: Modo Jogo restaurado ({restored} item(ns))");
            ShowToast("Modo Jogo restaurado", "O jogo fechou e os ajustes temporários foram desfeitos.", "Success");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _log.Write("WARN", "Não foi possível restaurar o Modo Jogo automaticamente: " + ex.Message);
        }
    }
}
