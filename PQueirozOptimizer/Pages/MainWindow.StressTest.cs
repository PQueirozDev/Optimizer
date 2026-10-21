using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Stress test de CPU e memória com conferência de erros: memória (padrão escrito e relido), WHEA e reinícios do
/// driver de vídeo durante o teste. Fica no processo (estático), então continua ao trocar de página ou de idioma.
/// </summary>
public partial class MainWindow
{
    private static readonly StressTestService Stress = new();
    private static DateTime _stressStarted;
    private static TimeSpan? _stressLimit = TimeSpan.FromMinutes(15);
    private static bool _stressCpu = true, _stressRam = true;
    private static HardwareErrors? _stressErrors;
    private static string? _stressVerdict;
    private static double _stressMaxGpuTemp;
    private DispatcherTimer? _stressTimer;
    // Vigia do teste fora da página: aplica o limite de tempo e relê o log mesmo com outra página aberta
    private static DispatcherTimer? _stressWatch;

    private static readonly (string Pt, string En, TimeSpan? Limit)[] StressDurations =
    {
        ("5 min", "5 min", TimeSpan.FromMinutes(5)), ("15 min", "15 min", TimeSpan.FromMinutes(15)), ("30 min", "30 min", TimeSpan.FromMinutes(30)),
        ("1 hora", "1 hour", TimeSpan.FromHours(1)), ("Sem limite", "No limit", null),
    };

    private void ShowStressTest()
    {
        PageTitle.Text = "Stress test";
        PageBadge.Visibility = Visibility.Collapsed;
        _stressTimer?.Stop();
        var root = new StackPanel();
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);

        // ---------- Configuração ----------
        var setup = new StackPanel();
        setup.Children.Add(CardHeader(Glyphs.Lightning, L("TESTE DE ESTABILIDADE", "STABILITY TEST"), L("CPU e memória", "CPU and memory"), null, titleIsData: false));
        var intro = Label(L("Carrega a CPU em todos os núcleos e enche a memória livre com um padrão que é conferido a cada passada. Use depois de mexer em XMP/EXPO, overclock ou undervolt: um PC estável termina com zero erros de memória, zero erros WHEA e nenhum reinício do driver de vídeo.",
            "Loads every CPU core and fills free memory with a pattern that is checked on every pass. Use it after changing XMP/EXPO, an overclock or an undervolt: a stable PC finishes with zero memory errors, zero WHEA errors and no graphics driver resets."), 12.5, true);
        intro.Tag = Translator.SystemDataTag;
        setup.Children.Add(intro);
        var cpu = new CheckBox { Content = L("CPU (todos os núcleos)", "CPU (all cores)"), IsChecked = _stressCpu, Margin = new Thickness(0, 8, 0, 8), IsEnabled = !Stress.Running };
        cpu.SetResourceReference(StyleProperty, "SwitchCheckBox");
        var ram = new CheckBox { Content = L("Memória (60% da RAM livre, com conferência de erros)", "Memory (60% of free RAM, with error checking)"), IsChecked = _stressRam, Margin = new Thickness(0, 0, 0, 12), IsEnabled = !Stress.Running };
        ram.SetResourceReference(StyleProperty, "SwitchCheckBox");
        cpu.Checked += (_, _) => _stressCpu = true; cpu.Unchecked += (_, _) => _stressCpu = false;
        ram.Checked += (_, _) => _stressRam = true; ram.Unchecked += (_, _) => _stressRam = false;
        setup.Children.Add(cpu); setup.Children.Add(ram);
        var selected = Array.FindIndex(StressDurations, d => d.Limit == _stressLimit);
        var duration = Segmented(StressDurations.Select(d => L(d.Pt, d.En)).ToArray(), selected, i => { _stressLimit = StressDurations[i].Limit; KeepScroll(ShowStressTest); });
        duration.IsEnabled = !Stress.Running;
        setup.Children.Add(SettingRow(L("Duração", "Duration"), L("Para sozinho ao fim do tempo.", "Stops by itself when time is up."), duration));
        var start = IconButton(Stress.Running ? Glyphs.Stop : Glyphs.Play, Stress.Running ? L("Parar teste", "Stop test") : L("Iniciar teste", "Start test"), primary: true);
        start.HorizontalAlignment = HorizontalAlignment.Left;
        start.Click += (_, _) =>
        {
            if (Stress.Running) FinishStress(L("Parado por você.", "Stopped by you."));
            else StartStress();
            KeepScroll(ShowStressTest);
        };
        setup.Children.Add(start);
        root.Children.Add(Surface(setup));

