using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.BiosAdvisor.Detector;
using PQueirozOptimizer.Engine;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// QRZTWEAKS COMMAND CENTER: o que está ativo, a última análise, o último teste, alertas e ações rápidas.
/// Só mostra o que foi lido de verdade; o que não existe aparece como "sem dados", nunca como número inventado.
/// </summary>
public partial class MainWindow
{
    internal static string GoalTitle(OptimizationGoal goal) => Goals.First(g => g.Goal == goal).Title;

    private CompactMonitorWindow? _compact;

    /// <summary>Abre (ou traz para frente) a janela do modo compacto. Ela fecha junto com o app.</summary>
    private void OpenCompactMonitor()
    {
        if (_compact is { IsVisible: true }) { _compact.Activate(); return; }
        _compact = new CompactMonitorWindow(() => _capture?.Samples.LastOrDefault()?.Fps,
            () => SmartHistory.Default.LastAnalysis?.Goal is { } g ? Translator.Tr(GoalTitle(g)) : Translator.Tr("nenhum objetivo analisado"));
        _compact.Closed += (_, _) => _compact = null;
        _compact.Show();
    }

    /// <summary>Alertas atuais, do mais importante para o menos.</summary>
    private List<(string Text, string Tone, string? Page)> CommandAlerts(SystemSnapshot snapshot)
    {
        var alerts = new List<(string, string, string?)>();
        if (GamingService.ActiveSession() is { RecoveryPending: true }) alerts.Add(("Restauração do Modo Jogo pendente", "Danger", "gaming"));
        if (snapshot.FreePercent < 10) alerts.Add(($"Pouco espaço livre no disco ({snapshot.FreePercent:0}%)", "Warning", "dashboard"));
        if (CurrentLicense is { IsExpiringSoon: true } l) alerts.Add((l.DaysLeft == 0 ? "A licença vence hoje" : $"A licença vence em {l.DaysLeft} dia(s)", "Warning", "settings"));
        if (_pendingUpdate is { } u) alerts.Add(($"Atualização disponível: {u.LatestVersion}", "Info", "settings"));
        if (GamingService.ActiveSession() is { RecoveryPending: false }) alerts.Add(("Modo Jogo ativo", "Info", "gaming"));
        return alerts;
    }

