using System.IO;
using System.Text.Json;

namespace PQueirozOptimizer.Services;

public sealed record DefenderStatus(bool Available, bool RealTimeOn, bool TamperProtected, string[] Exclusions);

/// <summary>
/// Windows Defender para jogos: liga/desliga a proteção em tempo real e gerencia exclusões de pastas
/// (o jeito seguro de evitar que o antivírus escaneie os arquivos do jogo enquanto ele carrega).
/// </summary>
public sealed class DefenderService
{
    private readonly ActivityLog _log;
    public DefenderService(ActivityLog log) => _log = log;

    public static async Task<DefenderStatus> ReadStatusAsync()
    {
        try
        {
            var json = await PowerShellBridge.RunScriptAsync(
                "$s = Get-MpComputerStatus; $p = Get-MpPreference;" +
                "[pscustomobject]@{ On = [bool]$s.RealTimeProtectionEnabled; Tamper = [bool]$s.IsTamperProtected; Ex = @($p.ExclusionPath | Where-Object { $_ }) } | ConvertTo-Json -Compress",
                timeout: TimeSpan.FromSeconds(40));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var exclusions = root.GetProperty("Ex") switch
            {
                { ValueKind: JsonValueKind.Array } a => a.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0 && !s.StartsWith("N/A", StringComparison.Ordinal)).ToArray(),
                { ValueKind: JsonValueKind.String } s => new[] { s.GetString() ?? "" },
                _ => Array.Empty<string>(),
            };
            return new DefenderStatus(true, root.GetProperty("On").GetBoolean(), root.GetProperty("Tamper").GetBoolean(), exclusions);
        }
        // Defender desativado por outro antivírus ou ausente: os cmdlets falham
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or JsonException or KeyNotFoundException)
        {
            return new DefenderStatus(false, false, false, Array.Empty<string>());
        }
    }

    /// <summary>Liga ou desliga a proteção em tempo real. Com a Proteção contra Adulteração ativa, o Windows recusa.</summary>
    public async Task SetRealTimeAsync(bool on)
    {
        await PowerShellBridge.RunScriptAsync("Set-MpPreference -DisableRealtimeMonitoring ([bool]::Parse($env:PQO_OFF))",
            new Dictionary<string, string> { ["PQO_OFF"] = (!on).ToString() });
        var status = await ReadStatusAsync();
        if (status.RealTimeOn != on)
            throw new InvalidOperationException(status.TamperProtected
                ? "O Windows recusou a mudança porque a Proteção contra Adulteração está ativa. Desative-a em Segurança do Windows → Proteção contra vírus e ameaças → Gerenciar configurações e tente de novo."
                : "O Windows não aplicou a mudança na proteção em tempo real.");
        _log.Write(on ? "SUCCESS" : "WARN", on ? "Proteção em tempo real do Defender ligada" : "Proteção em tempo real do Defender desligada (o Windows a religa sozinho depois de um tempo)");
    }

    /// <summary>
    /// Motivo para recusar a exclusão, ou null se a pasta é específica o bastante. A exclusão é permanente
    /// (vale até ser removida), então uma unidade inteira, o Windows, Program Files ou a pasta do usuário
    /// deixariam boa parte do PC sem verificação de vírus. Downloads e Temp são onde arquivos baixados caem.
    /// </summary>
    public static string? ExclusionProblem(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "Escolha uma pasta.";
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return "Caminho inválido."; }
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return "Pastas de rede não podem ser excluídas.";
        if (Path.GetPathRoot(full) is { } root && string.Equals(Path.TrimEndingDirectorySeparator(root), full, StringComparison.OrdinalIgnoreCase))
            return "Não é possível excluir uma unidade inteira. Escolha a pasta do jogo.";

        static string? Known(Environment.SpecialFolder f) { var p = Environment.GetFolderPath(f); return string.IsNullOrEmpty(p) ? null : Path.TrimEndingDirectorySeparator(p); }
        var profile = Known(Environment.SpecialFolder.UserProfile);
        // Pastas que nunca podem ser excluídas, nem nada dentro delas
        var forbiddenTrees = new[] { Known(Environment.SpecialFolder.Windows), profile is null ? null : Path.Combine(profile, "Downloads"), Path.TrimEndingDirectorySeparator(Path.GetTempPath()) };
        foreach (var tree in forbiddenTrees.OfType<string>())
            if (string.Equals(full, tree, StringComparison.OrdinalIgnoreCase) || full.StartsWith(tree + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return "Esta pasta é do Windows ou recebe arquivos baixados; excluí-la abre espaço para vírus. Escolha a pasta do jogo.";
        // Pastas amplas: só uma subpasta delas (ex.: Program Files\Jogo) é aceita
        var broad = new[]
        {
            Known(Environment.SpecialFolder.ProgramFiles), Known(Environment.SpecialFolder.ProgramFilesX86), Known(Environment.SpecialFolder.CommonApplicationData),
            profile, profile is null ? null : Path.GetDirectoryName(profile), Known(Environment.SpecialFolder.ApplicationData), Known(Environment.SpecialFolder.LocalApplicationData),
            Known(Environment.SpecialFolder.Desktop), Known(Environment.SpecialFolder.MyDocuments),
        };
        if (broad.OfType<string>().Any(b => string.Equals(full, b, StringComparison.OrdinalIgnoreCase)))
            return "Esta pasta é ampla demais: excluí-la tira muitos programas da verificação. Escolha a pasta do jogo dentro dela.";
        return null;
    }

    public async Task AddExclusionAsync(string folder)
    {
        if (ExclusionProblem(folder) is { } problem) throw new InvalidOperationException(problem);
        await PowerShellBridge.RunScriptAsync("Add-MpPreference -ExclusionPath $env:PQO_PATH", new Dictionary<string, string> { ["PQO_PATH"] = folder });
        var status = await ReadStatusAsync();
        if (!status.Available || !status.Exclusions.Any(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("O Windows não aceitou a exclusão do Defender; a Proteção contra Adulteração pode estar ativa.");
        _log.Write("SUCCESS", $"Pasta excluída da verificação do Defender: {folder}");
    }

    public async Task RemoveExclusionAsync(string folder)
    {
        await PowerShellBridge.RunScriptAsync("Remove-MpPreference -ExclusionPath $env:PQO_PATH", new Dictionary<string, string> { ["PQO_PATH"] = folder });
        var status = await ReadStatusAsync();
        if (!status.Available || status.Exclusions.Any(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("O Windows não removeu a exclusão do Defender; a Proteção contra Adulteração pode estar ativa.");
        _log.Write("SUCCESS", $"Exclusão do Defender removida: {folder}");
    }
}