        // ---------- Ao vivo ----------
        var live = HomeGrid(200);
        var elapsed = StatTile(L("TEMPO", "TIME"));
        var cpuLoad = StatTile(L("USO DA CPU", "CPU USAGE"));
        var clock = StatTile(L("CLOCK DA CPU", "CPU CLOCK"));
        var gpuTemp = StatTile(L("GPU · TEMPERATURA", "GPU · TEMPERATURE"));
        var passes = StatTile(L("PASSADAS DA MEMÓRIA", "MEMORY PASSES"));
        var memErrors = StatTile(L("ERROS DE MEMÓRIA", "MEMORY ERRORS"));
        var whea = StatTile(L("ERROS WHEA", "WHEA ERRORS"));
        var tdr = StatTile(L("REINÍCIOS DO VÍDEO", "GRAPHICS RESETS"));
        foreach (var tile in new[] { elapsed, cpuLoad, clock, gpuTemp, passes, memErrors, whea, tdr }) live.Children.Add(tile.Card);
        root.Children.Add(HomeBandTitle(L("Ao vivo", "Live"), live: Stress.Running));
        root.Children.Add(live);

        var verdictHost = new StackPanel();
        root.Children.Add(verdictHost);
        var note = Label(L("A temperatura da CPU não aparece: lê-la exige um driver de kernel (como o WinRing0), que o Windows Defender bloqueia por ser vulnerável. Acompanhe pelo software da placa-mãe ou pelo HWiNFO durante o teste.",
            "CPU temperature isn't shown: reading it requires a kernel driver (such as WinRing0), which Windows Defender blocks as vulnerable. Watch it in the motherboard software or HWiNFO during the test."), 12, true);
        note.Tag = Translator.SystemDataTag;
        root.Children.Add(note);

