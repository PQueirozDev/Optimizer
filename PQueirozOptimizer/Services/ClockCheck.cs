namespace PQueirozOptimizer.Services;

/// <summary>
/// Diferença entre o relógio do Windows e a hora de um servidor (o cabeçalho Date da conferência da licença).
/// Relógio errado derruba logins de jogos e launchers, certificados HTTPS e o Windows Update.
/// </summary>
public static class ClockCheck
{
    /// <summary>A partir daqui o aviso aparece: o cabeçalho tem resolução de 1 s e a rede soma alguns segundos.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(2);

    private static readonly TaskCompletionSource<TimeSpan?> FirstReading = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Servidor menos relógio local (positivo = relógio atrasado). Null sem leitura (sem internet).</summary>
    public static TimeSpan? Offset { get; private set; }

    /// <summary>Primeira leitura da sessão (completa com null quando não houve internet).</summary>
    public static Task<TimeSpan?> FirstReadingAsync => FirstReading.Task;

    public static void Record(DateTime serverUtc, DateTime localUtc)
    {
        Offset = serverUtc - localUtc;
        FirstReading.TrySetResult(Offset);
    }

    public static void Unavailable() => FirstReading.TrySetResult(null);

    /// <summary>O relógio acabou de ser sincronizado: a leitura antiga não vale mais.</summary>
    public static void Clear() => Offset = null;

    public static bool IsOff(TimeSpan? offset) => offset is { } o && o.Duration() > Tolerance;

    public static string Describe(TimeSpan offset)
    {
        var minutes = (int)Math.Round(offset.Duration().TotalMinutes);
        var amount = minutes >= 120 ? $"{minutes / 60} horas" : $"{minutes} minutos";
        return offset > TimeSpan.Zero ? $"{amount} atrasado" : $"{amount} adiantado";
    }
}
