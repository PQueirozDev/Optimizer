using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PQueirozOptimizer.Services;

/// <summary>O que uma etapa faz com o sistema, para a tela de revisão avisar antes de aplicar.</summary>
public enum StepEffect { Backup, Irreversible, OneOff }

public sealed record OperationStep(string Name, StepEffect Effect);

public sealed class PowerShellBridge
{
    private readonly ActivityLog _log;
    private readonly string _scriptPath = Path.Combine(AppContext.BaseDirectory, "Otimizador_de_PC.ps1");
    public PowerShellBridge(ActivityLog log) => _log = log;

    /// <summary>
    /// Executa um script curto via -EncodedCommand. Valores externos (caminhos, nomes) devem
    /// ir em <paramref name="variables"/> e ser lidos no script como $env:NOME, nunca
    /// interpolados no texto, para evitar problemas de aspas e injeção de comandos.
    /// </summary>
    public static async Task<string> RunScriptAsync(string script, IReadOnlyDictionary<string, string>? variables = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var wrapped = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.Encoding]::UTF8;\n" + script;
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped)) })
            psi.ArgumentList.Add(argument);
        if (variables != null) foreach (var (name, value) in variables) psi.Environment[name] = value;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o PowerShell.");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? TimeSpan.FromMinutes(2));
        var output = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var error = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try { await process.WaitForExitAsync(timeoutSource.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw new TimeoutException("O comando do PowerShell excedeu o tempo limite.");
        }
        if (process.ExitCode != 0)
        {
            var message = (await error).Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? $"O PowerShell terminou com código {process.ExitCode}." : message);
        }
        return (await output).Trim();
    }

    public IReadOnlyList<OperationStep> GetSteps(string operation)
    {
        var function = operation switch { "padrao" => "Otimizar-Padrao", "gamer" or "gamerservicos" => "Otimizar-Gamer", "debloat" => "Otimizar-Debloat", _ => "" };
        if (function.Length == 0) return Array.Empty<OperationStep>();
        var script = File.ReadAllText(_scriptPath);
        var start = script.IndexOf("function " + function + " {", StringComparison.Ordinal);
        if (start < 0) throw new InvalidDataException("Plano de execução não encontrado.");
        var end = script.IndexOf("\nfunction ", start + 1, StringComparison.Ordinal);
        var body = script[start..(end < 0 ? script.Length : end)];
        var pattern = operation == "debloat" ? @"Executar-Se-Confirmado\s+""[^""]*""\s*`?\s*""([^""]+)""" : @"@\{ Nome = ""([^""]+)""";
        var matches = Regex.Matches(body, pattern);
        var steps = new List<OperationStep>();
        for (var i = 0; i < matches.Count; i++)
        {
            var name = matches[i].Groups[1].Value;
            if (name == "Arquivos temporarios removidos" || steps.Any(s => s.Name == name)) continue;
            // O efeito vem do código da própria etapa (do nome dela até o início da próxima)
            var code = body[matches[i].Index..(i + 1 < matches.Count ? matches[i + 1].Index : body.Length)];
            if (operation == "gamerservicos" && ChangesServices(code)) continue; // sem parar serviços: etapa de serviço não aparece (o script também bloqueia)
            steps.Add(new OperationStep(name, ClassifyStep(code)));
        }
        return steps;
    }

    /// <summary>Etapa que para, desativa ou muda o tipo de início de algum serviço.</summary>
    public static bool ChangesServices(string code) => Regex.IsMatch(code, @"\b(Stop-Service|Set-Service|Suspend-Service|Desativar-Servico)\b", RegexOptions.IgnoreCase);

    public static StepEffect ClassifyStep(string code) =>
        Regex.IsMatch(code, @"Registrar-Irreversivel|Remove-Item|Remove-Appx|cleanmgr", RegexOptions.IgnoreCase) ? StepEffect.Irreversible
        : Regex.IsMatch(code, @"Capturar-|Set-PoliticaDword|Desativar-Servico|Aplicar-EfeitosVisuais|Aplicar-Politicas", RegexOptions.IgnoreCase) ? StepEffect.Backup
        : StepEffect.OneOff;

    public async Task RunAsync(string operation, IReadOnlyList<string>? selectedSteps = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_scriptPath)) throw new FileNotFoundException("Script de operações não encontrado.", _scriptPath);
        _log.Write("INFO", $"Iniciando: {operation}");
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", _scriptPath, "-Operation", operation, "-UiMode" }) psi.ArgumentList.Add(argument);
        if (selectedSteps != null)
        {
            psi.ArgumentList.Add("-SelectedStepsBase64");
            psi.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(selectedSteps))));
        }
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o PowerShell.");
        // Cancelar encerra o PowerShell e o que ele abriu (sfc, dism, chkdsk...). O backup é gravado
        // a cada item capturado, então o que já foi aplicado continua podendo ser revertido.
        using var cancellation = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            // AggregateException: algum processo filho já tinha saído ou não pôde ser encerrado
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException) { }
        });
        async Task ReadAsync(StreamReader reader, string level)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                _log.Write(level, line);
                // Erros vão marcados para a tela destacá-los e contá-los como falha
                progress?.Report(level == "ERROR" ? "[ERRO] " + line : line);
            }
        }
        await Task.WhenAll(ReadAsync(process.StandardOutput, "INFO"), ReadAsync(process.StandardError, "ERROR"), process.WaitForExitAsync());
        if (cancellationToken.IsCancellationRequested)
        {
            _log.Write("WARN", $"Operação cancelada pelo usuário: {operation}");
            throw new OperationCanceledException(cancellationToken);
        }
        if (process.ExitCode != 0) throw new InvalidOperationException($"A execução terminou com falhas (código {process.ExitCode}). Consulte a atividade e a reversão para alterações parciais.");
        _log.Write("SUCCESS", $"Execução concluída: {operation}");
    }
}


