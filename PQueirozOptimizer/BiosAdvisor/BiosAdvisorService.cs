using PQueirozOptimizer.BiosAdvisor.Detector;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>
/// Liga detector, motor e armazenamento, e registra o que aconteceu no log de atividade.
/// O log traz só modelos e versões (nada de número de série ou dado pessoal).
/// A detecção roda fora da thread da interface e fica em cache durante a sessão.
/// </summary>
public sealed class BiosAdvisorService
{
    private readonly ActivityLog _log;
    private readonly HardwareDetector _detector;
    private Task<HardwareProfile>? _detection;

    public BiosAdvisorStore Store { get; }
    public BiosAdvisorState State { get; }

    public BiosAdvisorService(ActivityLog log, HardwareDetector? detector = null, BiosAdvisorStore? store = null)
    {
        _log = log;
        _detector = detector ?? new HardwareDetector();
        Store = store ?? new BiosAdvisorStore();
        State = Store.Load();
        _log.Write("INFO", "BIOS Advisor initialized");
    }

    public HardwareProfile? Profile => _detection is { IsCompletedSuccessfully: true } t ? t.Result : null;

    /// <summary>
    /// Detecta uma vez por sessão; <paramref name="refresh"/> força uma nova leitura. Uma falha fica guardada até o
    /// usuário pedir de novo (senão cada redesenho da página dispararia outra tentativa).
    /// </summary>
    public Task<HardwareProfile> DetectAsync(bool refresh = false)
    {
        if (refresh || _detection is null)
            _detection = Task.Run(() => LogDetected(_detector.Detect()));
        return _detection;
    }

    /// <summary>Usado pelos testes de tela: injeta um perfil pronto sem tocar no hardware.</summary>
    public void UseProfile(HardwareProfile profile) => _detection = Task.FromResult(profile);

    private HardwareProfile LogDetected(HardwareProfile p)
    {
        _log.Write("INFO", "Hardware detected");
        _log.Write("INFO", $"Motherboard detected: {p.Motherboard.Manufacturer} {p.Motherboard.Product} · BIOS {p.Bios.Version}");
        _log.Write("INFO", $"CPU detected: {p.Cpu.Name} ({p.Cpu.Cores}C/{p.Cpu.Threads}T)");
        _log.Write("INFO", $"Memory detected: {p.Memory.Modules.Count} module(s), {p.Memory.TotalBytes / 1024 / 1024 / 1024} GB, {p.Memory.ConfiguredMhz} MHz");
        foreach (var g in p.Gpus) _log.Write("INFO", $"GPU detected: {g.Name} (driver {g.DriverVersion})");
        return p;
    }

    public AdvisorReport Analyze(HardwareProfile profile)
    {
        _log.Write("INFO", $"Rules loaded: {BiosAdvisorEngine.Rules.Count}");
        var report = BiosAdvisorEngine.Analyze(profile, State.Preset, BiosAdvisorStore.ValidConfirmations(State, profile));
        _log.Write("INFO", $"Recommendations generated: {report.Recommendations.Count} ({report.Preset}, score {report.Score.Overall?.ToString() ?? "n/a"})");
        return report;
    }

    public void SetPreset(AdvisorPreset preset) { State.Preset = preset; Store.Save(State); }

    /// <summary>Marca/desmarca um item como conferido na BIOS (vale para esta placa e versão de BIOS).</summary>
    public void SetConfirmed(HardwareProfile profile, string recommendationId, bool confirmed)
    {
        if (State.ConfirmationsFingerprint != profile.Fingerprint) { State.Confirmed.Clear(); State.ConfirmationsFingerprint = profile.Fingerprint; }
        if (confirmed) State.Confirmed.Add(recommendationId); else State.Confirmed.Remove(recommendationId);
        Store.Save(State);
    }

    public void SaveBenchmark(Benchmark.BenchmarkRun run)
    {
        if (run.Slot == Benchmark.BenchmarkSlot.Baseline) State.Baseline = run; else State.After = run;
        Store.Save(State);
        _log.Write("INFO", $"BIOS Advisor: benchmark {(run.Slot == Benchmark.BenchmarkSlot.Baseline ? "baseline" : "after")} saved");
    }

    public void ClearBenchmarks() { State.Baseline = null; State.After = null; Store.Save(State); }
}
