using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Personalizar Windows: preferências do sistema com liga/desliga, aplicadas na hora.</summary>
public partial class MainWindow
{
    private WindowsCustomizeService? _customize;
    private WindowsCustomizeService Customize => _customize ??= new WindowsCustomizeService(_log);

    private void ShowCustomize()
    {
        PageTitle.Text = "Personalizar Windows";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        var settings = Customize.Settings;

        // Aviso para reiniciar o Explorer: só aparece depois de mudar algo que precisa dele
        var restartBar = new DockPanel();
        var restart = IconButton(Glyphs.Refresh, "Reiniciar o Explorer", primary: true);
        DockPanel.SetDock(restart, Dock.Right); restartBar.Children.Add(restart);
        var restartIcon = GlyphIcon(Glyphs.Info, 14, "AccentBrush"); restartIcon.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(restartIcon, Dock.Left); restartBar.Children.Add(restartIcon);
        var restartText = Label("Algumas mudanças só aparecem depois de reiniciar o Explorer (a barra de tarefas some e volta; janelas de pastas abertas fecham).", 12.5, true);
        restartText.Margin = new Thickness(0); restartText.VerticalAlignment = VerticalAlignment.Center;
        restartBar.Children.Add(restartText);
        var restartCard = Surface(restartBar); restartCard.Visibility = Visibility.Collapsed;
        restart.Click += (_, _) => { Customize.RestartExplorer(); restartCard.Visibility = Visibility.Collapsed; ShowToast("Personalizar Windows", "Explorer reiniciado: a barra de tarefas volta em alguns segundos."); };

        var switches = new List<(WindowsSetting Setting, CheckBox Check)>();
        var applying = false;
        // Uma alteração por vez, fora da thread da tela: o aviso WM_SETTINGCHANGE para todas as janelas
        // pode levar até 2 s e, na thread da tela, congelava a página e a animação do interruptor.
        // O interruptor fica bloqueado até gravar, para uma falha não desfazer um clique mais novo.
        async Task<bool> Set(WindowsSetting setting, CheckBox check, bool on)
        {
            if (check.Tag is true) return false;
            check.Tag = true;
            var wasFocusable = check.Focusable;
            check.IsHitTestVisible = false;
            check.Focusable = false;
            Interlocked.Increment(ref _pendingSystemMutations);
            await _systemTweakGate.WaitAsync();
            try
            {
                await Task.Run(() => Customize.Apply(setting, on));
                applying = true;
                try { check.IsChecked = setting.Read(); }
                finally { applying = false; }
                if (setting.NeedsExplorerRestart && restartCard.Visibility != Visibility.Visible) ShowKeepingScroll(restartCard);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
            {
                _log.Write("ERROR", $"Personalizar Windows: {setting.Title}: {ex.Message}");
                ShowToast("Não foi possível alterar", $"{setting.Title}: {ex.Message}", "Danger");
                applying = true; check.IsChecked = !on; applying = false;
                return false;
            }
            finally
            {
                _systemTweakGate.Release();
                check.IsHitTestVisible = true;
                check.Focusable = wasFocusable;
                check.Tag = false;
                Interlocked.Decrement(ref _pendingSystemMutations);
            }
            return true;
        }

        // Cabeçalho com o botão de aplicar todos os recomendados
        var head = new DockPanel();
        var recommended = IconButton(Glyphs.Check, "Aplicar recomendados", primary: true);
        recommended.VerticalAlignment = VerticalAlignment.Center; recommended.Margin = new Thickness(16, 0, 0, 0);
        DockPanel.SetDock(recommended, Dock.Right); head.Children.Add(recommended);
        head.Children.Add(SectionHeader("Ajustes do dia a dia, aplicados na hora",
            "Cada interruptor mostra como o Windows está agora. As mudanças não passam pela revisão de otimizações: para voltar, é só desligar de novo. As marcadas como recomendadas deixam o Windows mais prático e melhor para jogos."));
        var headCard = Surface(head); headCard.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "HeroBrush");
        root.Children.Add(headCard);
        root.Children.Add(restartCard);

        foreach (var group in settings.GroupBy(s => s.Category))
        {
            var panel = new StackPanel();
            panel.Children.Add(SectionHeader(group.Key));
            foreach (var setting in group)
            {
                bool current;
                try { current = setting.Read(); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { current = false; }
                var check = new CheckBox { IsChecked = current, Content = "" };
                check.SetResourceReference(StyleProperty, "SwitchCheckBox");
                check.Checked += (_, _) => { if (!applying) _ = Set(setting, check, true); };
                check.Unchecked += (_, _) => { if (!applying) _ = Set(setting, check, false); };
                switches.Add((setting, check));

                var control = new StackPanel { Orientation = Orientation.Horizontal };
                if (setting.Recommended is { } rec)
                {
                    var pill = Pill(rec ? "Recomendado: ligado" : "Recomendado: desligado", "Accent");
                    pill.Margin = new Thickness(0, 0, 12, 0);
                    control.Children.Add(pill);
                }
                control.Children.Add(check);
                panel.Children.Add(SettingRow(setting.Title, setting.Description, control));
            }
            root.Children.Add(Surface(panel));
        }

        recommended.Click += async (_, _) =>
        {
            var pending = switches.Where(s => s.Setting.Recommended is { } rec && s.Check.IsChecked != rec).ToList();
            if (pending.Count == 0) { ShowToast("Personalizar Windows", "Tudo já está como recomendado."); return; }
            var list = string.Join("\n", pending.Select(p => $"• {p.Setting.Title}: {(p.Setting.Recommended == true ? "ligar" : "desligar")}"));
            if (Msg($"Estas opções vão mudar:\n\n{list}\n\nContinuar?", "Aplicar recomendados", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            recommended.IsHitTestVisible = false;
            var success = 0;
            try
            {
                foreach (var (setting, check) in pending)
                {
                    applying = true;
                    check.IsChecked = setting.Recommended;
                    applying = false;
                    if (await Set(setting, check, setting.Recommended!.Value)) success++;
                }
            }
            finally { recommended.IsHitTestVisible = true; }
            ShowToast("Personalizar Windows", $"{success} de {pending.Count} opções ajustadas para o recomendado.",
                success == pending.Count ? "Success" : "Warning");
        };

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }
}
