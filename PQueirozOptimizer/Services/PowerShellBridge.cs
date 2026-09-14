using System.Diagnostics;
using System.IO;

namespace PQueirozOptimizer.Services;

public sealed class PowerShellBridge
{
    private readonly ActivityLog _log;
    private readonly string _scriptPath;

    public PowerShellBridge(ActivityLog log)
    {
        _log = log;
        _scriptPath = Path.Combine(AppContext.BaseDirectory, "Otimizador_de_PC.ps1");
    }

    public async Task RunAsync(string operation, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_scriptPath))
            throw new FileNotFoundException("O script PowerShell original não foi encontrado.", _scriptPath);

        _log.Write("INFO", $"Iniciando operação: {operation}");
        var psi = new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{_scriptPath}\" -Operation \"{operation}\"",
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o PowerShell.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"A operação terminou com código {process.ExitCode}.");
        _log.Write("SUCCESS", $"Operação concluída: {operation}");
    }
}
