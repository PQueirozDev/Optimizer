using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Registro da otimização em andamento. É gravado antes do script começar e apagado quando ele termina (com
/// sucesso, falha ou cancelamento). Se o app fechar no meio (queda, falta de energia, Gerenciador de Tarefas),
/// o registro sobra e a próxima abertura avisa o usuário para conferir o que foi aplicado. O backup de cada item
/// já é gravado antes da mudança; este registro só serve para o usuário saber que precisa olhar.
/// Nada daqui é executado: o conteúdo só aparece numa mensagem.
/// </summary>
public sealed class OperationJournal
{
    public sealed record Entry(string Operation, IReadOnlyList<string>? Steps, DateTime StartedAtUtc, int ProcessId, DateTime ProcessStartUtc, string AppVersion);

    /// <summary>Operações que mudam o sistema; diagnósticos e verificações não entram no registro.</summary>
    public static readonly IReadOnlySet<string> Tracked = new HashSet<string>(StringComparer.Ordinal) { "padrao", "gamer", "gamerservicos", "debloat", "inteligente", "reverter" };

    public static OperationJournal Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PQueirozOptimizer", "Journal"));

    private readonly string _directory;
    public OperationJournal(string directory) => _directory = directory;
    private string ActivePath => Path.Combine(_directory, "em-andamento.json");

    public void Begin(string operation, IReadOnlyList<string>? steps)
    {
        using var self = Process.GetCurrentProcess();
        var entry = new Entry(operation, steps, DateTime.UtcNow, self.Id, self.StartTime.ToUniversalTime(),
            typeof(OperationJournal).Assembly.GetName().Version?.ToString(3) ?? "");
        RegistryTweakStore.EnsureProtectedDirectory(_directory);
        var temp = ActivePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entry));
        File.Move(temp, ActivePath, overwrite: true);
    }

    public void End()
    {
        try { File.Delete(ActivePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Operação que começou e não terminou porque o processo que a iniciou não existe mais. Null se não há
    /// registro ou se o processo dono ainda está rodando (outra janela do app no meio da execução).
    /// </summary>
    public Entry? FindInterrupted(Func<int, DateTime, bool>? isRunning = null)
    {
        Entry? entry;
        try
        {
            if (!File.Exists(ActivePath)) return null;
            entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(ActivePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        // Registro ilegível também indica uma execução que não fechou direito
        catch (JsonException) { return new Entry("", null, File.GetLastWriteTimeUtc(ActivePath), 0, default, ""); }
        if (entry is null || !Tracked.Contains(entry.Operation)) return entry is null ? null : entry with { Operation = "" };
        return (isRunning ?? IsRunning)(entry.ProcessId, entry.ProcessStartUtc) ? null : entry;
    }

    /// <summary>O usuário já foi avisado: o registro sai para não repetir o aviso.</summary>
    public void Acknowledge() => End();

    private static bool IsRunning(int processId, DateTime startUtc)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            // O Windows reaproveita números de processo: confere também a hora de início
            return Math.Abs((process.StartTime.ToUniversalTime() - startUtc).TotalSeconds) < 2;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
}
