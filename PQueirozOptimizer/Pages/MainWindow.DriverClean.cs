using System.IO;
using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Instalação limpa do driver de vídeo com o DDU.</summary>
public partial class MainWindow
{
    private string? _cleanInstaller;

    private Border DriverCleanCard()
    {
        var gpus = DriverCleanService.DetectGpus();
        var gpu = gpus.FirstOrDefault(g => g.Vendor != GpuVendor.Intel);
        if (gpu == default) gpu = gpus.FirstOrDefault();
        var vendor = gpu.Vendor;
        var ddu = DriverCleanService.DduPath();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        left.Children.Add(FeatureHeader(Glyphs.Refresh, "Accent", "Instalação limpa de driver (DDU)",
            "Remove completamente o driver de vídeo atual com o Display Driver Uninstaller, reinicia e abre o instalador do driver novo assim que você entrar no Windows. Resolve travadas, telas pretas e restos de drivers antigos.",
            ddu is null ? "DDU será instalado" : "DDU instalado", ddu is null ? "Info" : "Success"));
        var gpuLine = Label(gpu == default ? "Nenhuma placa de vídeo reconhecida." : $"Placa detectada: {gpu.Name}", 12.5);
        gpuLine.Margin = new Thickness(0, 14, 0, 0); gpuLine.FontWeight = FontWeights.SemiBold;
        left.Children.Add(gpuLine);
        grid.Children.Add(left);

        // Passo a passo à direita
        var steps = new StackPanel();
        var stepItems = new[]
        {
            ("1", "Baixe o driver novo", "No site oficial da fabricante da sua placa."),
            ("2", "Escolha o instalador", _cleanInstaller is null ? "O arquivo .exe que você baixou." : Path.GetFileName(_cleanInstaller)),
            ("3", "Remova e reinstale", "Ponto de restauração, DDU, reinício e instalação."),
        };
        foreach (var (number, title, detail) in stepItems)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var badge = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            badge.SetResourceReference(Border.BackgroundProperty, number == "2" && _cleanInstaller != null ? "SuccessBrush" : "AccentGradientBrush");
            badge.Child = new TextBlock { Text = number == "2" && _cleanInstaller != null ? "✓" : number, Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Tag = Translator.SystemDataTag };
            DockPanel.SetDock(badge, Dock.Left); row.Children.Add(badge);
            var text = new StackPanel();
            var t = Label(title, 13); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0);
            var d = Label(detail, 11.5, true); d.Margin = new Thickness(0, 1, 0, 0);
            if (number == "2" && _cleanInstaller != null) d.Tag = Translator.SystemDataTag;
            text.Children.Add(t); text.Children.Add(d);
            row.Children.Add(text);
            steps.Children.Add(row);
        }
        Grid.SetColumn(steps, 2);
        grid.Children.Add(steps);

        var panel = new StackPanel();
        panel.Children.Add(grid);
        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var site = IconButton(Glyphs.OpenInNew, "Baixar driver oficial");
        site.Click += (_, _) => OpenUrl(DriverCleanService.DriverPage(vendor));
        var pick = IconButton(Glyphs.Folder, _cleanInstaller is null ? "Escolher instalador" : "Trocar instalador");
        pick.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Instalador do driver (*.exe)|*.exe", Title = "Escolha o instalador do driver novo" };
            if (dialog.ShowDialog(this) == true) { _cleanInstaller = dialog.FileName; ShowDrivers(); }
        };
        var run = IconButton(Glyphs.Play, "Iniciar instalação limpa", primary: _cleanInstaller != null);
        run.IsEnabled = _cleanInstaller != null && vendor != GpuVendor.Unknown;
        run.Click += async (_, _) =>
        {
            if (Msg($"O driver de vídeo atual será removido e o PC vai reiniciar sozinho. Depois de entrar no Windows, o instalador \"{Path.GetFileName(_cleanInstaller)}\" abre automaticamente.\n\nSalve seus trabalhos e feche os programas. Continuar?",
                    "Instalação limpa de driver", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var progress = new Progress<string>(line => { OperationStatus.Text = line; _log.Write("INFO", line); });
            await ExecuteTrackedAsync("Instalação limpa de driver", async _ => await new DriverCleanService(_log).RunCleanInstallAsync(vendor, _cleanInstaller!, progress));
        };
        actions.Children.Add(site); actions.Children.Add(pick); actions.Children.Add(run);
        panel.Children.Add(actions);

        var card = Surface(panel);
        card.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        return card;
    }
}