    /// <summary>Aviso do modo demonstração: o que dá para ver e como ativar.</summary>
    private Border DemoBanner()
    {
        var dock = new DockPanel();
        var activate = IconButton(Glyphs.Key, "Ativar licença", primary: true);
        activate.Margin = new Thickness(12, 0, 0, 0);
        activate.Click += (_, _) => ActivateAdminLicense();
        DockPanel.SetDock(activate, Dock.Right); dock.Children.Add(activate);
        var chip = IconChip(Glyphs.Info, "Info", 40); DockPanel.SetDock(chip, Dock.Left); dock.Children.Add(chip);
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var t = Label("Modo demonstração", 14); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0);
        text.Children.Add(t);
        var d = Label("Veja o hardware, o monitor, o diagnóstico, o Performance Lab e as recomendações do Smart Optimize. Para aplicar qualquer ajuste, ative uma licença.", 12, true);
        d.Margin = new Thickness(0, 2, 0, 0); text.Children.Add(d);
        dock.Children.Add(text);
        var card = Surface(dock);
        card.SetResourceReference(Border.BorderBrushProperty, "InfoBrush");
        return card;
    }

    private UIElement CommandStatusRow(SystemSnapshot snapshot)
    {
        var grid = Responsive(new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -14, 2) }, 210, 4);
        var analysis = SmartHistory.Default.LastAnalysis;
        var lastRun = SmartHistory.Default.Runs.FirstOrDefault();
        var lastTest = PerfLabStore.Default.List().FirstOrDefault();
        var alerts = CommandAlerts(snapshot);
        Border Clickable(Border card, string page, string hint)
        {
            card.Cursor = System.Windows.Input.Cursors.Hand; card.ToolTip = hint;
            card.MouseLeftButtonUp += (_, _) => NavigateTo(page);
            return card;
        }
        grid.Children.Add(Clickable(Card("OTIMIZAÇÕES ATIVAS", analysis is null ? "Analise o PC para ver" : $"{analysis.Applied} ajustes do catálogo aplicados" + (lastRun is null ? "" : $" · {lastRun.Verified.Length} confirmados na última execução"), Glyphs.Lightning, "AccentBrush"), "smart", "Abrir o Smart Optimize"));
        grid.Children.Add(Clickable(Card("ÚLTIMA ANÁLISE", analysis is null ? "Nenhuma ainda" : $"{analysis.At:dd/MM HH:mm} · {GoalTitle(analysis.Goal)} · {analysis.Recommended} recomendados", Glyphs.Search, "InfoBrush"), "smart", "Abrir o Smart Optimize"));
        grid.Children.Add(Clickable(Card("ÚLTIMO TESTE", lastTest is null ? "Nenhum teste no Performance Lab" : $"{lastTest.StartedAt:dd/MM HH:mm} · " + (lastTest.HasFrames ? $"{lastTest.AverageFps:0} FPS · 1% low {lastTest.Low1Fps:0}" : Translator.Tr("só sensores")), Glyphs.Speed, "SuccessBrush"), "perflab", "Abrir o Performance Lab"));
        var alertCard = Card("ALERTAS", alerts.Count == 0 ? "Nenhum alerta" : $"{alerts.Count} · {alerts[0].Text}", alerts.Count == 0 ? Glyphs.Check : Glyphs.Warning, alerts.Count == 0 ? "SuccessBrush" : alerts[0].Tone + "Brush");
        if (alerts.Count > 0) alertCard.ToolTip = string.Join("\n", alerts.Select(a => "• " + Translator.Tr(a.Text)));
        if (alerts.FirstOrDefault().Page is { } page && page != "dashboard") { alertCard.Cursor = System.Windows.Input.Cursors.Hand; alertCard.MouseLeftButtonUp += (_, _) => NavigateTo(page); }
        grid.Children.Add(alertCard);
        return grid;
    }

    private Border QuickActionsPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Ações rápidas"));
        var row = new WrapPanel();
        void Add(string glyph, string text, bool primary, string tooltip, Action click)
        {
            var b = IconButton(glyph, text, primary); b.Margin = new Thickness(0, 0, 8, 8); b.ToolTip = tooltip; b.IsEnabled = !_operationRunning;
            b.Click += (_, _) => click();
            row.Children.Add(b);
        }
        Add(Glyphs.Lightning, "Smart Optimize", true, "Analisar o PC e revisar os ajustes recomendados", () => NavigateTo("smart"));
        Add(Glyphs.Game, "Modo Jogo", false, "Sessão de jogo temporária, restaurada ao desativar", () => NavigateTo("gaming"));
        Add(Glyphs.Speed, "Performance Lab", false, "Medir FPS, frametime e sensores", () => NavigateTo("perflab"));
        Add(Glyphs.Undo, "Restaurar", false, "Backups, reversão e pontos de restauração", () => NavigateTo("history"));
        Add(Glyphs.Diagnostic, "Diagnóstico", false, "Diagnóstico inteligente e verificação do Windows", () => NavigateTo("diagnostics"));
        Add(Glyphs.Pin, "Modo compacto", false, "Janela pequena com CPU, GPU, RAM e temperaturas, para um segundo monitor", OpenCompactMonitor);
        panel.Children.Add(row);
        return Surface(panel);
    }

    /// <summary>Card de temperaturas: só sensores reais (GPU NVIDIA pelo driver). CPU fica indisponível, sem estimativa.</summary>
    private Border TemperatureCard()
    {
        var card = Card("TEMPERATURAS", "Lendo...", Glyphs.Warning, "MutedBrush");
        var value = card.Child is Grid g && g.Children.OfType<StackPanel>().FirstOrDefault()?.Children[1] is TextBlock v ? v : null;
        _ = Task.Run(() =>
        {
            double? gpu = null;
            try { if (NvidiaSmi.Available) gpu = NvidiaSmi.Double(NvidiaSmi.Query("temperature.gpu").FirstOrDefault()?[0]); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IndexOutOfRangeException) { }
            return gpu;
        }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            if (value is null) return;
            value.Text = (t.Result is { } c ? $"GPU {c:0} °C" : Translator.Tr("GPU: sensor indisponível")) + " · " + Translator.Tr("CPU: requer driver de kernel");
            value.Tag = Translator.SystemDataTag;
        }), TaskScheduler.Default);
        card.ToolTip = "Temperatura da GPU lida pelo driver NVIDIA. A da CPU exige um driver de kernel que o Qrztweaks não instala, por segurança.";
        return card;
    }
}
