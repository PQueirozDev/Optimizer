using System.IO;
using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Configurações → Preferências: frequência do monitor ao vivo, avisos de sucesso e exportar/importar as
/// preferências. Importar só lê dados (aparência, modos, tutoriais); nada vindo do arquivo é executado.
/// </summary>
public partial class MainWindow
{
    private Border PreferencesSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Preferências", "Monitoramento, avisos e uma cópia das suas preferências para outro PC ou para depois de reinstalar."));

        if (_configService.LastLoadError is { } loadError)
        {
            var warning = Label("As preferências salvas não puderam ser lidas na abertura: " + loadError, 12.5);
            warning.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
            warning.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(warning);
        }

        // Frequência do monitor ao vivo
        var interval = new ComboBox { Width = 170, Height = 34 };
        foreach (var seconds in HardwareMonitorService.AllowedIntervals)
            interval.Items.Add(new ComboBoxItem { Content = Tr(seconds switch { 1 => "A cada 1 s (padrão)", 2 => "A cada 2 s", _ => "A cada 5 s (economia)" }), Tag = seconds });
        interval.SelectedIndex = Math.Max(0, Array.IndexOf(HardwareMonitorService.AllowedIntervals, HardwareMonitorService.Shared.IntervalSeconds));
        interval.SelectionChanged += (_, _) =>
        {
            if (interval.SelectedItem is not ComboBoxItem { Tag: int seconds }) return;
            HardwareMonitorService.Shared.IntervalSeconds = seconds;
            try { _configService.SaveMonitorInterval(seconds); }
            catch (IOException ex) { ShowToast("Preferências", ex.Message, "Warning"); }
        };
        panel.Children.Add(SettingRow("Atualização do monitor ao vivo", "Com que frequência CPU, GPU, memória e rede são lidas. Intervalos maiores usam menos CPU. O monitor já para sozinho com a janela minimizada.", interval));

        // Avisos de sucesso
        var toasts = new CheckBox { IsChecked = _configService.Config.SuccessNotifications, Content = "" };
        toasts.SetResourceReference(StyleProperty, "SwitchCheckBox");
        void SaveToasts(bool enabled)
        {
            try { _configService.SaveSuccessNotifications(enabled); }
            catch (IOException ex) { ShowToast("Preferências", ex.Message, "Warning"); }
        }
        toasts.Checked += (_, _) => SaveToasts(true);
        toasts.Unchecked += (_, _) => SaveToasts(false);
        panel.Children.Add(SettingRow("Avisos de sucesso", "Mostra um aviso no canto da tela quando uma ação termina bem. Erros e alertas aparecem sempre.", toasts));

        // Exportar e importar
        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var export = IconButton(Glyphs.Download, "Exportar preferências");
        export.Margin = new Thickness(0, 0, 8, 8);
        export.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "qrztweaks-preferencias.json", Filter = "JSON (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { _configService.ExportTo(dialog.FileName); ShowToast("Preferências", "Preferências exportadas.", "Success"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowToast("Preferências", ex.Message, "Danger"); }
        };
        var import = IconButton(Glyphs.Folder, "Importar preferências");
        import.Margin = new Thickness(0, 0, 8, 8);
        import.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            if (Msg("Trocar as preferências atuais (aparência, modos, tutoriais e monitor) pelas do arquivo? Nenhuma otimização é aplicada.", "Importar preferências",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try
            {
                _configService.ImportFrom(dialog.FileName);
                HardwareMonitorService.Shared.IntervalSeconds = _configService.Config.MonitorIntervalSeconds;
                AppearanceService.Apply(ConfigService.EffectiveAppearance(_configService.Config));
                _log.Write("SUCCESS", "Preferências importadas de " + Path.GetFileName(dialog.FileName));
                ShowToast("Preferências", "Preferências importadas.", "Success");
                UpdateNavBadges();
                ShowSettings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                ShowToast("Importar preferências", ex.Message, "Danger");
            }
        };
        actions.Children.Add(export); actions.Children.Add(import);
        panel.Children.Add(actions);
        var card = Surface(panel);
        card.Margin = new Thickness(0, 0, 0, 24);
        return card;
    }
}
