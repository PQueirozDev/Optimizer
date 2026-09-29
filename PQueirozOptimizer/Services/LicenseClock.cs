using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Trava contra atrasar o relógio do Windows para esticar uma licença com validade. Guarda a data mais
/// recente em que o app rodou, num arquivo e no registro do usuário (vale o maior, então apagar um só
/// não adianta). Com internet, a hora do servidor substitui o registro, inclusive para baixo: um relógio
/// que estava adiantado por engano não deixa o usuário travado.
/// </summary>
public sealed class LicenseClock
{
    // Fuso, horário de verão e pequenos acertos do relógio não contam como atraso
    private static readonly TimeSpan Tolerance = TimeSpan.FromDays(1);
    private const string RegistryValue = "LastSeenUtc";
    public static LicenseClock Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "clock.dat"), @"Software\PQueirozOptimizer");

    private readonly string _path;
    private readonly string? _registryKey;

    /// <param name="registryKey">Chave em HKCU com a segunda cópia; null nos testes.</param>
    public LicenseClock(string path, string? registryKey = null) { _path = path; _registryKey = registryKey; }

    public DateTime LastSeenUtc
    {
        get
        {
            var file = ReadFile(); var registry = ReadRegistry();
            return file > registry ? file : registry;
        }
    }

    public bool IsRolledBack(DateTime nowUtc) => nowUtc < LastSeenUtc - Tolerance;

    /// <summary>"Agora" para conferir a validade: nunca antes da última vez em que o app rodou.</summary>
    public DateTime EffectiveNowUtc(DateTime nowUtc) { var last = LastSeenUtc; return nowUtc > last ? nowUtc : last; }

    public void Observe(DateTime nowUtc) { if (nowUtc > LastSeenUtc) Write(nowUtc); }

    public void SetTrusted(DateTime serverUtc) => Write(serverUtc);

    private void Write(DateTime utc)
    {
        var ticks = DateTime.SpecifyKind(utc, DateTimeKind.Utc).Ticks;
        try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.WriteAllText(_path, ticks.ToString(CultureInfo.InvariantCulture)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (_registryKey is null) return;
        try { using var key = Registry.CurrentUser.CreateSubKey(_registryKey); key.SetValue(RegistryValue, ticks, RegistryValueKind.QWord); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    private DateTime ReadFile()
    {
        try { return File.Exists(_path) && long.TryParse(File.ReadAllText(_path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ? FromTicks(ticks) : DateTime.MinValue; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    private DateTime ReadRegistry()
    {
        if (_registryKey is null) return DateTime.MinValue;
        try { using var key = Registry.CurrentUser.OpenSubKey(_registryKey); return key?.GetValue(RegistryValue) is long ticks ? FromTicks(ticks) : DateTime.MinValue; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return DateTime.MinValue; }
    }

    private static DateTime FromTicks(long ticks) => ticks is > 0 and <= 3155378975999999999 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
}
