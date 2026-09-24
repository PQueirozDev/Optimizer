using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

public sealed record StartupItem(string Name, string Command, string Location, string Publisher, bool Enabled, StartupSource Source);

/// <summary>Onde o item está registrado e onde o Windows guarda se ele está ativo.</summary>
public sealed record StartupSource(RegistryHive Hive, string? RunKey, string? Folder, string ApprovedKey, string Label);

/// <summary>
/// Lista e ativa/desativa programas de inicialização usando o mesmo mecanismo do Gerenciador
/// de Tarefas (chaves StartupApproved): nada é apagado, então qualquer mudança pode ser desfeita.
/// </summary>
public sealed class StartupService
{
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    private readonly ActivityLog _log;
    public StartupService(ActivityLog log) => _log = log;

    private static IEnumerable<StartupSource> Sources()
    {
        yield return new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", null, Approved + "Run", "Usuário");
        yield return new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", null, Approved + "Run", "Todos os usuários");
        yield return new(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", null, Approved + "Run32", "Todos os usuários (32 bits)");
        yield return new(RegistryHive.CurrentUser, null, Environment.GetFolderPath(Environment.SpecialFolder.Startup), Approved + "StartupFolder", "Pasta Inicializar");
        yield return new(RegistryHive.LocalMachine, null, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Approved + "StartupFolder", "Pasta Inicializar (todos)");
    }

    public IReadOnlyList<StartupItem> GetItems()
    {
        var items = new List<StartupItem>();
        foreach (var source in Sources())
        {
            try
            {
                using var hive = RegistryKey.OpenBaseKey(source.Hive, RegistryView.Registry64);
                using var approved = hive.OpenSubKey(source.ApprovedKey);
                if (source.RunKey != null)
                {
                    using var run = hive.OpenSubKey(source.RunKey);
                    if (run == null) continue;
                    foreach (var name in run.GetValueNames().Where(n => !string.IsNullOrWhiteSpace(n)))
                    {
                        var command = run.GetValue(name)?.ToString() ?? "";
                        items.Add(new StartupItem(name, command, source.Label, Publisher(command), IsEnabled(approved?.GetValue(name) as byte[]), source));
                    }
                }
                else if (source.Folder != null && Directory.Exists(source.Folder))
                {
                    foreach (var file in Directory.EnumerateFiles(source.Folder).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                    {
                        var name = Path.GetFileName(file);
                        items.Add(new StartupItem(Path.GetFileNameWithoutExtension(file), file, source.Label, Publisher(file), IsEnabled(approved?.GetValue(name) as byte[]), source));
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                _log.Write("WARN", $"Inicialização: não foi possível ler {source.Label}: {ex.Message}");
            }
        }
        return items.OrderByDescending(i => i.Enabled).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void SetEnabled(StartupItem item, bool enabled)
    {
        using var hive = RegistryKey.OpenBaseKey(item.Source.Hive, RegistryView.Registry64);
        using var approved = hive.CreateSubKey(item.Source.ApprovedKey, writable: true);
        var valueName = item.Source.Folder != null ? Path.GetFileName(item.Command) : item.Name;
        approved.SetValue(valueName, BuildApprovedValue(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
        _log.Write("SUCCESS", $"Inicialização: {item.Name} {(enabled ? "ativado" : "desativado")}");
    }

    /// <summary>O Windows considera ativo quando o valor não existe ou o primeiro byte é par (02, 06).</summary>
    public static bool IsEnabled(byte[]? approved) => approved is not { Length: > 0 } || (approved[0] & 1) == 0;

    /// <summary>02 00 00 00 + zeros = ativo; 03 00 00 00 + FILETIME da desativação = desativado.</summary>
    public static byte[] BuildApprovedValue(bool enabled, DateTime utcNow)
    {
        var value = new byte[12];
        value[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(utcNow.ToFileTimeUtc()).CopyTo(value, 4);
        return value;
    }

    /// <summary>Extrai o executável de uma linha de comando ("C:\x\a.exe" --arg ou C:\x\a.exe -arg).</summary>
    public static string? ExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? command[..(exe + 4)] : command.Split(' ')[0];
    }

    private static string Publisher(string command)
    {
        try
        {
            var path = ExecutablePath(command);
            if (path != null && File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim() ?? "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return "";
    }
}
