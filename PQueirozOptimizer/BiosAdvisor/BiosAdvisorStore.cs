using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PQueirozOptimizer.BiosAdvisor.Benchmark;

namespace PQueirozOptimizer.BiosAdvisor;

/// <summary>
/// O que o BIOS Advisor guarda entre aberturas: preset escolhido, itens que o usuário conferiu na BIOS
/// (atrelados à placa + versão da BIOS) e os testes antes/depois. Fica em %LocalAppData%\PQueirozOptimizer\bios-advisor.
/// </summary>
public sealed class BiosAdvisorState
{
    public AdvisorPreset Preset { get; set; } = AdvisorPreset.Safe;
    /// <summary>Placa + BIOS das confirmações. Se a BIOS mudar, as confirmações deixam de valer.</summary>
    public string? ConfirmationsFingerprint { get; set; }
    public HashSet<string> Confirmed { get; set; } = new();
    public BenchmarkRun? Baseline { get; set; }
    public BenchmarkRun? After { get; set; }
    /// <summary>Última leitura da BIOS pelo SCEWIN (vale só para a mesma placa + versão de BIOS).</summary>
    public BiosReadings? Readings { get; set; }
}

public sealed class BiosAdvisorStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly string _path;

    public BiosAdvisorStore(string? path = null) =>
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "bios-advisor", "state.json");

    public BiosAdvisorState Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<BiosAdvisorState>(File.ReadAllText(_path), Json) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save(BiosAdvisorState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(state, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Confirmações válidas para este hardware; descarta as de outra placa/versão de BIOS.</summary>
    public static IReadOnlySet<string> ValidConfirmations(BiosAdvisorState state, HardwareProfile profile) =>
        state.ConfirmationsFingerprint == profile.Fingerprint ? state.Confirmed : new HashSet<string>();
}
