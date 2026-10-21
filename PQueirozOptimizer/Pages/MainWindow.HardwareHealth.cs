using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Saúde do hardware: erros de hardware, S.M.A.R.T. dos discos, bateria e o relatório do PC.</summary>
public partial class MainWindow
{
    private static string L(string pt, string en) => Translator.IsEnglish ? en : pt;

    private void ShowHardwareHealth()
    {
        PageTitle.Text = "Saúde do hardware";
        PageBadge.Visibility = Visibility.Collapsed;
        PageActions.Children.Clear();
        PageActions.Children.Add(RoundIcon(Glyphs.Refresh, "Ler de novo", () => NavigateTo("health")));
        var report = IconButton(Glyphs.Document, L("Exportar relatório", "Export report"));
        report.Margin = new Thickness(8, 0, 0, 0);
        report.Click += async (_, _) => await ExportReportAsync();
        PageActions.Children.Add(report);

        var root = new StackPanel();
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        var errorsHost = new StackPanel();
        var disksHost = new StackPanel();
        var batteryHost = new StackPanel();
        root.Children.Add(errorsHost);
        root.Children.Add(HomeBandTitle(L("Discos", "Disks")));
        root.Children.Add(disksHost);
        root.Children.Add(batteryHost);
        errorsHost.Children.Add(LoadingCard(L("Lendo o log do sistema…", "Reading the system log…")));
        disksHost.Children.Add(LoadingCard(L("Lendo o S.M.A.R.T. dos discos…", "Reading the disks' S.M.A.R.T.…")));

        _ = Task.Run(() => HardwareHealthService.ErrorsSince(DateTime.Now.AddDays(-30))).ContinueWith(t =>
        {
            if (!root.IsLoaded && root.Parent is null) return;
            errorsHost.Children.Clear();
            if (t.IsCompletedSuccessfully) errorsHost.Children.Add(HardwareErrorsCard(t.Result, 30));
        }, TaskScheduler.FromCurrentSynchronizationContext());

        _ = Task.Run(() => (Disks: HardwareHealthService.ReadDisks(), Batteries: HardwareHealthService.ReadBatteries())).ContinueWith(t =>
        {
            disksHost.Children.Clear();
            if (!t.IsCompletedSuccessfully) { disksHost.Children.Add(Label(t.Exception?.InnerException?.Message ?? "", 12.5, true)); return; }
            var grid = HomeGrid(360);
            grid.AllowedColumns = new[] { 1, 2 };
            grid.MaxColumns = 2;
            foreach (var card in t.Result.Disks) grid.Children.Add(HealthCardView(card, Glyphs.Drive));
            disksHost.Children.Add(grid);
            if (t.Result.Batteries.Count == 0) return;
            batteryHost.Children.Add(HomeBandTitle(L("Bateria", "Battery")));
            var batteries = HomeGrid(360);
            batteries.AllowedColumns = new[] { 1, 2 };
            batteries.MaxColumns = 2;
            foreach (var card in t.Result.Batteries) batteries.Children.Add(HealthCardView(card, Glyphs.Battery));
            batteryHost.Children.Add(batteries);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private Border LoadingCard(string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 80, Height = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
        var label = Label(text, 12.5, true); label.Margin = new Thickness(0); row.Children.Add(label);
        return Surface(row);
    }

    /// <summary>Erros WHEA e reinícios do driver de vídeo em um período: zero é o esperado num PC estável.</summary>
    private Border HardwareErrorsCard(HardwareErrors errors, int days)
    {
        var body = new StackPanel();
        var total = errors.Whea + errors.GpuResets;
        var tone = errors.Error != null ? "Muted" : total == 0 ? "Success" : errors.Whea > 0 ? "Danger" : "Warning";
        var status = errors.Error != null ? L("Sem leitura", "Not read") : total == 0 ? L("Nenhum erro", "No errors") : L($"{total} registro(s)", $"{total} entr(ies)");
        body.Children.Add(CardHeader(Glyphs.Shield, L($"ERROS DE HARDWARE · {days} DIAS", $"HARDWARE ERRORS · {days} DAYS"), L("Estabilidade", "Stability"), Pill(status, tone == "Muted" ? "Info" : tone), titleIsData: false));
        var explain = Label(errors.Error ?? (total == 0
            ? L("O Windows não registrou erros de CPU, memória ou PCIe (WHEA) nem reinícios do driver de vídeo. É o esperado num PC estável, inclusive depois de overclock ou undervolt.",
                "Windows logged no CPU, memory or PCIe errors (WHEA) and no graphics driver resets. That's what a stable PC looks like, including after an overclock or undervolt.")
            : L("Erros WHEA e reinícios do driver de vídeo indicam instabilidade mesmo sem tela azul: overclock, undervolt ou XMP/EXPO agressivos, temperatura alta ou fonte no limite. Volte o último ajuste e rode o Stress test de novo.",
                "WHEA errors and graphics driver resets point to instability even without a blue screen: aggressive overclock, undervolt or XMP/EXPO, high temperature or a PSU at its limit. Undo the last change and run the Stress test again.")), 12.5, true);
        explain.Tag = Translator.SystemDataTag;
        body.Children.Add(explain);
        if (errors.Error is null)
        {
            var stats = HomeGrid(160);
            stats.AllowedColumns = new[] { 1, 2 }; stats.MaxColumns = 2; stats.Margin = new Thickness(0, 6, 0, 0);
            stats.Children.Add(BigStat(errors.Whea.ToString(), L("Erros WHEA (CPU, memória, PCIe)", "WHEA errors (CPU, memory, PCIe)")));
            stats.Children.Add(BigStat(errors.GpuResets.ToString(), L("Reinícios do driver de vídeo", "Graphics driver resets")));
            body.Children.Add(stats);
            if (errors.Last is { } last)
            {
                var l = Label(L("Último: ", "Last: ") + last, 12, true); l.Tag = Translator.SystemDataTag; l.Margin = new Thickness(0, 12, 0, 0);
                body.Children.Add(l);
            }
        }
        var events = SecondaryButton(Glyphs.Document, L("Abrir o Visualizador de Eventos", "Open Event Viewer"));
        events.HorizontalAlignment = HorizontalAlignment.Left; events.Margin = new Thickness(0, 14, 0, 0);
        events.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SystemTools.Resolve("eventvwr.exe")) { UseShellExecute = false }); } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.FileNotFoundException) { ShowToast(L("Visualizador de Eventos", "Event Viewer"), ex.Message, "Danger"); } };
        body.Children.Add(events);
        return Surface(body);
    }

    /// <summary>Número grande em fonte mono com a legenda embaixo (estilo dos painéis de hardware).</summary>
    private static StackPanel BigStat(string value, string caption)
    {
        var panel = new StackPanel();
        var v = new TextBlock { Text = value, FontSize = 28, FontWeight = FontWeights.SemiBold, Tag = Translator.SystemDataTag };
        v.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var c = new TextBlock { Text = caption, FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = Translator.SystemDataTag };
        c.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(v); panel.Children.Add(c);
        return panel;
    }

    private static string VerdictTone(HealthVerdict verdict) => verdict switch
    {
        HealthVerdict.Good => "Success", HealthVerdict.Warning => "Warning", HealthVerdict.Critical => "Danger", _ => "Info",
    };

    /// <summary>Card de componente: nome, resumo, veredito, motivos e as leituras em duas colunas separadas por fios.</summary>
    private Border HealthCardView(HealthCard card, string glyph)
    {
        var body = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var pill = Pill(SystemReportService.VerdictText(card.Verdict), VerdictTone(card.Verdict));
        pill.Tag = Translator.SystemDataTag;
        pill.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(pill, Dock.Right); head.Children.Add(pill);
        var chip = IconChip(glyph, "Text", 38); chip.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(chip, Dock.Left); head.Children.Add(chip);
        var titles = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var t = Label(card.Title, 14); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0); t.Tag = Translator.SystemDataTag;
        titles.Children.Add(t);
        if (card.Meta.Length > 0) { var m = Label(card.Meta, 12, true); m.Margin = new Thickness(0, 2, 0, 0); m.Tag = Translator.SystemDataTag; titles.Children.Add(m); }
        head.Children.Add(titles);
        body.Children.Add(head);
        var reasons = Label(string.Join(" ", card.Reasons), 12.5); reasons.Tag = Translator.SystemDataTag; reasons.Margin = new Thickness(0, 0, 0, 10);
        body.Children.Add(reasons);
        var metrics = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var (key, value) in card.Metrics)
        {
            var cell = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 14, 8) };
            cell.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
            var stack = new StackPanel();
            var k = new TextBlock { Text = key, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Tag = Translator.SystemDataTag };
            k.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var v = new TextBlock { Text = value, FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = value, Tag = Translator.SystemDataTag };
            v.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            stack.Children.Add(k); stack.Children.Add(v);
            cell.Child = stack;
            metrics.Children.Add(cell);
        }
        body.Children.Add(metrics);
        return HomeCard(body);
    }

    private async Task ExportReportAsync()
    {
        OperationStatus.Text = L("Montando o relatório do PC…", "Building the PC report…");
        try
        {
            var path = await Task.Run(() => SystemReportService.Save(AppVersion));
            OperationStatus.Text = L("Relatório salvo em ", "Report saved to ") + path;
            ShowToast(L("Relatório do PC", "PC report"), L("Salvo em Documentos\\Qrztweaks.", "Saved to Documents\\Qrztweaks."));
            // Abre pelo Explorer: o navegador sobe com as permissões normais do usuário, não como administrador
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{path}\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowToast(L("Relatório do PC", "PC report"), ex.Message, "Danger");
        }
    }
}
