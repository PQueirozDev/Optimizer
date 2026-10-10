using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PQueirozOptimizer.Engine;
using PQueirozOptimizer.Services;

/// <summary>Qrztweaks 2.0: catálogo, Smart Optimize, Performance Lab, Optimization Lab, diagnóstico, perfis, atualizações e demonstração.</summary>
internal static class V2Tests
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        Catalog();
        Smart();
        Perf(root);
        Lab();
        Diagnostics();
        Profiles(root);
        Updates();
        Demo();
    }

    /// <summary>Estado de mentira para os testes: registro, serviços e apps em memória.</summary>
    sealed class FakeState : ISystemState
    {
        public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
        public object? Registry(RegistryHive hive, string key, string name) => Values.TryGetValue($"{hive}\\{key}\\{name}", out var v) ? v : null;
        public IReadOnlyList<string> SubKeys(RegistryHive hive, string key) => Array.Empty<string>();
        public int? ServiceStart(string name) => Services.TryGetValue(name, out var s) ? s : null;
        public Guid? ActivePowerPlan { get; set; } = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        public IReadOnlySet<string>? AppxPackages { get; set; } = new HashSet<string>();
        public IReadOnlyDictionary<string, bool>? TaskEnabled { get; set; } = new Dictionary<string, bool>();
        public bool OneDriveInstalled { get; set; }
        public void Set(RegistryHive hive, string key, string name, object value) => Values[$"{hive}\\{key}\\{name}"] = value;
    }

    static TweakContext Context(FakeState s, bool desktop = true, bool x3d = false) => new() { System = s, IsDesktop = desktop, DualCcdX3d = x3d, HasAmdGpu = false, WindowsBuild = 26200 };

    static void Catalog()
    {
        var all = TweakCatalog.All;
        Check(all.Select(t => t.Id).Distinct().Count() == all.Count, "Catálogo: identificadores únicos");
        Check(all.All(t => t.Name.Length > 0 && t.Description.Length > 0 && t.SideEffects.Length > 0 && t.Verification.Length > 0 && t.Revert.Length > 0 && t.Evidence.Length > 0 && t.Category.Length > 0),
            "Catálogo: todo ajuste tem descrição, efeitos colaterais, verificação, reversão e evidência");
        Check(all.All(t => t.Risk != StepRisk.High), "Catálogo: nenhum ajuste Avançado (alto risco) entra no Smart Optimize");

        // Sincronia com o script: cada etapa do catálogo existe e cada etapa do script está no catálogo ou na lista de exclusões
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Otimizador_de_PC.ps1"));
        var functions = new Dictionary<string, string> { ["padrao"] = "Otimizar-Padrao", ["gamer"] = "Otimizar-Gamer", ["debloat"] = "Otimizar-Debloat" };
        var scriptSteps = new Dictionary<string, (string Code, StepRisk Risk)>();
        foreach (var (op, function) in functions)
        {
            var start = script.IndexOf("function " + function + " {", StringComparison.Ordinal);
            var end = script.IndexOf("\nfunction ", start + 1, StringComparison.Ordinal);
            var body = script[start..(end < 0 ? script.Length : end)];
            var pattern = op == "debloat" ? @"Executar-Se-Confirmado\s+""[^""]*""\s*`?\s*""([^""]+)""" : @"@\{ Nome = ""([^""]+)""";
            var matches = Regex.Matches(body, pattern);
            for (var i = 0; i < matches.Count; i++)
            {
                var code = body[matches[i].Index..(i + 1 < matches.Count ? matches[i + 1].Index : body.Length)];
                scriptSteps[$"{op}|{matches[i].Groups[1].Value}"] = (code, PowerShellBridge.ClassifyRisk(code));
            }
        }
        var missingInScript = all.Where(t => !scriptSteps.ContainsKey($"{t.Operation}|{t.Step}")).Select(t => t.Id).ToList();
        Check(missingInScript.Count == 0, "Catálogo: toda etapa do catálogo existe no script" + (missingInScript.Count > 0 ? " (faltam: " + string.Join(", ", missingInScript) + ")" : ""));
        var uncovered = scriptSteps.Keys.Where(k => !all.Any(t => $"{t.Operation}|{t.Step}" == k) && !TweakCatalog.Excluded.ContainsKey(k)).ToList();
        Check(uncovered.Count == 0, "Catálogo: toda etapa do script está no catálogo ou excluída com motivo" + (uncovered.Count > 0 ? " (sem cobertura: " + string.Join(", ", uncovered) + ")" : ""));
        var riskMismatch = all.Where(t => scriptSteps.TryGetValue($"{t.Operation}|{t.Step}", out var s) && s.Risk != t.Risk).Select(t => t.Id).ToList();
        Check(riskMismatch.Count == 0, "Catálogo: risco igual ao marcador do script" + (riskMismatch.Count > 0 ? " (diferentes: " + string.Join(", ", riskMismatch) + ")" : ""));

        // Leituras de estado
        var s = new FakeState();
        var qrz = TweakCatalog.Find("power.qrz")!;
        Check(qrz.Read(Context(s)).State == TweakState.Recommended, "Estado: plano Equilibrado → Qrz recomendado");
        s.ActivePowerPlan = PowerPlanService.QrzGuid;
        Check(qrz.Read(Context(s)).State == TweakState.Applied, "Estado: plano Qrz ativo → já aplicado");
        s.ActivePowerPlan = Guid.NewGuid();
        Check(qrz.Read(Context(s)).State == TweakState.NeedsReview, "Estado: plano personalizado de outro programa → requer revisão");
        Check(qrz.Read(Context(s, desktop: false)).State == TweakState.NotApplicable, "Estado: notebook → plano Qrz não aplicável");
        Check(qrz.Read(Context(s, x3d: true)).State == TweakState.Incompatible && TweakCatalog.Find("game.dvr")!.Read(Context(s, x3d: true)).State == TweakState.Incompatible,
            "Estado: Ryzen X3D com dois CCDs → plano Qrz e gravação incompatíveis");
        var hags = TweakCatalog.Find("gpu.hags")!;
        s.Set(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2);
        Check(hags.Read(Context(s)).State == TweakState.Applied, "Estado: HwSchMode = 2 → HAGS já aplicado");
        var telemetry = TweakCatalog.Find("svc.telemetry")!;
        Check(telemetry.Read(Context(s)).State == TweakState.NotApplicable, "Estado: serviços inexistentes → não aplicável");
        s.Services["DiagTrack"] = 2; s.Services["WerSvc"] = 4;
        Check(telemetry.Read(Context(s)) is { State: TweakState.Recommended } r && r.Detail.Contains("DiagTrack") && !r.Detail.Contains("WerSvc"), "Estado: só os serviços ativos aparecem como pendentes");
        s.Services["DiagTrack"] = 4;
        Check(telemetry.Read(Context(s)).State == TweakState.Applied, "Estado: todos desativados → já aplicado");
        var bloat = TweakCatalog.Find("app.bloat")!;
        s.AppxPackages = new HashSet<string> { "Microsoft.BingNews", "Microsoft.WindowsCalculator" };
        Check(bloat.Read(Context(s)).State == TweakState.Recommended, "Estado: app da lista instalado → remoção recomendada");
        s.AppxPackages = null;
        Check(bloat.Read(Context(s)).State == TweakState.ReadFailed, "Estado: lista de apps ilegível → falha na leitura (não inventa)");
        Check(TweakCatalog.Like("Microsoft.BingWeather", "*BingWeather*") && !TweakCatalog.Like("Microsoft.People2", "Microsoft.People"), "Curinga igual ao -like do PowerShell");
        s.Set(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0);
        Check(TweakCatalog.Find("net.deliveryopt")!.Read(Context(s)).State == TweakState.Applied, "Estado: configuração mais restrita que a do ajuste conta como aplicada");
    }

    static MachineSummary Machine(bool laptop = false, bool gameSession = false, bool wsl = false) =>
        new("CPU", "GPU", "1.0", 16, "100 GB", "Placa", "Windows 11", 26200, "Equilibrado", laptop, false, wsl, false, false, gameSession, false);

    static void Smart()
    {
        var s = new FakeState();
        var allSteps = TweakCatalog.All.GroupBy(t => t.Operation).ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>)g.Select(t => t.Step).ToHashSet());
        IReadOnlyCollection<string> Steps(string op) => allSteps.TryGetValue(op, out var x) ? x : Array.Empty<string>();
        var full = new LicenseInfo("Teste", null, "X", "Standard", LicensePlans.Lifetime);
        var gaming = SmartOptimizer.Analyze(Machine(), Context(s), OptimizationGoal.CompetitiveGaming, full, Steps);
        Check(gaming.Items.Count == TweakCatalog.All.Count, "Smart: todo o catálogo é avaliado");
        Check(gaming.Items.First(i => i.Tweak.Id == "game.dvr").Preselected && !gaming.Items.First(i => i.Tweak.Id == "app.onedrive").Preselected, "Smart: gaming pré-seleciona a gravação e nunca a remoção do OneDrive");
        var dev = SmartOptimizer.Analyze(Machine(wsl: true), Context(s), OptimizationGoal.Development, full, Steps);
        Check(dev.Items.First(i => i.Tweak.Id == "app.bloat") is { Preselected: false, GoalNote: not null }, "Smart: programação não remove apps (Teams) e explica o motivo");
        Check(dev.Notices.Any(n => n.Title.StartsWith("Virtualização", StringComparison.Ordinal)), "Smart: WSL detectado → aviso de que a virtualização é preservada");
        var custom = SmartOptimizer.Analyze(Machine(), Context(s), OptimizationGoal.Custom, full, Steps);
        Check(custom.Items.All(i => !i.Preselected), "Smart: personalizado não marca nada");
        var basePlan = SmartOptimizer.Analyze(Machine(), Context(s), OptimizationGoal.CompetitiveGaming, new LicenseInfo("Teste", null, "X", "Standard", LicensePlans.Base), Steps);
        Check(basePlan.Items.Where(i => i.Tweak.Operation is "gamer" or "debloat").All(i => i.Locked && !i.Selectable && !i.Preselected) && basePlan.Items.Any(i => i.Tweak.Operation == "padrao" && !i.Locked),
            "Smart: plano Base bloqueia ajustes da Avançada e do Debloat (sem contornar a licença)");
        var noSteps = SmartOptimizer.Analyze(Machine(), Context(s), OptimizationGoal.CompetitiveGaming, full, _ => Array.Empty<string>());
        Check(noSteps.Items.All(i => i.State != TweakState.Recommended), "Smart: etapa que o script não oferece neste PC vira não aplicável");

        Check(SmartOptimizer.SelectionConflicts(new[] { "privacy.policies", "privacy.diagrequired" }, Machine()).Any(c => c.Blocking), "Conflito: telemetria configurada duas vezes bloqueia a aplicação");
        Check(SmartOptimizer.SelectionConflicts(new[] { "power.qrz" }, Machine(gameSession: true)).Any(c => c.Blocking), "Conflito: plano de energia com o Modo Jogo ativo bloqueia");
        Check(SmartOptimizer.SelectionConflicts(new[] { "game.mode", "gpu.hags" }, Machine()).Count == 0, "Conflito: seleção compatível não é bloqueada");

        var plan = SmartOptimizer.Plan(new[] { "game.dvr", "privacy.adid", "boot.faststartup" });
        Check(plan.Select(p => p.Operation).SequenceEqual(new[] { "padrao", "debloat", "gamer" }) && plan.All(p => p.Steps.Length == 1), "Smart: seleção agrupada por operação, na ordem Padrão → Debloat → Avançada");

        s.Set(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
        var verify = SmartOptimizer.Verify(new[] { "game.dvr", "maint.dns" }, Context(s));
        Check(verify.First(v => v.Tweak.Id == "game.dvr").Verified == false, "Verificação: só um dos valores mudou → não confirmado");
        s.Set(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
        verify = SmartOptimizer.Verify(new[] { "game.dvr", "maint.dns" }, Context(s));
        Check(verify.All(v => v.Verified), "Verificação: estado confirmado no registro e ação pontual registrada como executada");
        var failedRun = SmartOptimizer.Verify(new[] { "maint.dns" }, Context(s), new HashSet<string> { "debloat" });
        Check(!failedRun.Single().Verified, "Verificação: ação pontual de uma execução com falhas não é dada como concluída");
        Check(SmartOptimizer.IsDualCcdX3d("AMD Ryzen 9 7950X3D 16-Core Processor") && !SmartOptimizer.IsDualCcdX3d("AMD Ryzen 7 7800X3D 8-Core Processor"), "X3D com dois CCDs reconhecido pelo nome");
    }

    static void Perf(string root)
    {
        var frames = Enumerable.Repeat(10.0, 1000).ToList(); // 100 FPS estáveis
        frames[500] = 50; // uma travada
        var samples = Enumerable.Range(0, 10).Select(i => new PerfSample(i, 100, 10, 40, 97, 60, 70, 1800, 200, 4000, i < 3 ? 5 : 0)).ToList();
        var result = PerfMetrics.Build(new PerfResult { Label = "teste", Process = "cs2.exe", Source = "PresentMon" }, frames, samples);
        Check(result.HasFrames && result.Frames == 1000 && Math.Abs(result.AverageFps!.Value - 1000 / (10.04)) < 0.5, "Performance: FPS médio calculado dos frametimes reais");
        Check(result.P95FrametimeMs == 10 && result.P99FrametimeMs is >= 10 and < 11 && result.MinimumFps is >= 90 and <= 100, "Performance: P95, P99 e FPS mínimo por segundo");
        Check(result.GpuAvg == 97 && result.GpuTempMax == 70 && result.LimitedSeconds == 3, "Performance: média dos sensores e segundos com limite de desempenho");
        var noFrames = PerfMetrics.Build(new PerfResult { Label = "sensores" }, null, samples);
        Check(!noFrames.HasFrames && noFrames.AverageFps is null && noFrames.CpuAvg == 40, "Performance: sem quadros reais, FPS fica vazio (não é estimado)");
        Check(PerfMetrics.MinimumPerSecond(new[] { 500.0, 500, 250, 250, 250, 250 }) == 2, "Performance: FPS mínimo é o pior segundo completo");

        var after = result with { AverageFps = result.AverageFps * 1.1, Conditions = new CaptureConditions("CPU", "GPU", "2.0", "Win", 26200, "Qrz", "1920x1080", 144, "2.0.0") };
        var before = result with { Conditions = new CaptureConditions("CPU", "GPU", "1.0", "Win", 26200, "Equilibrado", "1920x1080", 144, "2.0.0") };
        var deltas = PerfMetrics.Compare(before, after);
        Check(deltas.First(d => d.Metric == "FPS médio") is { Percent: > 9.9 and < 10.1, HigherIsBetter: true }, "Comparação: diferença percentual do FPS médio");
        var diffs = PerfMetrics.ConditionDifferences(before, after);
        Check(diffs.Any(d => d.Contains("Driver")) && diffs.Any(d => d.Contains("Plano de energia")), "Comparação: avisa driver e plano de energia diferentes");

        var csv = PerfExport.ToCsv(result);
        Check(csv.Contains("avg_fps,") && csv.Contains("second,fps,frametime_ms") && PerfExport.ToJson(result).Contains("\"AverageFps\""), "Exportação: CSV com resumo e leituras por segundo; JSON completo");

        var store = new PerfLabStore(Path.Combine(root, "perflab"));
        store.Save(result);
        Check(store.List().Any(r => r.Id == result.Id && r.AverageFps == result.AverageFps), "Histórico: teste salvo e lido de volta");
        store.Delete(result.Id);
        Check(store.List().All(r => r.Id != result.Id), "Histórico: teste excluído");

        // Leitura incremental do CSV do PresentMon: linhas parciais esperam a próxima leitura
        var path = Path.Combine(root, "pm.csv");
        File.WriteAllText(path, "Application,ProcessID,MsBetweenPresents\ncs2.exe,1,10.0\nother.exe,2,5.0\ncs2.exe,1,1");
        var tail = new FrameCsvTail(path, "cs2.exe");
        var first = tail.ReadNew();
        File.AppendAllText(path, "2.0\ncs2.exe,1,8.0\n");
        var second = tail.ReadNew();
        Check(first.SequenceEqual(new[] { 10.0 }) && second.SequenceEqual(new[] { 12.0, 8.0 }) && tail.Frametimes.Count == 3, "PresentMon: leitura incremental só de linhas completas e do processo escolhido");
        // Dois fluxos do mesmo jogo (outro processo/swapchain) não se misturam; cabeçalho com aspas e outra caixa é aceito
        var multi = Path.Combine(root, "pm-multi.csv");
        File.WriteAllText(multi, "\"application\",\"processid\",\"swapchainaddress\",\"msbetweenpresents\"\n" +
            string.Concat(Enumerable.Range(0, 5).Select(_ => "game.exe,1,0xA,10\n")) + string.Concat(Enumerable.Range(0, 2).Select(_ => "game.exe,2,0xB,99\n")));
        var multiTail = new FrameCsvTail(multi, "game.exe");
        multiTail.ReadNew();
        Check(multiTail.Frametimes.Count == 5 && multiTail.Frametimes.All(ms => ms == 10), "PresentMon: só o fluxo principal (processo + swapchain) entra nas métricas; cabeçalho com aspas aceito");
        var (app, best) = PQueirozOptimizer.BiosAdvisor.Benchmark.FrametimeAnalyzer.BestStream(File.ReadAllText(multi));
        Check(app == "game.exe" && best.Count == 5, "Importação de CSV usa o mesmo fluxo principal");
        Check(PresentMonTool.Arguments("cs2.exe", "out.csv", 60).SequenceEqual(new[] { "--process_name", "cs2.exe", "--output_file", "out.csv", "--timed", "60", "--terminate_after_timed", "--stop_existing_session", "--no_console_stats", "--session_name", "QrztweaksPerfLab" }),
            "PresentMon: argumentos do PresentMon 2.x");
    }

    static PerfResult Run(double fps, double? temp = 70, string process = "cs2.exe") => new() { AverageFps = fps, Low1Fps = fps * 0.7, GpuTempMax = temp, CpuAvg = 40, Process = process, Conditions = new CaptureConditions("C", "G", "1", "W", 1, "P", "1920x1080", 144, "2") };

    static void Lab()
    {
        Check(Math.Abs(OptimizationLab.RegularizedIncompleteBeta(0.5, 1, 1) - 0.5) < 1e-9 && Math.Abs(OptimizationLab.RegularizedIncompleteBeta(0.3, 2, 3) - 0.3483) < 1e-3, "Lab: beta incompleta regularizada confere com valores conhecidos");
        Check(Math.Abs(OptimizationLab.LogGamma(5) - Math.Log(24)) < 1e-9, "Lab: ln Γ(5) = ln 24");
        var p = OptimizationLab.WelchPValue(new[] { 100.0, 101, 99 }, new[] { 110.0, 111, 109 });
        Check(p is < 0.01, $"Lab: diferença grande e consistente tem p pequeno (p = {p:0.####})");
        Check(OptimizationLab.WelchPValue(new[] { 100.0, 101, 99 }, new[] { 100.0, 101, 99 }) is > 0.99, "Lab: amostras iguais têm p ≈ 1");

        var insufficient = OptimizationLab.Evaluate(new[] { Run(100) }, new[] { Run(120) });
        Check(insufficient.Verdict == LabVerdict.InsufficientData, "Lab: menos de 3 gravações por fase = dados insuficientes");
        var small = OptimizationLab.Evaluate(new[] { Run(100), Run(101), Run(99) }, new[] { Run(101), Run(102), Run(100) });
        Check(small.Verdict == LabVerdict.NoProvenDifference, "Lab: diferença de 1% não é tratada como ganho comprovado");
        var gain = OptimizationLab.Evaluate(new[] { Run(100), Run(101), Run(99) }, new[] { Run(110), Run(111), Run(109) });
        Check(gain.Verdict == LabVerdict.LikelyGain && gain.DeltaPercent is > 9 and < 11, "Lab: ganho de 10% consistente = provável ganho");
        var loss = OptimizationLab.Evaluate(new[] { Run(110), Run(111), Run(109) }, new[] { Run(100), Run(101), Run(99) });
        Check(loss.Verdict == LabVerdict.LikelyLoss && loss.Recommendation.StartsWith("Reverter", StringComparison.Ordinal), "Lab: perda consistente recomenda reverter");
        var noisy = OptimizationLab.Evaluate(new[] { Run(80), Run(120), Run(100) }, new[] { Run(90), Run(130), Run(110) });
        Check(noisy.Verdict == LabVerdict.NoProvenDifference && noisy.Warnings.Any(w => w.Contains("Variação alta")), "Lab: variação alta avisa e não comprova ganho");
        var hot = OptimizationLab.Evaluate(new[] { Run(100, 60), Run(101, 61), Run(99, 60) }, new[] { Run(110, 75), Run(111, 76), Run(109, 75) });
        Check(hot.Warnings.Any(w => w.Contains("Temperatura")), "Lab: temperatura diferente entre as fases gera aviso");
        var mixed = OptimizationLab.Evaluate(new[] { Run(100), Run(101), Run(99, process: "other.exe") }, new[] { Run(110), Run(111), Run(109) });
        Check(mixed.Warnings.Any(w => w.Contains("mesmo processo")), "Lab: processos diferentes geram aviso");
    }

    static void Diagnostics()
    {
        var gpuBound = new PerfResult { DurationSeconds = 90, CpuAvg = 50, GpuAvg = 98, AverageFps = 120, Process = "cs2.exe", Conditions = new CaptureConditions("C", "G", "1", "W", 1, "P", "2560x1440", 144, "2") };
        var report = DiagnosticsEngine.Run(new DiagnosticInputs { LastCapture = gpuBound, RamTotalGb = 16 });
        Check(report.Findings.Any(f => f.Id == "bottleneck.gpu" && f.Confidence == DiagnosisConfidence.High && f.Evidence.Count > 0), "Diagnóstico: GPU a 98% num teste longo = limitado pela GPU, com evidência");
        var cpuBound = gpuBound with { GpuAvg = 60, AverageFps = 97 };
        Check(DiagnosticsEngine.Run(new DiagnosticInputs { LastCapture = cpuBound }).Findings.Any(f => f.Id == "bottleneck.cpu"), "Diagnóstico: GPU baixa e FPS abaixo do monitor = possível limitação de CPU");
        var capped = gpuBound with { GpuAvg = 60, AverageFps = 60 };
        Check(!DiagnosticsEngine.Run(new DiagnosticInputs { LastCapture = capped }).Findings.Any(f => f.Id == "bottleneck.cpu"), "Diagnóstico: FPS travado em 60 não é tratado como gargalo de CPU");
        var empty = DiagnosticsEngine.Run(new DiagnosticInputs());
        Check(!empty.Findings.Any(f => f.Id.StartsWith("bottleneck", StringComparison.Ordinal)) && empty.MissingData.Any(m => m.Contains("Performance Lab")), "Diagnóstico: sem teste, nenhum gargalo é afirmado e a falta de dados é informada");
        var disk = DiagnosticsEngine.Run(new DiagnosticInputs { SystemDriveFreeGb = 8, SystemDriveTotalGb = 500, Disks = new[] { new DiskReading("SSD", "SSD", "Warning", true) } });
        Check(disk.Findings.Any(f => f.Id == "disk.space") && disk.Findings.Any(f => f.Id == "disk.health" && f.Severity == DiagnosisSeverity.Critical), "Diagnóstico: pouco espaço e disco com alerta de saúde");
        Check(DiagnosticsEngine.Run(new DiagnosticInputs { PowerPlan = "Economia de energia", IsLaptop = false }).Findings.Any(f => f.Id == "power.saver"), "Diagnóstico: economia de energia num desktop");
        var ram = Enumerable.Range(0, 30).Select(_ => new HardwareSample(10, 93, 15, 16, null, 0, 0, null)).ToList();
        Check(DiagnosticsEngine.Run(new DiagnosticInputs { MonitorHistory = ram, RamTotalGb = 16 }).Findings.Any(f => f.Id == "ram.pressure" && f.Severity == DiagnosisSeverity.Critical), "Diagnóstico: RAM acima de 92% por 30 leituras = crítico");
        Check(DiagnosticsEngine.Run(new DiagnosticInputs { ProblemDevices = new[] { "Dispositivo X" } }).Findings.Any(f => f.Id == "drivers.problem"), "Diagnóstico: dispositivo com erro de driver");
        Check(DiagnosticsEngine.LooksCapped(143.5) && !DiagnosticsEngine.LooksCapped(97), "Diagnóstico: reconhece limites comuns de FPS");
    }

    static void Profiles(string root)
    {
        var (clean, warnings) = ProfileValidation.Sanitize(new UserProfile { Name = "  Teste  ", Tweaks = new[] { "game.mode", "nao.existe", "game.mode" }, Processes = new[] { "cs2.exe", "..\\evil.exe", "cmd /c del.exe" } });
        Check(clean.Name == "Teste" && clean.Tweaks.SequenceEqual(new[] { "game.mode" }) && clean.Processes.SequenceEqual(new[] { "cs2.exe" }) && warnings.Count == 2,
            "Perfis: importação descarta ajustes desconhecidos e processos inválidos");
        var threw = false;
        try { ProfileValidation.Sanitize(new UserProfile { Name = "x", Format = 99 }); } catch (InvalidDataException) { threw = true; }
        Check(threw, "Perfis: formato desconhecido é recusado");
        var file = Path.Combine(root, "perfil.json");
        File.WriteAllText(file, ProfileValidation.Export(clean with { Id = "fixo" }));
        var (imported, _) = ProfileValidation.Import(file);
        Check(imported.Name == "Teste" && imported.Id != "fixo", "Perfis: exportar e importar (o importado ganha um id novo)");
        File.WriteAllText(file, "{\"format\":1,\"name\":\"x\",\"script\":\"Remove-Item C:\\\\ -Recurse\"}");
        var (withScript, _) = ProfileValidation.Import(file);
        Check(withScript.Tweaks.Length == 0 && !ProfileValidation.Export(withScript).Contains("Remove-Item"), "Perfis: campos desconhecidos (como um script) são ignorados e não voltam ao exportar");
        File.WriteAllText(file, new string('a', ProfileValidation.MaxBytes + 1));
        threw = false;
        try { ProfileValidation.Import(file); } catch (InvalidDataException) { threw = true; }
        Check(threw, "Perfis: arquivo grande demais é recusado");

        var store = new ProfileStore(Path.Combine(root, "profiles.json"));
        File.Delete(Path.Combine(root, "profiles.json"));
        Check(store.Profiles.Any(p => p.Name == "Valorant") && store.Profiles.Any(p => p.Name == "Programação"), "Perfis: modelos iniciais (Valorant, CS2, Fortnite, Streaming, Programação, Notebook, Uso diário)");
        Check(ProfileTemplates.All().All(p => p.Tweaks.All(t => TweakCatalog.Find(t) is not null)), "Perfis: modelos só usam ajustes do catálogo");
        var mine = new UserProfile { Name = "Meu", Tweaks = new[] { "game.mode" }, Processes = new[] { "jogo.exe" }, AutoGameMode = true };
        store.Upsert(mine);
        var copy = store.Duplicate(mine.Id);
        Check(store.Profiles.Count(p => p.Name.StartsWith("Meu", StringComparison.Ordinal)) == 2 && copy.Id != mine.Id, "Perfis: duplicar cria outro perfil");
        store.Delete(copy.Id);
        Check(store.MatchRunning(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "JOGO.exe" })?.Id == mine.Id, "Perfis: jogo em execução encontra o perfil com Modo Jogo automático");

        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var events = new List<string>();
        using var watcher = new GameWatcher(() => running, store.MatchRunning, start: false);
        watcher.GameStarted += p => events.Add("start:" + p.Name);
        watcher.GameExited += _ => events.Add("exit");
        watcher.Poll();
        running.Add("jogo.exe"); watcher.Poll(); watcher.Poll();
        running.Clear(); watcher.Poll();
        Check(events.SequenceEqual(new[] { "start:Meu", "exit" }), "Perfis: jogo abriu → ativa uma vez; fechou → restaura");
    }

    static void Updates()
    {
        using var rsa = new RSACryptoServiceProvider(2048);
        var publicKey = Convert.ToBase64String(rsa.ExportCspBlob(false));
        var sums = Encoding.ASCII.GetBytes("abc123  Qrztweaks-Setup-v2.0.0.exe");
        var signature = Convert.ToBase64String(rsa.SignData(sums, CryptoConfig.MapNameToOID("SHA256")!));
        Check(UpdateService.VerifyChecksumSignature(sums, signature, publicKey), "Atualização: assinatura válida do arquivo de hashes é aceita");
        var tampered = (byte[])sums.Clone(); tampered[0] ^= 1;
        Check(!UpdateService.VerifyChecksumSignature(tampered, signature, publicKey), "Atualização: hash adulterado é recusado");
        Check(!UpdateService.VerifyChecksumSignature(sums, signature) && !UpdateService.VerifyChecksumSignature(sums, "não é base64"), "Atualização: assinatura de outra chave ou inválida é recusada");
        var hash = new string('a', 64);
        Check(UpdateService.FindHash($"{hash}  *Qrztweaks-Setup-v2.0.0.exe\n{new string('b', 64)}  outro.exe", "Qrztweaks-Setup-v2.0.0.exe") == hash, "Atualização: hash do instalador lido do SHA256SUMS");
        var unsigned = new UpdateInfo(true, "1", "2", null, "https://github.com/PQueirozDev/Optimizer/releases/download/v2/a.exe", "a.exe", "https://github.com/PQueirozDev/Optimizer/releases/download/v2/SHA256SUMS.txt");
        Check(!UpdateService.CanAutoInstall(unsigned) && UpdateService.CanAutoInstall(unsigned with { SignatureUrl = "https://github.com/PQueirozDev/Optimizer/releases/download/v2/SHA256SUMS.txt.sig" }),
            "Atualização: sem assinatura publicada, nada é instalado automaticamente");
    }

    static void Demo()
    {
        // Arquivo criado sem elevação (dono = usuário) não pode ser executado como ferramenta elevada
        var userFile = Path.Combine(Path.GetTempPath(), $"qrz-trust-{Guid.NewGuid():N}.exe");
        File.WriteAllText(userFile, "x");
        var elevated = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        try { if (!elevated) Check(!RegistryTweakStore.IsTrustedFile(userFile), "Ferramenta: arquivo de outro dono (não Administradores) é recusado"); }
        finally { File.Delete(userFile); }
        Check(!RegistryTweakStore.IsTrustedFile(Path.Combine(Path.GetTempPath(), "nao-existe.exe")), "Ferramenta: arquivo inexistente é recusado");
        try
        {
            PlanAccess.DemoMode = false;
            Check(!PlanAccess.Allows(null, "smart") && !PlanAccess.Allows(null, "dashboard"), "Licença: sem licença e sem demonstração, nada é liberado");
            PlanAccess.DemoMode = true;
            Check(PlanAccess.Allows(null, "smart") && PlanAccess.Allows(null, "diagnostics") && PlanAccess.Allows(null, "perflab"), "Demonstração: páginas de leitura abrem sem licença");
            Check(!PlanAccess.Allows(null, "optimization") && !PlanAccess.Allows(null, "gaming") && !PlanAccess.Allows(null, "services") && !PlanAccess.Allows(null, PlanAccess.OperationPage("padrao")),
                "Demonstração: otimizações, Modo Jogo e serviços continuam bloqueados (nada é aplicado)");
            var s = new FakeState();
            var analysis = SmartOptimizer.Analyze(Machine(), Context(s), OptimizationGoal.CompetitiveGaming, null, _ => TweakCatalog.All.Select(t => t.Step).ToHashSet());
            Check(analysis.Items.All(i => i.Locked && !i.Selectable), "Demonstração: o Smart Optimize mostra as recomendações, mas nenhum ajuste pode ser selecionado");
        }
        finally { PlanAccess.DemoMode = false; }
    }
}
