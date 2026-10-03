using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PQueirozOptimizer.Services;

/// <summary>Opção de uma configuração de BIOS ("[01]Enabled").</summary>
public sealed record BiosOption(string Code, string Label);

/// <summary>Uma "Setup Question" do arquivo do SCEWIN.</summary>
public sealed class BiosSetting
{
    public string Question { get; init; } = "";
    public string Help { get; init; } = "";
    public string Token { get; init; } = "";
    public List<BiosOption> Options { get; } = new();
    public int OriginalIndex { get; set; } = -1;
    public int SelectedIndex { get; set; } = -1;
    /// <summary>Configuração numérica ("Value =&lt;5&gt;") em vez de lista de opções.</summary>
    public string? NumericValue { get; set; }
    public string? OriginalNumeric { get; set; }
    internal List<string> Lines { get; } = new();
    public bool Changed => Options.Count > 0 ? SelectedIndex != OriginalIndex : NumericValue != OriginalNumeric;
    public string SelectedLabel => SelectedIndex >= 0 && SelectedIndex < Options.Count ? Options[SelectedIndex].Label : NumericValue ?? "";
}

/// <summary>Ajuste recomendado: pergunta (por nome) e a opção desejada.</summary>
public sealed record BiosRecommendation(string QuestionPattern, string OptionPattern, string Why, string? RequiresQuestion = null, string? RequiresOption = null);