        void Update()
        {
            var sample = HardwareMonitorService.Shared.Latest;
            var time = Stress.Running ? DateTime.Now - _stressStarted : TimeSpan.Zero;
            elapsed.Value.Text = Stress.Running ? $"{(int)time.TotalMinutes:00}:{time.Seconds:00}" + (_stressLimit is { } l ? $" / {(int)l.TotalMinutes}:00" : "") : "—";
            cpuLoad.Value.Text = sample is { } s ? $"{s.Cpu:0}%" : "—";
            clock.Value.Text = sample?.CpuMhz is { } mhz ? $"{mhz / 1000:0.00} GHz" : "—";
            if (sample?.GpuTempC is { } t && Stress.Running) _stressMaxGpuTemp = Math.Max(_stressMaxGpuTemp, t);
            gpuTemp.Value.Text = sample?.GpuTempC is { } gt ? $"{gt:0} °C" + (_stressMaxGpuTemp > 0 ? L($" (máx. {_stressMaxGpuTemp:0})", $" (max {_stressMaxGpuTemp:0})") : "") : "—";
            passes.Value.Text = _stressRam || Stress.MemoryPasses > 0 ? $"{Stress.MemoryPasses}" + (Stress.MemoryAllocatedBytes > 0 ? $" · {Stress.MemoryAllocatedBytes / 1e9:0.0} GB" : "") : "—";
            memErrors.Value.Text = (Stress.Running ? Stress.MemoryErrors : _stressMemErrors).ToString();
            whea.Value.Text = _stressErrors is { Error: null } e ? e.Whea.ToString() : "—";
            tdr.Value.Text = _stressErrors is { Error: null } e2 ? e2.GpuResets.ToString() : "—";
            verdictHost.Children.Clear();
            if (_stressVerdict != null && !Stress.Running) verdictHost.Children.Add(StressVerdictCard());
        }
        Update();
        // Timer desta montagem da página: o Unloaded da página antiga chega depois da nova montada e não pode parar o novo
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _stressTimer = timer;
        var wasRunning = Stress.Running;
        timer.Tick += (_, _) =>
        {
            // O teste terminou (tempo esgotado) com a página aberta: remonta para mostrar o resultado e liberar os controles
            if (wasRunning && !Stress.Running) { timer.Stop(); KeepScroll(ShowStressTest); return; }
            Update();
        };
        timer.Start();
        root.Unloaded += (_, _) => timer.Stop();
    }

    private (Border Card, TextBlock Value) StatTile(string label)
    {
        var stack = new StackPanel();
        var l = new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Tag = Translator.SystemDataTag };
        l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var v = new TextBlock { Text = "—", FontSize = 22, Margin = new Thickness(0, 6, 0, 0), Tag = Translator.SystemDataTag, TextTrimming = TextTrimming.CharacterEllipsis };
        v.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(l); stack.Children.Add(v);
        return (HomeCard(stack), v);
    }

    // Execução atual: leituras do log que chegam atrasadas, de uma execução anterior, são descartadas
    private static int _stressRun;
    private static DateTime _stressEnded;
    private static long _stressMemErrors, _stressMemPasses;
    private static bool _stressRamRequested, _stressMemoryFailed;

    private void StartStress()
    {
        if (!_stressCpu && !_stressRam) { ShowToast("Stress test", L("Escolha CPU, memória ou os dois.", "Pick CPU, memory or both."), "Warning"); return; }
        var run = ++_stressRun;
        _stressStarted = DateTime.Now;
        _stressErrors = null; _stressVerdict = null; _stressMaxGpuTemp = 0;
        _stressRamRequested = _stressRam;
        Stress.Start(_stressCpu, _stressRam);
        _stressWatch?.Stop();
        var watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _stressWatch = watch;
        var ticks = 0;
        watch.Tick += (_, _) =>
        {
            if (!Stress.Running || run != _stressRun) { watch.Stop(); return; }
            // Janela atual (a troca de idioma recria a janela; o teste continua)
            var window = Application.Current?.MainWindow as MainWindow ?? this;
            if (_stressLimit is { } limit && DateTime.Now - _stressStarted >= limit) { window.FinishStress(L("Tempo concluído.", "Time finished.")); return; }
            // Os erros do log são relidos a cada 15 s (consulta mais cara que os contadores); só valem para esta execução
            if (++ticks % 15 == 0)
            {
                var since = _stressStarted;
                _ = Task.Run(() => HardwareHealthService.ErrorsSince(since)).ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully && run == _stressRun && Stress.Running) _stressErrors = t.Result;
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        };
        watch.Start();
        OperationStatus.Text = L("Stress test em andamento…", "Stress test running…");
        _log.Write("INFO", $"Stress test iniciado (CPU {_stressCpu}, memória {_stressRam}, limite {_stressLimit?.TotalMinutes.ToString() ?? "nenhum"} min)");
    }

    /// <summary>Resultado: estável, instável ou inconclusivo (log ilegível ou memória que não chegou a ser testada).</summary>
    private static (string Kind, string Tone) StressOutcome(long memoryErrors, HardwareErrors errors) =>
        memoryErrors > 0 || errors.Whea > 0 || errors.GpuResets > 0 ? ("unstable", "Danger")
        : errors.Error != null || (_stressRamRequested && (_stressMemoryFailed || _stressMemPasses == 0)) ? ("inconclusive", "Warning")
        : ("stable", "Success");

    private void FinishStress(string reason)
    {
        _stressRun++; // descarta leituras do log ainda em andamento
        _stressWatch?.Stop();
        _stressMemErrors = Stress.MemoryErrors;
        _stressMemPasses = Stress.MemoryPasses;
        _stressMemoryFailed = Stress.MemoryFailed;
        Stress.Stop();
        _stressEnded = DateTime.Now;
        var errors = HardwareHealthService.ErrorsSince(_stressStarted);
        _stressErrors = errors;
        _stressVerdict = reason;
        var (kind, tone) = StressOutcome(_stressMemErrors, errors);
        OperationStatus.Text = kind switch
        {
            "stable" => L("Stress test concluído sem erros.", "Stress test finished with no errors."),
            "unstable" => L("Stress test encontrou erros.", "Stress test found errors."),
            _ => L("Stress test concluído, mas o resultado é inconclusivo.", "Stress test finished, but the result is inconclusive."),
        };
        _log.Write(kind == "stable" ? "INFO" : "WARN", $"Stress test terminado após {(_stressEnded - _stressStarted).TotalMinutes:0.0} min ({kind}): {_stressMemErrors} erros de memória em {_stressMemPasses} passadas, {errors.Whea} WHEA, {errors.GpuResets} reinícios do vídeo" + (errors.Error is { } e ? $", log ilegível: {e}" : ""));
        ShowToast("Stress test", kind switch
        {
            "stable" => L("Nenhum erro encontrado.", "No errors found."),
            "unstable" => L("Erros encontrados: veja o resultado.", "Errors found: see the result."),
            _ => L("Resultado inconclusivo: veja o motivo.", "Inconclusive result: see why."),
        }, tone);
    }

    private Border StressVerdictCard()
    {
        var e = _stressErrors ?? new HardwareErrors(0, 0, null, null);
        var (kind, tone) = StressOutcome(_stressMemErrors, e);
        var minutes = (_stressEnded - _stressStarted).TotalMinutes;
        var body = new StackPanel();
        var (title, pill) = kind switch
        {
            "stable" => (L("Estável", "Stable"), L("Sem erros", "No errors")),
            "unstable" => (L("Instável", "Unstable"), L("Com erros", "Errors")),
            _ => (L("Inconclusivo", "Inconclusive"), L("Sem conclusão", "No verdict")),
        };
        body.Children.Add(CardHeader(kind == "stable" ? Glyphs.Check : Glyphs.Warning, L("RESULTADO", "RESULT"), title, Pill(pill, tone), titleIsData: false));
        var text = kind switch
        {
            "stable" => L($"{_stressVerdict} Nenhum erro de memória, WHEA ou do driver de vídeo em {minutes:0} min. Para validar um overclock, deixe pelo menos 30 minutos.",
                $"{_stressVerdict} No memory, WHEA or graphics driver errors in {minutes:0} min. To validate an overclock, run at least 30 minutes."),
            "unstable" => L($"{_stressVerdict} {_stressMemErrors} erro(s) de memória, {e.Whea} erro(s) WHEA e {e.GpuResets} reinício(s) do driver de vídeo. Volte o último ajuste (XMP/EXPO, overclock ou undervolt) e teste de novo.",
                $"{_stressVerdict} {_stressMemErrors} memory error(s), {e.Whea} WHEA error(s) and {e.GpuResets} graphics driver reset(s). Undo the last change (XMP/EXPO, overclock or undervolt) and test again."),
            _ => e.Error != null
                ? L($"{_stressVerdict} Não foi possível ler o log do sistema ({e.Error}), então os erros de hardware não foram conferidos.", $"{_stressVerdict} The system log couldn't be read ({e.Error}), so hardware errors weren't checked.")
                : L($"{_stressVerdict} A memória não chegou a completar uma passada (pouca RAM livre ou teste curto demais). Feche programas e teste por mais tempo.", $"{_stressVerdict} Memory didn't complete a single pass (little free RAM or the test was too short). Close programs and test for longer."),
        };
        if (minutes < 10 && kind == "stable") text += L(" Este teste foi curto: erros raros podem levar mais tempo para aparecer.", " This test was short: rare errors can take longer to show up.");
        var l = Label(text, 12.5, true); l.Tag = Translator.SystemDataTag;
        body.Children.Add(l);
        return Surface(body);
    }
}
