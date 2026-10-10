using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Engine;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Diagnóstico inteligente (fase 8): gargalos, memória, temperatura, disco, processos, energia e drivers, com evidência.</summary>
public partial class MainWindow
{
    private DiagnosticsEngine.Report? _lastDiagnosis;

    private Border SmartDiagnosticsPanel()
    {
        var panel = new StackPanel();
        var head = new DockPanel();
        var run = IconButton(Glyphs.Diagnostic, "Analisar desempenho", primary: true);
        DockPanel.SetDock(run, Dock.Right); head.Children.Add(run);
        head.Children.Add(SectionHeader("Diagnóstico inteligente",
            "Analisa o último teste do Performance Lab, o monitor ao vivo, discos, processos, energia e drivers. Cada conclusão mostra a evidência e o grau de confiança; sem dados suficientes, nada é afirmado."));
        panel.Children.Add(head);
        var results = new StackPanel();
        panel.Children.Add(results);
        if (_lastDiagnosis is { } last) RenderDiagnosis(results, last);
        run.Click += async (_, _) =>
        {
            run.IsEnabled = false;
            results.Children.Clear();
            results.Children.Add(Label("Coletando dados (alguns segundos)...", 13, true));
            results.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 5 });
            // O monitor ao vivo precisa estar rodando para haver histórico de uso
            HardwareMonitorService.Shared.Sampled += KeepMonitorAlive;
            try
            {
                var snapshot = await ReadSnapshotAsync();
                var inputs = await DiagnosticsCollector.CollectAsync(snapshot, _smartReading?.Machine, PerfLabStore.Default.List().FirstOrDefault());
                _lastDiagnosis = DiagnosticsEngine.Run(inputs);
                _log.Write("INFO", $"Diagnóstico inteligente: {_lastDiagnosis.Findings.Count} achado(s)");
                results.Children.Clear();
                RenderDiagnosis(results, _lastDiagnosis);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                results.Children.Clear();
                results.Children.Add(Label("Não foi possível concluir: " + ex.Message, 13, true));
            }
            finally { HardwareMonitorService.Shared.Sampled -= KeepMonitorAlive; run.IsEnabled = true; }
        };
        return Surface(panel);
    }

    private void RenderDiagnosis(StackPanel host, DiagnosticsEngine.Report report)
    {
        if (report.Findings.Count == 0)
            host.Children.Add(NoticeCard("Nenhum problema encontrado", "Com os dados disponíveis, nada indica gargalo ou problema. Veja abaixo o que não pôde ser avaliado.", "Info"));
        foreach (var d in report.Findings) host.Children.Add(DiagnosisCard(d));
        if (report.MissingData.Count > 0)
        {
            var missing = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var t = Label("Não avaliado por falta de dados", 13); t.FontWeight = FontWeights.SemiBold; missing.Children.Add(t);
            foreach (var m in report.MissingData) missing.Children.Add(Label("• " + m, 12.5, true));
            if (report.MissingData.Any(m => m.Contains("Performance Lab", StringComparison.Ordinal)))
            {
                var go = IconButton(Glyphs.Speed, "Abrir o Performance Lab");
                go.HorizontalAlignment = HorizontalAlignment.Left;
                go.Click += (_, _) => NavigateTo("perflab");
                missing.Children.Add(go);
            }
            host.Children.Add(missing);
        }
    }

    private Border DiagnosisCard(Diagnosis d)
    {
        var panel = new StackPanel();
        var head = new DockPanel();
        var tone = d.Severity switch { DiagnosisSeverity.Critical => "Danger", DiagnosisSeverity.Warning => "Warning", _ => "Info" };
        var chip = IconChip(d.Severity == DiagnosisSeverity.Info ? Glyphs.Info : Glyphs.Warning, tone, 34); DockPanel.SetDock(chip, Dock.Left); head.Children.Add(chip);
        var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        var t = Label(d.Title, 14); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 0, 0, 4); titles.Children.Add(t);
        var pills = new WrapPanel();
        var sev = Pill(d.Severity switch { DiagnosisSeverity.Critical => "Crítico", DiagnosisSeverity.Warning => "Atenção", _ => "Informativo" }, tone); sev.Margin = new Thickness(0, 0, 6, 0);
        var conf = Pill("Confiança " + d.Confidence switch { DiagnosisConfidence.High => "alta", DiagnosisConfidence.Medium => "média", _ => "baixa" }, "Accent");
        pills.Children.Add(sev); pills.Children.Add(conf);
        titles.Children.Add(pills);
        head.Children.Add(titles);
        panel.Children.Add(head);
        void Section(string title, IEnumerable<string> lines)
        {
            var s = Label(title, 12.5); s.FontWeight = FontWeights.SemiBold; s.Margin = new Thickness(46, 8, 0, 2); panel.Children.Add(s);
            foreach (var line in lines) { var l = Label("• " + line, 12.5, true); l.Margin = new Thickness(46, 0, 0, 2); panel.Children.Add(l); }
        }
        Section("Evidências", d.Evidence);
        Section("Possíveis causas", d.Causes);
        Section("Recomendações", d.Recommendations);
        Section("Como confirmar", new[] { d.HowToConfirm });
        Section("Riscos", new[] { d.Risks });
        if (d.RelatedPage is { } page && page != "dashboard")
        {
            var go = IconButton(Glyphs.ChevronRight, "Abrir " + PageTitles.GetValueOrDefault(page, page));
            go.Margin = new Thickness(46, 8, 0, 0); go.HorizontalAlignment = HorizontalAlignment.Left;
            go.Click += (_, _) => NavigateTo(page);
            panel.Children.Add(go);
        }
        var card = new Border { Child = panel, Padding = new Thickness(16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 10, 0, 0) };
        card.SetResourceReference(Border.BorderBrushProperty, tone + "SoftBrush");
        return card;
    }
}