/// <summary>
/// Editor de BIOS pelo SCEWIN (AMISCE), a ferramenta da AMI para ler e gravar as configurações da BIOS pelo
/// Windows. O SCEWIN não pode ser distribuído com o app: o usuário aponta a pasta dele. O primeiro export é
/// guardado como cópia original e só os itens alterados são gravados de volta.
/// </summary>
public sealed class BiosService
{
    private readonly ActivityLog _log;
    public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "BIOS");
    private static readonly string ToolPathFile = Path.Combine(DataDirectory, "scewin.txt");
    public static string OriginalPath => Path.Combine(DataDirectory, "bios-original.txt");
    public static string CurrentPath => Path.Combine(DataDirectory, "bios-atual.txt");

    public BiosService(ActivityLog log) => _log = log;

    public static readonly BiosRecommendation[] Recommendations =
    {
        new(@"^Above 4G Decoding$", @"^Enabled$", "Necessário para a placa de vídeo usar toda a VRAM (Resizable BAR)."),
        new(@"^Re-?Size BAR( Support)?$", @"^(Enabled|Auto)$", "Resizable BAR: ganho de FPS em jogos compatíveis.", @"^CSM( Support)?$", @"^Disabled$"),
        new(@"Spread Spectrum", @"^Disabled$", "Clock de referência estável, sem oscilação proposital."),
    };

    // ---------- Ferramenta ----------
    public static string? ToolPath()
    {
        try
        {
            var saved = File.Exists(ToolPathFile) ? File.ReadAllText(ToolPathFile).Trim() : null;
            if (saved != null && File.Exists(saved)) return saved;
        }
        catch (IOException) { }
        var local = Path.Combine(AppContext.BaseDirectory, "SCEWIN", "SCEWIN_64.exe");
        return File.Exists(local) ? local : null;
    }

    public static void SetToolPath(string exePath)
    {
        if (!Path.GetFileName(exePath).StartsWith("SCEWIN", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Escolha o arquivo SCEWIN_64.exe.");
        EnsureDirectory();
        File.WriteAllText(ToolPathFile, exePath);
    }

    private static void EnsureDirectory()
    {
        if (!Directory.Exists(DataDirectory)) { Directory.CreateDirectory(DataDirectory); RegistryTweakStore.ProtectDirectory(DataDirectory); }
    }

    private static async Task<(int Code, string Output)> RunToolAsync(params string[] args)
    {
        var tool = ToolPath() ?? throw new FileNotFoundException("SCEWIN não configurado.");
        var psi = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(tool)! };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o SCEWIN.");
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } throw new TimeoutException("O SCEWIN não respondeu em 3 minutos."); }
        return (p.ExitCode, (await output + "\n" + await error).Trim());
    }

    /// <summary>Lê as configurações atuais da BIOS. Na primeira leitura guarda a cópia original.</summary>
    public async Task<List<BiosSetting>> ExportAsync()
    {
        EnsureDirectory();
        if (File.Exists(CurrentPath)) File.Delete(CurrentPath);
        var (code, output) = await RunToolAsync("/o", "/s", CurrentPath);
        if (!File.Exists(CurrentPath)) throw new InvalidOperationException($"O SCEWIN não exportou as configurações (código {code}). {Explain(output)}");
        if (!File.Exists(OriginalPath)) File.Copy(CurrentPath, OriginalPath);
        _log.Write("SUCCESS", "Configurações da BIOS lidas pelo SCEWIN");
        return Parse(File.ReadAllText(CurrentPath, Encoding.Latin1), out _);
    }

    /// <summary>Grava somente os itens alterados. Vale após reiniciar.</summary>
    public async Task<int> ImportChangesAsync(List<BiosSetting> settings)
    {
        var header = ParseHeader(File.ReadAllText(CurrentPath, Encoding.Latin1));
        var changed = settings.Where(s => s.Changed).ToList();
        if (changed.Count == 0) return 0;
        var path = Path.Combine(DataDirectory, "bios-alteracoes.txt");
        File.WriteAllText(path, Serialize(header, changed), Encoding.Latin1);
        var (code, output) = await RunToolAsync("/i", "/s", path);
        if (code != 0) throw new InvalidOperationException($"O SCEWIN recusou a gravação (código {code}). {Explain(output)}");
        _log.Write("SUCCESS", $"BIOS: {changed.Count} configuração(ões) gravada(s) — reinicie para aplicar");
        foreach (var s in changed) _log.Write("INFO", $"BIOS: {s.Question} → {s.SelectedLabel}");
        return changed.Count;
    }

    /// <summary>Grava de volta a cópia original feita na primeira leitura.</summary>
    public async Task RestoreOriginalAsync()
    {
        if (!File.Exists(OriginalPath)) throw new FileNotFoundException("Não há cópia original da BIOS. Leia as configurações pelo menos uma vez antes.");
        var (code, output) = await RunToolAsync("/i", "/s", OriginalPath);
        if (code != 0) throw new InvalidOperationException($"O SCEWIN recusou a restauração (código {code}). {Explain(output)}");
        _log.Write("SUCCESS", "BIOS: configurações originais gravadas — reinicie para aplicar");
    }

    private static string Explain(string output)
    {
        if (Regex.IsMatch(output, "password|senha", RegexOptions.IgnoreCase)) return "A BIOS tem senha de administrador; remova-a para gravar pelo Windows.";
        if (Regex.IsMatch(output, "not supported|Platform identification failed|not AMI", RegexOptions.IgnoreCase)) return "Esta placa-mãe não usa BIOS AMI compatível com o SCEWIN.";
        if (Regex.IsMatch(output, "driver|amifldrv", RegexOptions.IgnoreCase)) return "O driver do SCEWIN (amifldrv64.sys) não carregou: deixe-o na mesma pasta e desative a Integridade de Memória se necessário.";
        var last = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
        return last ?? "";
    }

    // ---------- Formato nvram.txt ----------
    private static readonly Regex OptionLine = new(@"^(?<lead>\s*(Options\s*=)?\s*)(?<star>\*?)\[(?<code>[^\]]+)\](?<label>.*?)\s*(//.*)?$");

    public static List<string> ParseHeader(string text) =>
        text.Replace("\r\n", "\n").Split('\n').TakeWhile(l => !l.StartsWith("Setup Question", StringComparison.Ordinal)).ToList();

    public static List<BiosSetting> Parse(string text, out List<string> header)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        header = lines.TakeWhile(l => !l.StartsWith("Setup Question", StringComparison.Ordinal)).ToList();
        var result = new List<BiosSetting>();
        List<string>? block = null;
        foreach (var line in lines.Skip(header.Count))
        {
            if (line.StartsWith("Setup Question", StringComparison.Ordinal))
            {
                if (block != null) result.Add(BuildSetting(block));
                block = new List<string>();
            }
            block?.Add(line);
        }
        if (block != null) result.Add(BuildSetting(block));
        return result;
    }

    private static string FieldValue(IEnumerable<string> block, string name) =>
        block.FirstOrDefault(l => Regex.IsMatch(l, $@"^{name}\s*=")) is { } line ? Regex.Replace(line[(line.IndexOf('=') + 1)..], @"\s*//.*$", "").Trim() : "";

    private static BiosSetting BuildSetting(List<string> block)
    {
        // Linhas em branco do fim pertencem à separação entre blocos
        while (block.Count > 0 && string.IsNullOrWhiteSpace(block[^1])) block.RemoveAt(block.Count - 1);
        var setting = new BiosSetting { Question = FieldValue(block, "Setup Question"), Help = FieldValue(block, "Help String"), Token = FieldValue(block, "Token") };
        setting.Lines.AddRange(block);
        var inOptions = false;
        foreach (var line in block)
        {
            if (Regex.IsMatch(line, @"^Options\s*=")) inOptions = true;
            else if (inOptions && !Regex.IsMatch(line, @"^\s+\*?\[")) inOptions = false;
            if (!inOptions) continue;
            var m = OptionLine.Match(line);
            if (!m.Success) continue;
            if (m.Groups["star"].Value == "*") setting.OriginalIndex = setting.SelectedIndex = setting.Options.Count;
            setting.Options.Add(new BiosOption(m.Groups["code"].Value, m.Groups["label"].Value.Trim()));
        }
        if (setting.Options.Count == 0 && block.FirstOrDefault(l => Regex.IsMatch(l, @"^Value\s*=")) is { } valueLine)
        {
            var v = Regex.Match(valueLine, @"<([^>]*)>");
            if (v.Success) setting.NumericValue = setting.OriginalNumeric = v.Groups[1].Value;
        }
        return setting;
    }

    /// <summary>Reescreve o bloco movendo o "*" para a opção escolhida (ou trocando o valor numérico).</summary>
    public static List<string> RenderBlock(BiosSetting setting)
    {
        var output = new List<string>();
        var index = -1;
        var inOptions = false;
        foreach (var line in setting.Lines)
        {
            if (Regex.IsMatch(line, @"^Options\s*=")) inOptions = true;
            else if (inOptions && !Regex.IsMatch(line, @"^\s+\*?\[")) inOptions = false;
            if (inOptions && OptionLine.IsMatch(line))
            {
                index++;
                var bracket = line.IndexOf('[');
                var starred = bracket > 0 && line[bracket - 1] == '*';
                // Na linha "Options =" o "*" fica colado ao "="; nas seguintes ocupa um espaço do recuo
                var first = Regex.IsMatch(line, @"^Options\s*=");
                var text = line;
                if (starred && index != setting.SelectedIndex)
                    text = first ? line.Remove(bracket - 1, 1) : line.Remove(bracket - 1, 1).Insert(bracket - 1, " ");
                else if (!starred && index == setting.SelectedIndex)
                    text = !first && line[bracket - 1] == ' ' ? line.Remove(bracket - 1, 1).Insert(bracket - 1, "*") : line.Insert(bracket, "*");
                output.Add(text);
                continue;
            }
            if (setting.Options.Count == 0 && setting.NumericValue != null && Regex.IsMatch(line, @"^Value\s*="))
            {
                output.Add(Regex.Replace(line, @"<[^>]*>", "<" + setting.NumericValue + ">"));
                continue;
            }
            output.Add(line);
        }
        return output;
    }

    public static string Serialize(List<string> header, IEnumerable<BiosSetting> settings)
    {
        var sb = new StringBuilder();
        foreach (var h in header) sb.Append(h).Append("\r\n");
        foreach (var s in settings)
        {
            foreach (var l in RenderBlock(s)) sb.Append(l).Append("\r\n");
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Aplica as recomendações na lista (sem gravar). Retorna quantas mudaram.</summary>
    public static int ApplyRecommendations(List<BiosSetting> settings)
    {
        var changed = 0;
        foreach (var r in Recommendations)
        {
            if (r.RequiresQuestion != null)
            {
                var requirement = settings.FirstOrDefault(s => Regex.IsMatch(s.Question, r.RequiresQuestion, RegexOptions.IgnoreCase));
                // Sem o pré-requisito (ex.: CSM ligado), o ajuste poderia impedir o Windows de iniciar
                if (requirement is null || !Regex.IsMatch(requirement.SelectedLabel, r.RequiresOption!, RegexOptions.IgnoreCase)) continue;
            }
            foreach (var s in settings.Where(s => Regex.IsMatch(s.Question, r.QuestionPattern, RegexOptions.IgnoreCase)))
            {
                var target = s.Options.FindIndex(o => Regex.IsMatch(o.Label, r.OptionPattern, RegexOptions.IgnoreCase));
                if (target >= 0 && target != s.SelectedIndex) { s.SelectedIndex = target; changed++; }
            }
        }
        return changed;
    }

    public static BiosRecommendation? RecommendationFor(BiosSetting s) =>
        Recommendations.FirstOrDefault(r => Regex.IsMatch(s.Question, r.QuestionPattern, RegexOptions.IgnoreCase));
}
