using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using PQueirozOptimizer.BiosAdvisor;
using PQueirozOptimizer.BiosAdvisor.Detector;
using PQueirozOptimizer.Controls;
using PQueirozOptimizer.Engine;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// QRZTWEAKS PERFORMANCE LAB: captura de FPS e frametime com dados reais de apresentação (PresentMon), sensores
/// por segundo, histórico, comparação antes/depois, exportação e o Optimization Lab.
/// </summary>
public partial class MainWindow
{
    private string _perfTab = "capture";
    private HardwareProfile? _perfHardware;
    private PerfCaptureSession? _capture;
    private DispatcherTimer? _captureTimer;
    private PerfResult? _lastPerfResult;
    private string? _compareA, _compareB;

    /// <summary>Experimento do Optimization Lab em andamento (um ajuste por vez).</summary>
    private sealed class LabExperiment
    {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
        public required string TweakId { get; init; }
        public required string Process { get; init; }
        public int Seconds { get; init; } = 60;
        public bool Applied { get; set; }
        public List<PerfResult> Before { get; } = new();
        public List<PerfResult> After { get; } = new();
    }
    private LabExperiment? _lab;

    private void ShowPerformanceLab()
    {
        PageTitle.Text = "Performance Lab";
        PageBadge.Visibility = _capture is null ? Visibility.Collapsed : Visibility.Visible;
        PageBadgeText.Text = "gravando";
        var root = new StackPanel();
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, AppearanceService.Space(14)) };
        foreach (var (id, glyph, text) in new[] { ("capture", Glyphs.Play, "Captura"), ("history", Glyphs.History, "Histórico"), ("compare", Glyphs.Speed, "Comparar"), ("lab", Glyphs.Diagnostic, "Optimization Lab") })
        {
            var b = IconButton(glyph, text, primary: _perfTab == id);
            b.Margin = new Thickness(0, 0, 8, 8);
            System.Windows.Automation.AutomationProperties.SetName(b, text + (_perfTab == id ? " (aba atual)" : ""));
            b.Click += (_, _) => { _perfTab = id; ShowPerformanceLab(); };
            tabs.Children.Add(b);
        }
        root.Children.Add(tabs);
        var body = new StackPanel();
        root.Children.Add(body);
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        switch (_perfTab)
        {
            case "history": RenderPerfHistory(body); break;
            case "compare": RenderPerfCompare(body); break;
            case "lab": RenderLab(body); break;
            default: RenderPerfCapture(body); break;
        }
    }

    // ===================== Captura =====================
    private Border PresentMonCard()
    {
        var panel = new StackPanel();
        var tool = PresentMonTool.ToolPath();
        var head = new DockPanel();
        var pick = IconButton(Glyphs.Folder, tool is null ? "Escolher PresentMon" : "Trocar PresentMon");
        pick.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Escolha o PresentMon (PresentMon-2.x-x64.exe)", Filter = "PresentMon|PresentMon*.exe" };
            if (dialog.ShowDialog(this) != true) return;
            try { PresentMonTool.SetToolPath(dialog.FileName); ShowToast("PresentMon", "Pronto para capturar FPS e frametime.", "Success"); ShowPerformanceLab(); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { ShowToast("PresentMon", ex.Message, "Warning"); }
        };
        DockPanel.SetDock(pick, Dock.Right); head.Children.Add(pick);
        var download = IconButton(Glyphs.OpenInNew, "Baixar no GitHub oficial");
        download.Margin = new Thickness(0, 0, 8, 0);
        download.Click += (_, _) => OpenUrl("https://github.com/GameTechDev/PresentMon/releases");
        DockPanel.SetDock(download, Dock.Right); head.Children.Add(download);
        head.Children.Add(BoostText(tool is null ? "PresentMon não configurado" : "PresentMon pronto",
            tool is null
                ? "FPS e frametime vêm do PresentMon, a ferramenta aberta da Intel que lê a apresentação real dos quadros. Sem ele, a captura grava só os sensores e não mostra FPS (nada é estimado)."
                : "FPS, 1% low, 0,1% low e frametime são calculados dos quadros reais do jogo. O PresentMon roda a partir de uma cópia na pasta protegida do app."));
        panel.Children.Add(head);
        return Surface(panel);
    }

    private static List<string> WindowProcesses()
    {
        var names = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.MainWindowHandle != IntPtr.Zero && p.Id != Environment.ProcessId && !string.IsNullOrEmpty(p.ProcessName)) names.Add(p.ProcessName + ".exe"); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { p.Dispose(); }
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<HardwareProfile> PerfHardwareAsync() =>
        _perfHardware ??= _smartReading?.Hardware ?? await Task.Run(() => new HardwareDetector().Detect());

    private async Task<CaptureConditions> CaptureConditionsAsync()
    {
        var hw = await PerfHardwareAsync();
        var snapshot = await ReadSnapshotAsync();
        var (w, h, hz) = DisplayInfo.Current();
        var plan = MachineReader.Read(hw, snapshot).PowerPlan;
        return new CaptureConditions(hw.Cpu.Name, hw.PrimaryGpu?.Name ?? snapshot.Graphics, hw.PrimaryGpu?.DriverVersion ?? "", snapshot.OperatingSystem, SystemConditions.WindowsBuild,
            plan, $"{w}x{h}", hz, AppVersion);
    }

    private void RenderPerfCapture(StackPanel body)
    {
        body.Children.Add(PresentMonCard());
        var setup = new StackPanel();
        setup.Children.Add(SectionHeader("Nova captura", "Abra o jogo, escolha o processo e comece a gravar. Volte ao jogo: a captura continua em segundo plano e para sozinha no tempo escolhido."));
        var row = new WrapPanel();
        var process = new ComboBox { Width = 260, IsEditable = true, Margin = new Thickness(0, 0, 10, 10), ToolTip = "Processo do jogo (ex.: cs2.exe)" };
        System.Windows.Automation.AutomationProperties.SetName(process, "Processo do jogo");
        foreach (var n in WindowProcesses()) process.Items.Add(n);
        var refresh = IconButton(Glyphs.Refresh, "Atualizar lista"); refresh.Margin = new Thickness(0, 0, 10, 10);
        refresh.Click += (_, _) => { process.Items.Clear(); foreach (var n in WindowProcesses()) process.Items.Add(n); };
        var duration = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 10, 10) };
        System.Windows.Automation.AutomationProperties.SetName(duration, "Duração");
        foreach (var s in new[] { 30, 60, 120, 300 }) duration.Items.Add(new ComboBoxItem { Content = s >= 60 ? $"{s / 60} min" : $"{s} s", Tag = s });
        duration.SelectedIndex = 1;
        var label = new TextBox { Width = 220, Margin = new Thickness(0, 0, 10, 10), Text = "", ToolTip = "Nome do teste (ex.: Antes do Smart Optimize)" };
        System.Windows.Automation.AutomationProperties.SetName(label, "Nome do teste");
        // Cada campo com o rótulo em cima: sem isso as caixas vazias não diziam o que preencher
        StackPanel Field(string caption, FrameworkElement input)
        {
            var field = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            var c = Label(caption, 11, true); c.Margin = new Thickness(0, 0, 0, 4); field.Children.Add(c);
            field.Children.Add(input);
            return field;
        }
        refresh.VerticalAlignment = VerticalAlignment.Bottom;
        row.Children.Add(Field("Processo do jogo", process)); row.Children.Add(Field(" ", refresh)); row.Children.Add(Field("Duração", duration)); row.Children.Add(Field("Nome do teste (opcional)", label));
        setup.Children.Add(row);
        var start = IconButton(Glyphs.Play, _capture is null ? "Iniciar captura" : "Parar captura", primary: true);
        start.HorizontalAlignment = HorizontalAlignment.Left;
        setup.Children.Add(start);
        body.Children.Add(Surface(setup));

        var live = new StackPanel();
        live.Children.Add(SectionHeader("Ao vivo", "Valores lidos a cada segundo. FPS só aparece com o PresentMon capturando o processo escolhido."));
        var charts = new UniformGrid { Columns = 2 };
        (LineChart Chart, TextBlock Value) Chart(string title, double? max, string unit, string brush)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };
            var top = new DockPanel();
            var value = Label("—", 14); value.FontWeight = FontWeights.SemiBold; value.Margin = new Thickness(0); DockPanel.SetDock(value, Dock.Right);
            top.Children.Add(value);
            var t = Label(title, 12.5, true); t.Margin = new Thickness(0); top.Children.Add(t);
            p.Children.Add(top);
            var chart = new LineChart { Height = 90, Maximum = max, Unit = unit, StrokeKey = brush, Margin = new Thickness(0, 6, 0, 0) };
            p.Children.Add(chart);
            charts.Children.Add(p);
            return (chart, value);
        }
        var fps = Chart("FPS", null, "", "AccentBrush");
        var cpu = Chart("CPU", 100, "%", "InfoBrush");
        var gpu = Chart("GPU", 100, "%", "SuccessBrush");
        var temp = Chart("Temperatura da GPU", null, "°C", "WarningBrush");
        live.Children.Add(charts);
        var status = Label(_capture is null ? "Parado." : "Gravando...", 12.5, true);
        live.Children.Add(status);
        body.Children.Add(Surface(live));
        var resultHost = new StackPanel();
        body.Children.Add(resultHost);
        if (_lastPerfResult is { } previous && _capture is null) resultHost.Children.Add(PerfResultCard(previous));

        void Feed(PerfSample s)
        {
            fps.Chart.Push(s.Fps); cpu.Chart.Push(s.Cpu); gpu.Chart.Push(s.Gpu); temp.Chart.Push(s.GpuTemp);
            fps.Value.Text = s.Fps is { } f ? $"{f:0}" : "—"; cpu.Value.Text = s.Cpu is { } c ? $"{c:0}%" : "—";
            gpu.Value.Text = s.Gpu is { } g ? $"{g:0}%" : "—"; temp.Value.Text = s.GpuTemp is { } t ? $"{t:0} °C" : "indisponível";
        }
        if (_capture is not null) foreach (var s in _capture.Samples.TakeLast(60)) Feed(s);
        // A captura escreve sempre nos controles da página que está na tela (ela é redesenhada ao iniciar e ao voltar)
        _liveFeed = Feed; _liveStatus = status; _liveResultHost = resultHost;

        start.Click += async (_, _) =>
        {
            if (_capture is not null) { await StopCaptureAsync(_captureLabel); start.IsEnabled = true; ShowPerformanceLab(); return; }
            var name = (process.Text ?? "").Trim();
            if (name.Length > 0 && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { ShowToast("Performance Lab", "Nome de processo inválido.", "Warning"); return; }
            var seconds = (int)((ComboBoxItem)duration.SelectedItem).Tag;
            start.IsEnabled = false;
            await StartCaptureAsync(name.Length == 0 ? null : name, seconds, label.Text);
            start.IsEnabled = true;
            ShowPerformanceLab();
        };
    }

    private Action<PerfSample>? _liveFeed;
    private TextBlock? _liveStatus;
    private Panel? _liveResultHost;
    private string _captureLabel = "";

    private async Task StartCaptureAsync(string? process, int seconds, string label, Action<PerfResult>? finished = null)
    {
        try
        {
            var hw = await PerfHardwareAsync();
            _capture = PerfCaptureSession.Start(hw, process, seconds);
            HardwareMonitorService.Shared.Sampled += KeepMonitorAlive;
            _log.Write("INFO", $"Performance Lab: captura iniciada ({process ?? "só sensores"}, {seconds} s)");
            var started = DateTime.Now;
            _captureLabel = label;
            _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _captureTimer.Tick += async (_, _) =>
            {
                if (_capture is null) return;
                var sample = _capture.Tick();
                if (_currentPage == "perflab") _liveFeed?.Invoke(sample);
                var elapsed = (DateTime.Now - started).TotalSeconds;
                if (_liveStatus != null && _currentPage == "perflab") _liveStatus.Text = $"Gravando {elapsed:0} de {seconds} s" + (_capture.CapturingFrames ? (sample.Fps is null ? " · aguardando quadros do processo" : "") : " · só sensores (sem PresentMon ou sem processo)");
                if (elapsed >= seconds + 2 || _capture.PresentMonExited && elapsed > 3)
                {
                    var result = await StopCaptureAsync(label);
                    if (result is not null) finished?.Invoke(result);
                }
            };
            _captureTimer.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            _capture?.Dispose(); _capture = null;
            ShowToast("Performance Lab", ex.Message, "Danger");
        }
    }

    private static void KeepMonitorAlive(HardwareSample _) { }

    private async Task<PerfResult?> StopCaptureAsync(string label)
    {
        var status = _liveStatus;
        var resultHost = _currentPage == "perflab" && _perfTab == "capture" ? _liveResultHost : null;
        var session = _capture;
        if (session is null) return null;
        _captureTimer?.Stop(); _captureTimer = null;
        _capture = null;
        HardwareMonitorService.Shared.Sampled -= KeepMonitorAlive;
        try
        {
            var conditions = await CaptureConditionsAsync();
            var result = session.Finish(string.IsNullOrWhiteSpace(label) ? $"{session.Process ?? "Sensores"} · {DateTime.Now:dd/MM HH:mm}" : label.Trim(), conditions);
            if (_lab is { } lab && session.Process == lab.Process) result = result with { LabExperiment = lab.Id, LabPhase = lab.Applied ? "after" : "before" };
            PerfLabStore.Default.Save(result);
            _lastPerfResult = result;
            _log.Write("SUCCESS", $"Performance Lab: captura salva ({result.Source}" + (result.AverageFps is { } f ? $", {f:0} FPS médio" : "") + ")");
            if (status != null) status.Text = "Captura concluída e salva no histórico.";
            if (resultHost != null) { resultHost.Children.Clear(); resultHost.Children.Add(PerfResultCard(result)); }
            ShowToast("Performance Lab", result.HasFrames ? $"FPS médio {result.AverageFps:0} · 1% low {result.Low1Fps:0}" : "Captura salva (sem FPS: só sensores).", "Success");
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            ShowToast("Performance Lab", "A captura não pôde ser salva: " + ex.Message, "Danger");
            return null;
        }
        finally { session.Dispose(); }
    }

    private Border PerfResultCard(PerfResult r)
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader(r.Label, $"{r.StartedAt:dd/MM/yyyy HH:mm} · {r.DurationSeconds:0} s · {r.Process ?? "sem processo"} · fonte: {r.Source}"));
        var grid = new UniformGrid { Columns = 4 };
        string N(double? v, string fmt = "0") => v is { } x ? x.ToString(fmt) : "—";
        void Metric(string title, string value, string glyph) => grid.Children.Add(Card(title, value, glyph));
        Metric("FPS médio", N(r.AverageFps), Glyphs.Speed);
        Metric("FPS mínimo", N(r.MinimumFps), Glyphs.SpeedLow);
        Metric("1% low", N(r.Low1Fps), Glyphs.SpeedMedium);
        Metric("0,1% low", N(r.Low01Fps), Glyphs.SpeedLow);
        Metric("Frametime médio", r.AverageFrametimeMs is { } a ? $"{a:0.0} ms" : "—", Glyphs.Clock);
        Metric("Frametime P95 / P99", r.P95FrametimeMs is { } p95 ? $"{p95:0.0} / {r.P99FrametimeMs:0.0} ms" : "—", Glyphs.Clock);
        Metric("CPU / GPU", $"{N(r.CpuAvg)}% / {N(r.GpuAvg)}%", Glyphs.Chip);
        Metric("RAM", r.RamAvg is { } ram ? $"{ram:0}%" : "—", Glyphs.Memory);
        Metric("Temperatura da GPU", r.GpuTempMax is { } t ? $"{t:0} °C máx." : "indisponível", Glyphs.Warning);
        Metric("Clock / potência da GPU", r.GpuClockAvg is { } gc ? $"{gc:0} MHz · {N(r.GpuPowerAvg)} W" : "indisponível", Glyphs.Video);
        Metric("Clock da CPU (estimado)", r.CpuClockAvg is { } cc ? $"{cc:0} MHz" : "—", Glyphs.Chip);
        Metric("Limite de desempenho", r.LimitedSeconds is { } ls ? (ls == 0 ? "não detectado" : $"{ls} s com limite") : "—", Glyphs.Shield);
        panel.Children.Add(grid);
        if (!r.HasFrames) panel.Children.Add(Label("Sem FPS: a captura não teve quadros do PresentMon (ferramenta não configurada, processo errado ou menos de 100 quadros). Os números de FPS não são estimados a partir de outras métricas.", 12.5, true));
        if (r.ThrottleReasons.Count > 0) panel.Children.Add(Label("Redução de clock informada pelo driver: " + string.Join(", ", r.ThrottleReasons), 12.5, true));
        panel.Children.Add(Label("CPU sem temperatura: ler a temperatura da CPU exige um driver de kernel que o Qrztweaks não instala.", 11, true));
        var actions = new WrapPanel();
        void Action(string glyph, string text, Action click) { var b = IconButton(glyph, text); b.Margin = new Thickness(0, 0, 8, 0); b.Click += (_, _) => click(); actions.Children.Add(b); }
        Action(Glyphs.Save, "Exportar CSV", () => ExportPerf(r, "csv"));
        Action(Glyphs.Save, "Exportar JSON", () => ExportPerf(r, "json"));
        Action(Glyphs.Print, "Relatório PDF", () => PrintPerfReport(r, null));
        Action(Glyphs.Speed, "Comparar", () => { _compareB = r.Id; _perfTab = "compare"; ShowPerformanceLab(); });
        panel.Children.Add(actions);
        return Surface(panel);
    }

    private void ExportPerf(PerfResult r, string kind)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"qrztweaks-perf-{r.StartedAt:yyyyMMdd-HHmm}.{kind}", Filter = kind == "csv" ? "CSV|*.csv" : "JSON|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, kind == "csv" ? PerfExport.ToCsv(r) : PerfExport.ToJson(r)); ShowToast("Exportado", dialog.FileName, "Success"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowToast("Exportação", ex.Message, "Danger"); }
    }

    /// <summary>Relatório visual impresso (escolha "Microsoft Print to PDF" para gerar o PDF).</summary>
    private void PrintPerfReport(PerfResult r, PerfResult? before)
    {
        var doc = new FlowDocument { FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 12, PagePadding = new Thickness(48), ColumnWidth = 9999 };
        doc.Blocks.Add(new Paragraph(new Run(Translator.Tr("Qrztweaks · Relatório do Performance Lab"))) { FontSize = 20, FontWeight = FontWeights.Bold });
        doc.Blocks.Add(new Paragraph(new Run($"{r.Label} · {r.StartedAt:dd/MM/yyyy HH:mm} · {r.DurationSeconds:0} s · {r.Process ?? "-"} · {Translator.Tr(r.Source)}")));
        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn { Width = new GridLength(220) }); table.Columns.Add(new TableColumn()); if (before != null) { table.Columns.Add(new TableColumn()); table.Columns.Add(new TableColumn()); }
        var group = new TableRowGroup(); table.RowGroups.Add(group);
        void Row(params string[] cells) { var row = new TableRow(); foreach (var c in cells) row.Cells.Add(new TableCell(new Paragraph(new Run(c))) { Padding = new Thickness(4), BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 1) }); group.Rows.Add(row); }
        if (before is null)
        {
            Row(Translator.Tr("Métrica"), Translator.Tr("Valor"));
            foreach (var line in PerfExport.ToCsv(r).Split('\n').Skip(1).TakeWhile(l => l.Trim().Length > 0)) { var parts = line.Split(',', 2); if (parts.Length == 2) Row(parts[0], parts[1].Trim().Trim('"')); }
        }
        else
        {
            Row(Translator.Tr("Métrica"), Translator.Tr("Antes"), Translator.Tr("Depois"), Translator.Tr("Diferença"));
            foreach (var d in PerfMetrics.Compare(before, r)) Row(Translator.Tr(d.Metric), $"{d.Before:0.#} {d.Unit}", $"{d.After:0.#} {d.Unit}", $"{d.Absolute:+0.#;-0.#;0} {d.Unit}" + (d.Percent is { } p ? $" ({p:+0.#;-0.#;0}%)" : ""));
            foreach (var w in PerfMetrics.ConditionDifferences(before, r)) doc.Blocks.Add(new Paragraph(new Run("⚠ " + Translator.Tr(w))));
        }
        doc.Blocks.Add(table);
        if (r.Conditions is { } c) doc.Blocks.Add(new Paragraph(new Run($"{c.Cpu} · {c.Gpu} ({c.GpuDriver}) · {c.Windows} {c.Build} · {c.Resolution} {c.RefreshHz} Hz · {c.PowerPlan} · Qrztweaks {c.AppVersion}")) { FontSize = 10 });
        doc.Blocks.Add(new Paragraph(new Run(Translator.Tr("FPS e frametime vêm de dados reais de apresentação de quadros (PresentMon). Métricas ausentes não foram medidas."))) { FontSize = 10 });
        var print = new PrintDialog();
        if (print.ShowDialog() != true) return;
        doc.PageWidth = print.PrintableAreaWidth; doc.PageHeight = print.PrintableAreaHeight;
        print.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Qrztweaks Performance Lab");
    }

    // ===================== Histórico =====================
    private void RenderPerfHistory(StackPanel body)
    {
        var results = PerfLabStore.Default.List();
        var panel = new StackPanel();
        var head = new DockPanel();
        var import = IconButton(Glyphs.Upload, "Importar CSV");
        import.ToolTip = "Importar um log do PresentMon ou do CapFrameX";
        import.Click += async (_, _) => await ImportFrameCsvAsync();
        DockPanel.SetDock(import, Dock.Right); head.Children.Add(import);
        head.Children.Add(SectionHeader("Histórico de testes", results.Count == 0 ? "Nenhum teste salvo ainda." : $"{results.Count} teste(s). Os arquivos ficam em %LocalAppData%\\PQueirozOptimizer\\PerformanceLab."));
        panel.Children.Add(head);
        foreach (var r in results)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var open = IconButton(Glyphs.Info, "Ver"); open.Click += (_, _) => { _lastPerfResult = r; _perfTab = "capture"; ShowPerformanceLab(); };
            var delete = new Button { Content = GlyphIcon(Glyphs.Delete, 12, "DangerBrush"), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Excluir" };
            delete.SetResourceReference(StyleProperty, "GhostButton");
            System.Windows.Automation.AutomationProperties.SetName(delete, "Excluir " + r.Label);
            delete.Click += (_, _) =>
            {
                if (Msg($"Excluir o teste \"{r.Label}\"?", "Performance Lab", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                PerfLabStore.Default.Delete(r.Id); ShowPerformanceLab();
            };
            buttons.Children.Add(open); buttons.Children.Add(delete);
            DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons);
            var text = new StackPanel();
            var t = Label(r.Label, 13); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0); t.Tag = Translator.SystemDataTag;
            text.Children.Add(t);
            var d = Label($"{r.StartedAt:dd/MM/yyyy HH:mm} · {r.Process ?? "-"} · {r.DurationSeconds:0} s · " + (r.HasFrames ? $"{r.AverageFps:0} FPS · 1% low {r.Low1Fps:0}" : Translator.Tr("sem FPS")) + (r.LabPhase is { } ph ? $" · Lab {(ph == "after" ? "depois" : "antes")}" : ""), 12, true);
            d.Margin = new Thickness(0, 2, 0, 0); d.Tag = Translator.SystemDataTag;
            text.Children.Add(d);
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        body.Children.Add(Surface(panel));
    }

    private async Task ImportFrameCsvAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Log de frametime (PresentMon ou CapFrameX)", Filter = "CSV|*.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            // Mesmo leitor do BIOS Advisor: cabeçalhos com aspas ou outra caixa e só o fluxo principal (processo + swapchain)
            var (application, frames) = BiosAdvisor.Benchmark.FrametimeAnalyzer.BestStream(await File.ReadAllTextAsync(dialog.FileName));
            var conditions = await CaptureConditionsAsync();
            var result = PerfMetrics.Build(new PerfResult { Label = Path.GetFileNameWithoutExtension(dialog.FileName), Process = application, Source = "CSV importado", Conditions = conditions },
                frames.Count >= 100 ? frames : null, Array.Empty<PerfSample>());
            if (!result.HasFrames) throw new FormatException("O log não tem quadros suficientes do processo principal.");
            PerfLabStore.Default.Save(result);
            ShowToast("Performance Lab", $"Importado: {result.AverageFps:0} FPS médio.", "Success");
            ShowPerformanceLab();
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException) { ShowToast("Importação", ex.Message, "Warning"); }
    }

    // ===================== Comparar =====================
    private void RenderPerfCompare(StackPanel body)
    {
        var results = PerfLabStore.Default.List();
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Comparação antes/depois", "Escolha dois testes. A comparação mostra a diferença absoluta e percentual e avisa quando as condições não são as mesmas."));
        if (results.Count < 2) { panel.Children.Add(Label("Grave pelo menos dois testes para comparar.", 13, true)); body.Children.Add(Surface(panel)); return; }
        ComboBox Pick(string? selectedId, string name)
        {
            var box = new ComboBox { Width = 360, Margin = new Thickness(0, 0, 12, 10) };
            System.Windows.Automation.AutomationProperties.SetName(box, name);
            foreach (var r in results) box.Items.Add(new ComboBoxItem { Content = new TextBlock { Text = $"{r.StartedAt:dd/MM HH:mm} · {r.Label}", Tag = Translator.SystemDataTag }, Tag = r.Id });
            box.SelectedIndex = Math.Max(0, results.ToList().FindIndex(r => r.Id == selectedId));
            return box;
        }
        _compareB ??= results[0].Id;
        _compareA ??= results.FirstOrDefault(r => r.Id != _compareB)?.Id;
        var a = Pick(_compareA, "Antes"); var b = Pick(_compareB, "Depois");
        var pickers = new WrapPanel();
        pickers.Children.Add(Label("Antes", 12.5, true)); pickers.Children.Add(a); pickers.Children.Add(Label("Depois", 12.5, true)); pickers.Children.Add(b);
        panel.Children.Add(pickers);
        a.SelectionChanged += (_, _) => { _compareA = (string)((ComboBoxItem)a.SelectedItem).Tag; ShowPerformanceLab(); };
        b.SelectionChanged += (_, _) => { _compareB = (string)((ComboBoxItem)b.SelectedItem).Tag; ShowPerformanceLab(); };
        var before = results.First(r => r.Id == (string)((ComboBoxItem)a.SelectedItem).Tag);
        var after = results.First(r => r.Id == (string)((ComboBoxItem)b.SelectedItem).Tag);
        body.Children.Add(Surface(panel));

        foreach (var warning in PerfMetrics.ConditionDifferences(before, after)) body.Children.Add(NoticeCard("Condições diferentes", warning, "Warning"));
        var table = new Grid();
        foreach (var w in new[] { 2.2, 1, 1, 1.4 }) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        var r0 = 0;
        void Cell(string text, int col, bool bold = false, string brush = "TextBrush")
        {
            var tb = new TextBlock { Text = text, FontSize = 12.5, Margin = new Thickness(0, 6, 8, 6), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
            tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
            Grid.SetRow(tb, r0); Grid.SetColumn(tb, col); table.Children.Add(tb);
        }
        table.RowDefinitions.Add(new RowDefinition());
        Cell("Métrica", 0, true); Cell("Antes", 1, true); Cell("Depois", 2, true); Cell("Diferença", 3, true);
        var deltas = PerfMetrics.Compare(before, after);
        foreach (var d in deltas)
        {
            r0++; table.RowDefinitions.Add(new RowDefinition());
            var better = d.Absolute == 0 ? (bool?)null : d.Absolute > 0 == d.HigherIsBetter;
            Cell(d.Metric, 0); Cell($"{d.Before:0.#} {d.Unit}", 1); Cell($"{d.After:0.#} {d.Unit}", 2);
            // Melhor/pior vai em texto e símbolo, não só na cor
            Cell($"{(better == true ? "▲ melhor" : better == false ? "▼ pior" : "=")} {d.Absolute:+0.#;-0.#;0} {d.Unit}" + (d.Percent is { } p ? $" ({p:+0.#;-0.#;0}%)" : ""), 3, false, better == true ? "SuccessBrush" : better == false ? "DangerBrush" : "MutedBrush");
        }
        var tablePanel = new StackPanel();
        tablePanel.Children.Add(table);
        if (deltas.Count == 0) tablePanel.Children.Add(Label("Os dois testes não têm métricas em comum.", 13, true));
        tablePanel.Children.Add(Label("Uma única gravação de cada lado não prova ganho: diferenças de poucos por cento estão dentro da variação normal. Para decidir sobre um ajuste, use o Optimization Lab (várias gravações e teste estatístico).", 12, true));
        var print = IconButton(Glyphs.Print, "Relatório PDF da comparação"); print.Click += (_, _) => PrintPerfReport(after, before);
        tablePanel.Children.Add(print);
        body.Children.Add(Surface(tablePanel));

        var cond = new StackPanel();
        cond.Children.Add(SectionHeader("Condições dos testes"));
        foreach (var (title, r) in new[] { ("Antes", before), ("Depois", after) })
            if (r.Conditions is { } c) { var l = Label($"{Translator.Tr(title)}: {c.Cpu} · {c.Gpu} (driver {c.GpuDriver}) · {c.Windows} build {c.Build} · {c.Resolution} {c.RefreshHz} Hz · {Translator.Tr(c.PowerPlan)}", 12, true); l.Tag = Translator.SystemDataTag; cond.Children.Add(l); }
        body.Children.Add(Surface(cond));
    }

    // ===================== Optimization Lab =====================
    private void RenderLab(StackPanel body)
    {
        var intro = new StackPanel();
        intro.Children.Add(SectionHeader("Optimization Lab", "Mede o efeito de um ajuste por vez: grave a mesma cena algumas vezes, aplique o ajuste, grave de novo e compare. Uma diferença só conta como ganho se for consistente e maior que 3%."));
        intro.Children.Add(Label("1. Grave a referência  ·  2. Aplique um ajuste reversível  ·  3. Grave depois  ·  4. Mantenha ou reverta", 12.5, true));
        body.Children.Add(Surface(intro));

        if (_lab is null)
        {
            var setup = new StackPanel();
            // Só ajustes reversíveis, que valem sem reiniciar (o experimento fica na memória e não sobrevive a um reinício)
            var tweaks = TweakCatalog.All.Where(t => !t.OneOff && !t.RequiresReboot && !t.Revert.StartsWith("Não reversível", StringComparison.Ordinal) && !t.Revert.StartsWith("Reinstale", StringComparison.Ordinal) && PlanAccess.Allows(CurrentLicense, PlanAccess.OperationPage(t.Operation))).ToList();
            if (tweaks.Count == 0)
            {
                setup.Children.Add(Label(CurrentLicense is null ? "No modo demonstração nenhum ajuste pode ser aplicado, então o Optimization Lab fica indisponível. Ative uma licença para usar." : "Nenhum ajuste do seu plano pode ser testado aqui.", 13, true));
                body.Children.Add(Surface(setup));
                return;
            }
            var tweakBox = new ComboBox { Width = 360, Margin = new Thickness(0, 0, 12, 10) };
            System.Windows.Automation.AutomationProperties.SetName(tweakBox, "Ajuste a testar");
            foreach (var t in tweaks) tweakBox.Items.Add(new ComboBoxItem { Content = t.Name, Tag = t.Id });
            tweakBox.SelectedIndex = 0;
            var process = new ComboBox { Width = 240, IsEditable = true, Margin = new Thickness(0, 0, 12, 10) };
            System.Windows.Automation.AutomationProperties.SetName(process, "Processo do jogo");
            foreach (var n in WindowProcesses()) process.Items.Add(n);
            var duration = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 12, 10) };
            foreach (var s in new[] { 30, 60, 90 }) duration.Items.Add(new ComboBoxItem { Content = $"{s} s", Tag = s });
            duration.SelectedIndex = 1;
            var row = new WrapPanel();
            row.Children.Add(tweakBox); row.Children.Add(process); row.Children.Add(duration);
            setup.Children.Add(row);
            if (PresentMonTool.ToolPath() is null) setup.Children.Add(Label("Sem o PresentMon, o Lab não mede FPS e o resultado fica limitado ao uso de CPU. Configure-o na aba Captura.", 12.5, true));
            var begin = IconButton(Glyphs.Play, "Começar experimento", primary: true);
            begin.HorizontalAlignment = HorizontalAlignment.Left;
            begin.Click += (_, _) =>
            {
                var name = (process.Text ?? "").Trim();
                if (name.Length == 0) { ShowToast("Optimization Lab", "Escolha o processo do jogo.", "Warning"); return; }
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
                if (tweakBox.SelectedItem is not ComboBoxItem { Tag: string tweakId }) { ShowToast("Optimization Lab", "Escolha o ajuste a testar.", "Warning"); return; }
                _lab = new LabExperiment { TweakId = tweakId, Process = name, Seconds = (int)((ComboBoxItem)duration.SelectedItem).Tag };
                ShowPerformanceLab();
            };
            setup.Children.Add(begin);
            body.Children.Add(Surface(setup));
            return;
        }

        var lab = _lab;
        var tweak = TweakCatalog.Find(lab.TweakId)!;
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader($"Experimento: {tweak.Name}", $"Processo {lab.Process} · {lab.Seconds} s por gravação · mínimo de {OptimizationLab.MinimumRuns} gravações em cada fase. Repita sempre a mesma cena, com as mesmas configurações do jogo."));
        var progress = new WrapPanel();
        var before = Pill($"Antes: {lab.Before.Count}/{OptimizationLab.MinimumRuns}", lab.Before.Count >= OptimizationLab.MinimumRuns ? "Success" : "Accent"); before.Margin = new Thickness(0, 0, 8, 8);
        var after = Pill($"Depois: {lab.After.Count}/{OptimizationLab.MinimumRuns}", lab.After.Count >= OptimizationLab.MinimumRuns ? "Success" : "Accent"); after.Margin = new Thickness(0, 0, 8, 8);
        var applied = Pill(lab.Applied ? "Ajuste aplicado" : "Ajuste ainda não aplicado", lab.Applied ? "Warning" : "Info"); applied.Margin = new Thickness(0, 0, 8, 8);
        progress.Children.Add(before); progress.Children.Add(applied); progress.Children.Add(after);
        panel.Children.Add(progress);
        var status = Label(_capture is null ? "" : "Gravando... volte ao jogo.", 12.5, true);
        panel.Children.Add(status);
        _liveFeed = null; _liveStatus = status; _liveResultHost = null;
        var actions = new WrapPanel();
        void Act(string glyph, string text, bool primary, bool enabled, Func<Task> click) { var b = IconButton(glyph, text, primary); b.IsEnabled = enabled && _capture is null; b.Margin = new Thickness(0, 0, 8, 8); b.Click += async (_, _) => await click(); actions.Children.Add(b); }
        Act(Glyphs.Play, "Gravar referência", !lab.Applied && lab.Before.Count < OptimizationLab.MinimumRuns, !lab.Applied, () => RecordLabRunAsync(status));
        Act(Glyphs.Lightning, "Aplicar o ajuste", lab.Before.Count >= OptimizationLab.MinimumRuns && !lab.Applied, lab.Before.Count >= OptimizationLab.MinimumRuns && !lab.Applied, async () =>
        {
            if (Msg($"Aplicar \"{tweak.Name}\"? Ele fica no backup e pode ser revertido no fim do experimento.", "Optimization Lab", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var outcome = await RunLiveAsync(tweak.Operation, new[] { tweak.Step });
            // Só conta como aplicado se o Windows confirmar o novo estado; senão, as gravações "depois" não mediriam nada
            if (outcome is LiveOutcome.Completed or LiveOutcome.CompletedWithFailures)
            {
                if (await TweakStateAsync(tweak) == TweakState.Applied) lab.Applied = true;
                else ShowToast("Optimization Lab", "O ajuste não foi confirmado no Windows. Veja o registro da execução; o experimento continua sem o ajuste.", "Warning");
            }
            _perfTab = "lab"; NavigateTo("perflab");
        });
        Act(Glyphs.Play, "Gravar depois", lab.Applied && lab.After.Count < OptimizationLab.MinimumRuns, lab.Applied, () => RecordLabRunAsync(status));
        Act(Glyphs.Cancel, "Encerrar experimento", false, true, () => { _lab = null; ShowPerformanceLab(); return Task.CompletedTask; });
        panel.Children.Add(actions);
        body.Children.Add(Surface(panel));

        if (lab.Before.Count > 0 && lab.After.Count > 0)
        {
            var outcome = OptimizationLab.Evaluate(lab.Before, lab.After);
            var result = new StackPanel();
            var (title, tone) = outcome.Verdict switch
            {
                LabVerdict.LikelyGain => ("Provável ganho", "Success"),
                LabVerdict.LikelyLoss => ("Provável perda", "Danger"),
                LabVerdict.NoProvenDifference => ("Sem diferença comprovada", "Info"),
                _ => ("Dados insuficientes", "Warning"),
            };
            result.Children.Add(SectionHeader("Resultado"));
            var verdict = Pill(title, tone); verdict.HorizontalAlignment = HorizontalAlignment.Left; verdict.Margin = new Thickness(0, 0, 0, 10);
            result.Children.Add(verdict);
            if (outcome.Verdict != LabVerdict.InsufficientData)
            {
                var stats = Label($"{outcome.Metric}: {outcome.MeanBefore:0.#} → {outcome.MeanAfter:0.#} ({outcome.DeltaPercent:+0.#;-0.#;0}%)" + (outcome.PValue is { } p ? $" · p = {p:0.###}" : "") +
                    $" · variação {outcome.CvBefore:0.#}% / {outcome.CvAfter:0.#}%" + (outcome.Low1DeltaPercent is { } l ? $" · 1% low {l:+0.#;-0.#;0}%" : ""), 13);
                stats.Tag = Translator.SystemDataTag;
                result.Children.Add(stats);
            }
            result.Children.Add(Label(outcome.Recommendation, 13));
            foreach (var w in outcome.Warnings) result.Children.Add(NoticeCard("Atenção", w, "Warning"));
            if (lab.Applied && lab.After.Count >= OptimizationLab.MinimumRuns)
            {
                var keep = IconButton(Glyphs.Check, "Manter", primary: outcome.Verdict == LabVerdict.LikelyGain);
                keep.Click += (_, _) => { _log.Write("INFO", $"Optimization Lab: ajuste mantido ({tweak.Name}, {outcome.DeltaPercent:+0.#;-0.#;0}%)"); _lab = null; ShowPerformanceLab(); };
                var revert = IconButton(Glyphs.Undo, "Reverter o ajuste", primary: outcome.Verdict != LabVerdict.LikelyGain);
                revert.Margin = new Thickness(8, 0, 0, 0);
                revert.Click += async (_, _) =>
                {
                    var o = await RunLiveAsync("reverter", new[] { tweak.Step });
                    // Revertido = o estado deixou de ser o do ajuste; se continuar aplicado, o experimento fica aberto
                    if (o is LiveOutcome.Completed or LiveOutcome.CompletedWithFailures && await TweakStateAsync(tweak) != TweakState.Applied)
                    { _log.Write("INFO", $"Optimization Lab: ajuste revertido ({tweak.Name})"); _lab = null; }
                    else ShowToast("Optimization Lab", "A reversão não foi confirmada. Confira em Atividade e reversão.", "Warning");
                    _perfTab = "lab"; NavigateTo("perflab");
                };
                var decision = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                decision.Children.Add(keep); decision.Children.Add(revert);
                result.Children.Add(decision);
            }
            body.Children.Add(Surface(result));
        }
    }

    /// <summary>Estado atual de um ajuste, lido no Windows.</summary>
    private static async Task<TweakState> TweakStateAsync(TweakDefinition tweak)
    {
        try { return tweak.Read(MachineReader.QuickContext(await LiveSystemState.LoadAsync())).State; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return TweakState.ReadFailed; }
    }

    private async Task RecordLabRunAsync(TextBlock status)
    {
        var lab = _lab!;
        var phase = lab.Applied ? "Depois" : "Antes";
        var label = $"Lab {lab.Id} · {TweakCatalog.Find(lab.TweakId)?.Name} · {phase} {(lab.Applied ? lab.After.Count : lab.Before.Count) + 1}";
        status.Text = "Gravando... volte ao jogo e repita a mesma cena.";
        await StartCaptureAsync(lab.Process, lab.Seconds, label, result =>
        {
            if (_lab != lab) return;
            (lab.Applied ? lab.After : lab.Before).Add(result);
            if (_currentPage == "perflab" && _perfTab == "lab") ShowPerformanceLab();
        });
        ShowPerformanceLab();
    }
}
