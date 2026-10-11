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
}
