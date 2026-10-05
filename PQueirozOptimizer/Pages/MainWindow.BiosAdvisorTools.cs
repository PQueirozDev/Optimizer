using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.BiosAdvisor;
using PQueirozOptimizer.BiosAdvisor.Benchmark;
using PQueirozOptimizer.BiosAdvisor.Monitoring;
using PQueirozOptimizer.BiosAdvisor.Updates;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>BIOS Advisor: versão da BIOS, analisador de memória, monitor de sensores e teste antes/depois.</summary>
public partial class MainWindow
{
    private Task<BiosUpdateResult>? _biosUpdateCheck;
    private SensorReader? _advisorSensors;
    private System.Windows.Threading.DispatcherTimer? _advisorMonitorTimer;
    private CancellationTokenSource? _advisorRecording;
    private List<SensorSnapshot>? _advisorRecorded;
    private BenchmarkSlot _advisorRecordingSlot;

    // ================= Versão da BIOS =================
    private Border AdvisorBiosVersionCard(AdvisorReport report)
    {
        var profile = report.Profile;
        var panel = new StackPanel();
        _biosUpdateCheck ??= BiosUpdateCheck.CheckAsync(profile, CancellationToken.None);
        var check = _biosUpdateCheck;
        if (!check.IsCompleted) _ = RedrawWhenAsync(check);
        var result = check.IsCompletedSuccessfully ? check.Result : null;
        var (status, tone) = result?.Status switch
        {
            BiosUpdateStatus.UpdateAvailable => (T("Atualização disponível", "Update available"), "Warning"),
            BiosUpdateStatus.UpToDate => (T("Atualizada", "Up to date"), "Success"),
            BiosUpdateStatus.Newer => (T("Mais nova que a pública", "Newer than public"), "Info"),
            null when !check.IsCompleted => (T("Verificando…", "Checking…"), "Info"),
            _ => (T("Não verificado", "Not verified"), "Info"),
        };
        panel.Children.Add(FeatureHeader(Glyphs.Download, "Accent", T("Versão da BIOS", "BIOS version"),
            T("Comparação com a lista oficial da fabricante. Sem fonte confiável, mostramos só a versão instalada.",
              "Compared with the manufacturer's official list. Without a reliable source, only the installed version is shown."), status, tone));
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, -10, 0) };
        grid.Children.Add(SmallStat(T("INSTALADA", "INSTALLED"), profile.Bios.Version.Length > 0 ? profile.Bios.Version : "—", profile.Bios.ReleaseDate?.ToString(_loc.IsEnglish ? "yyyy-MM-dd" : "dd/MM/yyyy")));
        grid.Children.Add(SmallStat(T("DISPONÍVEL", "AVAILABLE"), result?.Latest ?? "—", result?.LatestDate?.ToString(_loc.IsEnglish ? "yyyy-MM-dd" : "dd/MM/yyyy")));
        panel.Children.Add(grid);
        if (result?.Note is { } note) panel.Children.Add(Notice(L(note), "Info"));
        if (result?.Status == BiosUpdateStatus.UpdateAvailable)
            panel.Children.Add(Notice(T("Atualize só pelo arquivo do site oficial, com a ferramenta da própria BIOS (ex.: ASUS EZ Flash 3), sem desligar o PC durante o processo. Atualizar a BIOS volta as configurações ao padrão: anote as suas antes.",
                "Update only with the file from the official website, using the BIOS's own tool (e.g. ASUS EZ Flash 3), without powering off during the process. Updating resets BIOS settings to default: note yours first."), "Warning"));
        if ((result?.OfficialUrl ?? VendorCatalog.For(profile.Motherboard).SupportUrl(profile.Motherboard)) is { } url)
        {
            var open = IconButton(Glyphs.OpenInNew, T("Abrir página oficial da fabricante", "Open the manufacturer's official page"));
            open.Margin = new Thickness(0, 12, 0, 0); open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Click += (_, _) => OpenUrl(url);
            panel.Children.Add(open);
        }
        return Surface(panel);
    }

    private async Task RedrawWhenAsync(Task task)
    {
        try { await task; } catch (Exception ex) { _log.Write("WARN", "BIOS Advisor: " + ex.GetBaseException().Message); }
        if (_currentPage == "biosadvisor" && IsLoaded && AdvisorLayer.Visibility != Visibility.Visible) KeepScroll(ShowBiosAdvisor);
    }

    private Border SmallStat(string label, string value, string? detail)
    {
        var cell = new StackPanel();
        var l = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold };
        l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        cell.Children.Add(l);
        var v = new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, Tag = Translator.SystemDataTag };
        v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        cell.Children.Add(v);
        if (!string.IsNullOrEmpty(detail))
        {
            var d = new TextBlock { Text = detail, FontSize = 11, Margin = new Thickness(0, 2, 0, 0), Tag = Translator.SystemDataTag };
            d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            cell.Children.Add(d);
        }
        var box = ListRow(cell); box.Margin = new Thickness(0, 0, 10, 0);
        return box;
    }

    // ================= Memória =================
    private Border AdvisorMemoryCard(AdvisorReport report)
    {
        var mem = MemoryAnalyzer.Analyze(report.Profile.Memory, report.Platform);
        var p = report.Profile.Memory;
        var panel = new StackPanel();
        var ok = mem.Issues.Count == 0;
        panel.Children.Add(FeatureHeader(Glyphs.Memory, "Info", T("Analisador de memória", "Memory analyzer"),
            T("Canais, velocidade, pentes e timings. Nada é alterado: os timings só podem ser mudados na BIOS.", "Channels, speed, modules and timings. Nothing is changed: timings can only be changed in the BIOS."),
            ok ? T("Sem problemas", "No issues") : T($"{mem.Issues.Count} ponto(s) de atenção", $"{mem.Issues.Count} item(s) to review"), ok ? "Success" : "Warning"));
        var type = p.Type switch { MemoryType.Ddr5 => "DDR5", MemoryType.Ddr4 => "DDR4", MemoryType.Ddr3 => "DDR3", _ => "" };
        var summary = new List<string> { $"{p.TotalBytes / 1024d / 1024 / 1024:0.#} GB {type}".Trim() };
        if (mem.Channels != ChannelMode.Unknown) summary.Add(mem.Channels switch { ChannelMode.Dual => "Dual Channel", ChannelMode.Single => "Single Channel", _ => "Multi Channel" });
        if (mem.ConfiguredMhz > 0) summary.Add($"{mem.ConfiguredMhz} MHz");
        summary.Add(mem.CasLatency is { } cl ? $"CL{cl} ({T("pelo part number", "from part number")})" : T("timings não informados pelo Windows", "timings not reported by Windows"));
        var line = Label(string.Join(" · ", summary), 13); line.FontWeight = FontWeights.SemiBold; line.Margin = new Thickness(0, 14, 0, 8);
        panel.Children.Add(line);
        foreach (var m in p.Modules)
        {
            var slot = MemoryAnalyzer.SlotOf(m.Locator) ?? m.Locator;
            var rank = m.Rank is { } r ? $" · {r}R" : "";
            var text = new TextBlock { Text = $"{slot} · {m.CapacityBytes / 1024 / 1024 / 1024} GB · {m.Manufacturer} {m.PartNumber.Trim()} · {m.ConfiguredMhz}/{m.RatedMhz} MHz{rank}", FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = Translator.SystemDataTag, Margin = new Thickness(0, 0, 0, 3) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            panel.Children.Add(text);
        }
        if (p.TotalSlots is { } slots)
        {
            var s = Label(T($"{p.Modules.Count} de {slots} slots ocupados", $"{p.Modules.Count} of {slots} slots used"), 12, true); s.Margin = new Thickness(0, 4, 0, 0);
            panel.Children.Add(s);
        }
        foreach (var issue in mem.Issues) panel.Children.Add(Notice(L(issue), "Warning"));
        return Surface(panel);
    }

    // ================= Monitor =================
    private readonly Dictionary<string, TextBlock> _advisorMonitorValues = new();

    private Border AdvisorMonitorCard(AdvisorReport report)
    {
        var panel = new StackPanel();
        panel.Children.Add(FeatureHeader(Glyphs.Diagnostic, "Accent", T("Monitor de hardware", "Hardware monitor"),
            T("Leituras reais dos sensores que o Windows e o driver de vídeo expõem, atualizadas a cada 3 s. Temperatura e consumo da CPU exigem um driver de sensores que o app não instala: aparecem como indisponíveis.",
              "Real readings from sensors exposed by Windows and the graphics driver, refreshed every 3 s. CPU temperature and power need a sensor driver the app doesn't install: they show as unavailable."),
            T("Ao vivo", "Live"), "Success"));
        _advisorMonitorValues.Clear();
        var grid = Responsive(new UniformGrid { Columns = 5, Margin = new Thickness(0, 14, -10, 0) }, 170, 5);
        foreach (var (key, label) in new[]
        {
            ("cpuTemp", T("CPU · TEMPERATURA", "CPU · TEMPERATURE")), ("cpuClock", T("CPU · CLOCK (ESTIMADO)", "CPU · CLOCK (ESTIMATED)")), ("cpuUsage", T("CPU · USO", "CPU · USAGE")), ("cpuPower", T("CPU · CONSUMO", "CPU · POWER")), ("cpuLimit", T("CPU · LIMITAÇÃO", "CPU · THROTTLING")),
            ("gpuTemp", T("GPU · TEMPERATURA", "GPU · TEMPERATURE")), ("gpuClock", T("GPU · CLOCK", "GPU · CLOCK")), ("gpuUsage", T("GPU · USO", "GPU · USAGE")), ("gpuPower", T("GPU · CONSUMO", "GPU · POWER")), ("gpuLimit", T("GPU · LIMITE DE ENERGIA", "GPU · POWER LIMIT")),
        })
        {
            var cell = new StackPanel();
            var l = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            cell.Children.Add(l);
            var v = new TextBlock { Text = "…", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, Tag = Translator.SystemDataTag };
            v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            cell.Children.Add(v);
            _advisorMonitorValues[key] = v;
            var box = ListRow(cell); box.Margin = new Thickness(0, 0, 10, 10);
            grid.Children.Add(box);
        }
        panel.Children.Add(grid);
        var throttle = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Tag = Translator.SystemDataTag };
        throttle.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _advisorMonitorValues["reasons"] = throttle;
        panel.Children.Add(throttle);
        StartAdvisorMonitor(report.Profile);
        return Surface(panel);
    }

    private HardwareProfile? _advisorSensorsProfile;
    private bool _advisorClosed;

    private void StartAdvisorMonitor(HardwareProfile profile)
    {
        if (_advisorClosed) return;
        // Hardware detectado de novo: o leitor é refeito com o clock base e a placa de vídeo atuais
        if (!ReferenceEquals(_advisorSensorsProfile, profile) && !_advisorReading)
        {
            _advisorSensors?.Dispose();
            _advisorSensors = new SensorReader(profile);
            _advisorSensorsProfile = profile;
        }
        if (_advisorMonitorTimer is null)
        {
            _advisorMonitorTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _advisorMonitorTimer.Tick += async (_, _) => await RefreshAdvisorMonitorAsync();
            Closed += (_, _) => ShutdownAdvisorMonitor();
        }
        _advisorMonitorTimer.Start();
        _ = RefreshAdvisorMonitorAsync();
    }

    /// <summary>Janela fechando: para tudo sem salvar gravação pela metade e libera os contadores.</summary>
    private void ShutdownAdvisorMonitor()
    {
        _advisorClosed = true;
        _advisorMonitorTimer?.Stop();
        var recording = _advisorRecording;
        _advisorRecording = null; _advisorRecorded = null;
        _advisorRecordingRegistration.Dispose();
        recording?.Dispose();
        if (!_advisorReading) { _advisorSensors?.Dispose(); _advisorSensors = null; }
    }

    private bool _advisorReading;

    private async Task RefreshAdvisorMonitorAsync()
    {
        // Fora da página (e sem gravação em andamento) o monitor para sozinho
        if (_advisorClosed || (_currentPage != "biosadvisor" && _advisorRecording is null)) { _advisorMonitorTimer?.Stop(); return; }
        if (_advisorReading || _advisorSensors is null) return;
        _advisorReading = true;
        var reader = _advisorSensors;
        // A leitura pertence à gravação em andamento quando começou; se ela acabar no meio, a amostra é descartada
        var target = _advisorRecorded;
        try
        {
            var s = await Task.Run(reader.Read);
            if (target != null && ReferenceEquals(target, _advisorRecorded)) target.Add(s);
            if (!_advisorClosed && _currentPage == "biosadvisor") ShowSensorSnapshot(s);
        }
        finally
        {
            _advisorReading = false;
            if (_advisorClosed) { reader.Dispose(); _advisorSensors = null; }
        }
    }

    private void ShowSensorSnapshot(SensorSnapshot s)
    {
        var na = T("Indisponível", "Unavailable");
        void Set(string key, double? value, string format)
        {
            if (_advisorMonitorValues.TryGetValue(key, out var tb)) tb.Text = value is { } v ? string.Format(System.Globalization.CultureInfo.InvariantCulture, format, v) : na;
        }
        Set("cpuTemp", s.CpuTemperature, "{0:0} °C");
        Set("cpuClock", s.CpuClockMhz, "{0:0} MHz");
        Set("cpuUsage", s.CpuUsage, "{0:0}%");
        Set("cpuPower", s.CpuPowerWatts, "{0:0} W");
        if (_advisorMonitorValues.TryGetValue("cpuLimit", out var limit))
            limit.Text = s.CpuPerformanceLimit is { } l ? l < 99.5 ? T($"Limitada a {l:0}% (motivo não informado)", $"Limited to {l:0}% (reason not reported)") : T("Sem limitação", "Not limited") : na;
        Set("gpuTemp", s.GpuTemperature, "{0:0} °C");
        Set("gpuClock", s.GpuClockMhz, "{0:0} MHz");
        Set("gpuUsage", s.GpuUsage, "{0:0}%");
        Set("gpuPower", s.GpuPowerWatts, "{0:0} W");
        Set("gpuLimit", s.GpuPowerLimitWatts, "{0:0} W");
        if (_advisorMonitorValues.TryGetValue("reasons", out var reasons))
            reasons.Text = s.GpuThrottleReasons switch
            {
                { Count: > 0 } list => T("GPU reduzindo clock por: ", "GPU reducing clocks due to: ") + string.Join(", ", list.Select(L)),
                { } => T("GPU sem limitação de clock no momento.", "GPU not throttling right now."),
                null when s.GpuTemperature is null => T("Leituras de GPU detalhadas só para placas NVIDIA (nvidia-smi do driver).", "Detailed GPU readings only for NVIDIA cards (driver's nvidia-smi)."),
                _ => T("Motivo de limitação da GPU não informado pelo driver.", "GPU throttle reason not reported by the driver."),
            };
    }

    // ================= Antes / depois =================
    private Border AdvisorBenchmarkCard(AdvisorReport report)
    {
        var state = Advisor.State;
        var panel = new StackPanel();
        panel.Children.Add(FeatureHeader(Glyphs.Speed, "Accent", T("Benchmark antes / depois", "Before / after benchmark"),
            T("1) Antes de mudar a BIOS, rode o mesmo trecho de jogo gravando os frametimes com o PresentMon ou o CapFrameX e importe o CSV em ANTES. Se quiser, grave os sensores enquanto joga. 2) Aplique as mudanças, repita o mesmo teste e importe em DEPOIS. Só métricas medidas nos dois testes são comparadas.",
              "1) Before changing the BIOS, play the same game section while recording frametimes with PresentMon or CapFrameX, and import the CSV into BEFORE. Optionally record sensors while playing. 2) Apply the changes, repeat the same test and import into AFTER. Only metrics measured in both tests are compared."),
            state.Baseline != null && state.After != null ? T("Pronto para comparar", "Ready to compare") : T("Aguardando testes", "Waiting for tests"), state.Baseline != null && state.After != null ? "Success" : "Info"));
        var grid = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, -14, 0) }, 380, 2);
        grid.Children.Add(BenchmarkSlotPanel(report, BenchmarkSlot.Baseline, state.Baseline));
        grid.Children.Add(BenchmarkSlotPanel(report, BenchmarkSlot.After, state.After));
        panel.Children.Add(grid);

        if (state.Baseline is { } before && state.After is { } after)
        {
            var deltas = BenchmarkComparer.Compare(before, after);
            panel.Children.Add(SectionLabel(T("RESULTADO", "RESULT")));
            if (deltas.Count == 0) panel.Children.Add(Label(T("Os dois testes não têm métricas em comum para comparar.", "The two tests have no metrics in common to compare."), 12, true));
            var results = new WrapPanel();
            foreach (var d in deltas)
            {
                var better = d.HigherIsBetter ? d.Change > 0 : d.Change < 0;
                var sign = d.Change > 0 ? "+" : "";
                var text = d.IsPercent ? $"{sign}{d.Change:0.0}% {L(d.Metric)}" : $"{sign}{d.Change:0.#} {d.Unit} {L(d.Metric)}";
                var pill = Pill(text, Math.Abs(d.Change) < 0.05 ? "Info" : better ? "Success" : "Danger");
                pill.Margin = new Thickness(0, 0, 8, 8); pill.ToolTip = $"{d.Before:0.#} → {d.After:0.#} {d.Unit}";
                results.Children.Add(pill);
            }
            panel.Children.Add(results);
            var note = Label(T("Diferenças de 1–3% podem ser variação normal entre execuções: repita cada teste 2–3 vezes no mesmo trecho.", "Differences of 1–3% can be normal run-to-run variation: repeat each test 2–3 times on the same section."), 11.5, true);
            panel.Children.Add(note);
            var clear = IconButton(Glyphs.Delete, T("Apagar testes", "Clear tests"));
            clear.HorizontalAlignment = HorizontalAlignment.Left;
            clear.Click += (_, _) => { Advisor.ClearBenchmarks(); KeepScroll(ShowBiosAdvisor); };
            panel.Children.Add(clear);
        }
        return Surface(panel);
    }

    private Border BenchmarkSlotPanel(AdvisorReport report, BenchmarkSlot slot, BenchmarkRun? run)
    {
        var panel = new StackPanel();
        var title = Label(slot == BenchmarkSlot.Baseline ? T("ANTES (baseline)", "BEFORE (baseline)") : T("DEPOIS (after optimization)", "AFTER (after optimization)"), 13);
        title.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(title);
        if (run is null) panel.Children.Add(Label(T("Nenhum teste salvo.", "No test saved."), 12, true));
        else
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            void Metric(string name, double? value, string format)
            {
                if (value is null) return;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
                var v = new TextBlock { Text = string.Format(culture, format, value), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Tag = Translator.SystemDataTag };
                v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                DockPanel.SetDock(v, Dock.Right); row.Children.Add(v);
                var n = new TextBlock { Text = name, FontSize = 12 };
                n.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                row.Children.Add(n);
                panel.Children.Add(row);
            }
            Metric(T("FPS médio", "Average FPS"), run.AverageFps, "{0:0}");
            Metric("1% low", run.Low1Fps, "{0:0}");
            Metric("0.1% low", run.Low01Fps, "{0:0}");
            Metric(T("Frametime médio", "Average frametime"), run.AverageFrametimeMs, "{0:0.00} ms");
            Metric(T("CPU máx.", "CPU max"), run.CpuTempMax, "{0:0} °C");
            Metric(T("GPU máx.", "GPU max"), run.GpuTempMax, "{0:0} °C");
            Metric(T("Uso médio da CPU", "Average CPU usage"), run.CpuUsageAvg, "{0:0}%");
            Metric(T("Uso médio da GPU", "Average GPU usage"), run.GpuUsageAvg, "{0:0}%");
            var info = new TextBlock { Text = string.Join(" · ", new[] { run.Application, run.Frames is { } f ? T($"{f} quadros", $"{f} frames") : null, run.SensorSeconds is { } s ? T($"{s} leituras de sensores", $"{s} sensor readings") : null, "BIOS " + run.BiosVersion }.Where(x => !string.IsNullOrEmpty(x))), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Tag = Translator.SystemDataTag };
            info.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            panel.Children.Add(info);
        }
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var import = IconButton(Glyphs.Folder, T("Importar CSV de frametimes", "Import frametime CSV"));
        import.Margin = new Thickness(0, 0, 8, 6);
        import.Click += (_, _) => ImportFrametimes(report, slot, run);
        actions.Children.Add(import);
        var recording = _advisorRecording != null && _advisorRecordingSlot == slot;
        var record = IconButton(recording ? Glyphs.Stop : Glyphs.Play, recording ? T("Parar e salvar sensores", "Stop and save sensors") : T("Gravar sensores enquanto joga", "Record sensors while playing"), primary: recording);
        record.Margin = new Thickness(0, 0, 8, 6);
        record.IsEnabled = _advisorRecording is null || recording;
        record.Click += (_, _) => { if (recording) StopSensorRecording(report); else StartSensorRecording(report, slot); };
        actions.Children.Add(record);
        panel.Children.Add(actions);
        var box = ListRow(panel); box.Margin = new Thickness(0, 0, 14, 10);
        return box;
    }

    private async void ImportFrametimes(AdvisorReport report, BenchmarkSlot slot, BenchmarkRun? existing)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "CSV (PresentMon / CapFrameX)|*.csv", Title = T("Escolha o CSV de frametimes", "Choose the frametime CSV") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            // Logs do PresentMon podem ter centenas de MB: lidos fora da thread da interface
            var file = dialog.FileName;
            var metrics = await Task.Run(() => FrametimeAnalyzer.ParseFile(file));
            if (_advisorClosed) return;
            // Pega o teste salvo agora (pode ter mudado enquanto o arquivo era lido)
            var current = (slot == BenchmarkSlot.Baseline ? Advisor.State.Baseline : Advisor.State.After) ?? existing;
            var run = (current ?? new BenchmarkRun { Slot = slot, BiosVersion = report.Profile.Bios.Version }).WithFrames(metrics, Path.GetFileName(file));
            Advisor.SaveBenchmark(run);
            ShowToast("BIOS Advisor", T($"{metrics.Frames} quadros importados.", $"{metrics.Frames} frames imported."), "Success");
            if (_currentPage == "biosadvisor") KeepScroll(ShowBiosAdvisor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or OutOfMemoryException)
        {
            if (!_advisorClosed) ShowToast("BIOS Advisor", ex.Message, "Danger");
        }
    }

    private CancellationTokenRegistration _advisorRecordingRegistration;

    private void StartSensorRecording(AdvisorReport report, BenchmarkSlot slot)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        _advisorRecording = cts;
        _advisorRecordingSlot = slot;
        _advisorRecorded = new List<SensorSnapshot>();
        // Durante a gravação o monitor lê a cada segundo
        StartAdvisorMonitor(report.Profile);
        if (_advisorMonitorTimer != null) _advisorMonitorTimer.Interval = TimeSpan.FromSeconds(1);
        // Limite de 10 minutos: encerra só esta gravação (outra iniciada depois não é afetada)
        _advisorRecordingRegistration = cts.Token.Register(() => Dispatcher.BeginInvoke(() =>
        {
            if (!_advisorClosed && ReferenceEquals(_advisorRecording, cts)) StopSensorRecording(report);
        }));
        ShowToast("BIOS Advisor", T("Gravando sensores. Jogue o trecho de teste e depois clique em Parar (máximo 10 minutos).", "Recording sensors. Play the test section, then click Stop (10 minutes max)."), "Info");
        KeepScroll(ShowBiosAdvisor);
    }

    private void StopSensorRecording(AdvisorReport report)
    {
        var samples = _advisorRecorded ?? new List<SensorSnapshot>();
        var slot = _advisorRecordingSlot;
        var cts = _advisorRecording;
        _advisorRecording = null; _advisorRecorded = null;
        _advisorRecordingRegistration.Dispose();
        cts?.Dispose();
        if (_advisorMonitorTimer != null) _advisorMonitorTimer.Interval = TimeSpan.FromSeconds(3);
        if (samples.Count < 5) { ShowToast("BIOS Advisor", T("Gravação curta demais: nada foi salvo.", "Recording too short: nothing was saved."), "Warning"); }
        else
        {
            var existing = slot == BenchmarkSlot.Baseline ? Advisor.State.Baseline : Advisor.State.After;
            Advisor.SaveBenchmark((existing ?? new BenchmarkRun { Slot = slot, BiosVersion = report.Profile.Bios.Version }).WithSensors(samples));
            ShowToast("BIOS Advisor", T($"{samples.Count} leituras de sensores salvas.", $"{samples.Count} sensor readings saved."), "Success");
        }
        if (_currentPage == "biosadvisor") KeepScroll(ShowBiosAdvisor);
    }
}
