using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Onde o item de logon está registrado e onde o Windows guarda se ele está ativo.
/// Sem ApprovedKey o item é só de leitura (o Windows não tem liga/desliga para ele).
/// </summary>
public sealed record StartupSource(RegistryHive Hive, string? RunKey, string? Folder, string? ApprovedKey, string Label,
    string? PackageFamilyName = null, string? PackageTaskId = null);

/// <summary>
/// Itens de logon (chaves Run, pastas Inicializar, RunOnce, políticas e Winlogon). Ativar e desativar usa o
/// mesmo mecanismo do Gerenciador de Tarefas (chaves StartupApproved): nada é apagado e tudo pode ser desfeito.
/// </summary>
public sealed class StartupService
{
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    private const string Winlogon = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private readonly ActivityLog _log;
    public StartupService(ActivityLog log) => _log = log;

    private static IEnumerable<StartupSource> Sources(string? startupFolder, string? commonStartupFolder)
    {
        yield return new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", null, Approved + "Run", "Usuário");
        yield return new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", null, Approved + "Run", "Todos os usuários");
        yield return new(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", null, Approved + "Run32", "Todos os usuários (32 bits)");
        yield return new(RegistryHive.CurrentUser, null, startupFolder, Approved + "StartupFolder", "Pasta Inicializar");
        yield return new(RegistryHive.LocalMachine, null, commonStartupFolder, Approved + "StartupFolder", "Pasta Inicializar (todos)");
        // Só leitura, como aparecem no Autoruns: executam uma vez no próximo logon ou vêm de política
        yield return new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", null, null, "Executa uma vez no próximo logon");
        yield return new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", null, null, "Executa uma vez no próximo logon");
        yield return new(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", null, null, "Executa uma vez no próximo logon");
        yield return new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", null, null, "Definido por política do Windows");
        yield return new(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", null, null, "Definido por política do Windows");
    }

    /// <summary>Lê os itens de logon. Deve rodar numa thread STA: atalhos .lnk são resolvidos via COM.</summary>
    public IReadOnlyList<AutorunEntry> GetEntries()
    {
        var entries = new List<AutorunEntry>();
        var user = InteractiveUser();
        foreach (var source in Sources(user.StartupFolder, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)))
        {
            try
            {
                using var hive = OpenSourceHive(source.Hive, user.Sid);
                using var approved = source.ApprovedKey is null ? null : hive.OpenSubKey(source.ApprovedKey);
                if (source.RunKey != null)
                {
                    using var run = hive.OpenSubKey(source.RunKey);
                    if (run == null) continue;
                    foreach (var name in run.GetValueNames().Where(n => !string.IsNullOrWhiteSpace(n)))
                    {
                        var command = run.GetValue(name)?.ToString() ?? "";
                        entries.Add(new AutorunEntry
                        {
                            Category = AutorunCategory.Logon, Name = name, Key = name, Command = command,
                            Location = ShortHive(source.Hive) + "\\" + source.RunKey,
                            RegistryPath = LongHive(source.Hive) + "\\" + source.RunKey,
                            ImagePath = ResolveImage(ExecutablePath(command)),
                            Enabled = source.ApprovedKey is null || IsEnabled(approved?.GetValue(name) as byte[]),
                            CanToggle = source.ApprovedKey != null,
                            Detail = source.ApprovedKey is null ? source.Label : "",
                            Source = source,
                        });
                    }
                }
                else if (source.Folder != null && Directory.Exists(source.Folder))
                {
                    foreach (var file in Directory.EnumerateFiles(source.Folder).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Atalhos mostram o programa de destino, como no Autoruns
                        var shortcut = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutFile.Read(file) : null;
                        var target = shortcut is { Target.Length: > 0 } ? shortcut : null;
                        entries.Add(new AutorunEntry
                        {
                            Category = AutorunCategory.Logon, Name = Path.GetFileNameWithoutExtension(file), Key = Path.GetFileName(file),
                            Command = target is { } t ? $"\"{t.Target}\" {t.Arguments}".Trim() : file,
                            Location = source.Folder,
                            ImagePath = target is { } found ? ResolveImage(found.Target) : file,
                            Enabled = IsEnabled(approved?.GetValue(Path.GetFileName(file)) as byte[]),
                            Source = source,
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                _log.Write("WARN", $"Inicialização: não foi possível ler {source.Label}: {ex.Message}");
            }
        }
        AddPackagedEntries(entries, user);
        AddWinlogon(entries);
        return entries;
    }

    /// <summary>
    /// Apps da Store/MSIX com tarefa de inicialização (como o Gerenciador de Tarefas mostra). O Windows indexa
    /// essas tarefas em Classes\Extensions\ContractId\Windows.StartupTask\PackageId\{pacote}\ActivatableClassId\{tarefa};
    /// o liga/desliga fica em SystemAppData\{família}\{tarefa}\State, gravável pelo próprio usuário.
    /// </summary>
    private void AddPackagedEntries(List<AutorunEntry> entries, InteractiveUserInfo user)
    {
        if (string.IsNullOrWhiteSpace(user.Sid)) return;
        try
        {
            using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
            using var index = users.OpenSubKey($@"{user.Sid}\Software\Classes\Extensions\ContractId\Windows.StartupTask\PackageId");
            if (index is null) return;
            foreach (var fullName in index.GetSubKeyNames())
            {
                var family = PackageFamily(fullName);
                if (family is null) continue;
                var packageName = family[..family.LastIndexOf('_')];
                using var tasks = index.OpenSubKey($@"{fullName}\ActivatableClassId");
                foreach (var id in tasks?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    var state = ReadPackageState(user.Sid, family, id);
                    entries.Add(new AutorunEntry
                    {
                        Category = AutorunCategory.Logon,
                        Name = $"{packageName} ({id})",
                        Key = id,
                        Command = "",
                        Location = $"Pacote {family}",
                        Detail = state is 3 or 4 ? "Controlado pela política do Windows" : "Tarefa de inicialização do pacote",
                        Enabled = state is null or 2,
                        CanToggle = state is null or 1 or 2,
                        Source = new StartupSource(RegistryHive.CurrentUser, null, null, null, "Aplicativo empacotado", family, id),
                    });
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _log.Write("WARN", $"Inicialização: não foi possível ler os apps empacotados: {ex.Message}");
        }
    }

    /// <summary>Nome_Versão_Arquitetura_Recurso_Editor → Nome_Editor (família do pacote).</summary>
    public static string? PackageFamily(string fullName)
    {
        var parts = fullName.Split('_');
        return parts.Length == 5 && parts[0].Length > 0 && parts[4].Length > 0 ? parts[0] + "_" + parts[4] : null;
    }

    private static int? ReadPackageState(string? sid, string family, string task)
    {
        if (string.IsNullOrWhiteSpace(sid)) return null;
        using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
        using var state = users.OpenSubKey($@"{sid}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\{family}\{task}");
        return state?.GetValue("State") is int value ? value : null;
    }

    /// <summary>Shell e Userinit do Winlogon: alterados, são um sinal clássico de malware.</summary>
    private void AddWinlogon(List<AutorunEntry> entries)
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var winlogon = hive.OpenSubKey(Winlogon);
            foreach (var name in new[] { "Shell", "Userinit" })
            {
                if (winlogon?.GetValue(name) is not string data || string.IsNullOrWhiteSpace(data)) continue;
                var first = data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? data;
                entries.Add(new AutorunEntry
                {
                    Category = AutorunCategory.Logon, Name = name, Key = name, Command = data,
                    Location = @"HKLM\" + Winlogon, RegistryPath = @"HKEY_LOCAL_MACHINE\" + Winlogon,
                    ImagePath = ResolveImage(ExecutablePath(first)), Enabled = true, CanToggle = false,
                    Detail = "Parte do logon do Windows",
                });
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            _log.Write("WARN", "Inicialização: não foi possível ler o Winlogon: " + ex.Message);
        }
    }

    /// <summary>Valores padrão do Windows; qualquer outra coisa aparece mesmo com "Ocultar itens do Windows".</summary>
    public static bool IsDefaultWinlogon(string name, string data)
    {
        var value = data.Trim().TrimEnd(',').Trim();
        return name.Equals("Shell", StringComparison.OrdinalIgnoreCase)
            ? value.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
            : value.Equals(Path.Combine(Environment.SystemDirectory, "userinit.exe"), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(AutorunEntry entry, bool enabled)
    {
        if (entry.Source?.PackageFamilyName is { } family && entry.Source.PackageTaskId is { } task)
        {
            var sid = InteractiveUser().Sid ?? throw new InvalidOperationException("Não foi possível identificar o usuário interativo.");
            using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
            using var state = users.CreateSubKey($@"{sid}\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\{family}\{task}", writable: true)
                ?? throw new UnauthorizedAccessException("O estado da tarefa do pacote não está disponível.");
            state.SetValue("State", enabled ? 2 : 1, RegistryValueKind.DWord);
            if (state.GetValue("State") is not int actual || actual != (enabled ? 2 : 1))
                throw new IOException("O Windows não aceitou o estado da tarefa do pacote.");
            _log.Write("SUCCESS", $"Inicialização: {entry.Name} {(enabled ? "ativado" : "desativado")}");
            return;
        }
        if (entry.Source?.ApprovedKey is not { } approvedKey) throw new InvalidOperationException("Este item não pode ser desativado por aqui.");
        using var hive = OpenSourceHive(entry.Source.Hive, InteractiveUser().Sid);
        using var approved = hive.CreateSubKey(approvedKey, writable: true);
        approved.SetValue(entry.Key, BuildApprovedValue(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
        _log.Write("SUCCESS", $"Inicialização: {entry.Name} {(enabled ? "ativado" : "desativado")}");
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

    private static RegistryKey OpenSourceHive(RegistryHive hive, string? interactiveSid)
    {
        if (hive == RegistryHive.CurrentUser)
        {
            if (string.IsNullOrWhiteSpace(interactiveSid))
                throw new InvalidOperationException("Não foi possível identificar o usuário interativo.");
            return RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64).OpenSubKey(interactiveSid, writable: true)
                ?? throw new UnauthorizedAccessException("A conta interativa não está disponível no registro.");
        }
        return RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
    }

    private sealed record InteractiveUserInfo(string? Sid, string StartupFolder, string? ProfileFolder);

    private static InteractiveUserInfo InteractiveUser()
    {
        var sid = TryGetInteractiveSid();
        if (sid is null)
            return new(null, Environment.GetFolderPath(Environment.SpecialFolder.Startup), null);

        try
        {
            using var profiles = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            var profile = profiles?.OpenSubKey(sid)?.GetValue("ProfileImagePath")?.ToString();
            if (!string.IsNullOrWhiteSpace(profile))
            {
                profile = Environment.ExpandEnvironmentVariables(profile);
                return new(sid, Path.Combine(profile, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs", "Startup"), profile);
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { }
        return new(sid, Environment.GetFolderPath(Environment.SpecialFolder.Startup), null);
    }

    private static string? TryGetInteractiveSid()
    {
        // Sessão do próprio app (não a do console): em acesso remoto o console pode ser de outro usuário
        var session = (uint)System.Diagnostics.Process.GetCurrentProcess().SessionId;
        if (session == uint.MaxValue || !WTSQuerySessionInformation(IntPtr.Zero, session, WTS_INFO_CLASS.WTSUserName, out var user, out var userLength))
            return null;
        try
        {
            // O tamanho vem em bytes, não em caracteres: ler até o terminador evita lixo no nome
            var name = Marshal.PtrToStringUni(user)?.TrimEnd('\0');
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (!WTSQuerySessionInformation(IntPtr.Zero, session, WTS_INFO_CLASS.WTSDomainName, out var domain, out var domainLength))
                return null;
            try
            {
                var qualified = $"{Marshal.PtrToStringUni(domain)?.TrimEnd('\0')}\\{name}";
                try { return new NTAccount(qualified).Translate(typeof(SecurityIdentifier)).Value; }
                // Conta que não resolve (domínio fora do ar, nome inválido): usa o usuário atual
                catch (Exception ex) when (ex is IdentityNotMappedException or System.ComponentModel.Win32Exception or SystemException) { return null; }
            }
            finally { WTSFreeMemory(domain); }
        }
        finally { WTSFreeMemory(user); }
    }

    private enum WTS_INFO_CLASS { WTSUserName = 5, WTSDomainName = 7 }
    [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId, WTS_INFO_CLASS infoClass, out IntPtr buffer, out uint bytes);
    [DllImport("Wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr buffer);

    /// <summary>
    /// Extrai o executável de uma linha de comando ("C:\x\a.exe" --arg ou C:\x\a.exe -arg).
    /// Para o rundll32, devolve a DLL que ele carrega, que é o que de fato roda.
    /// </summary>
    public static string? ExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        string exe, rest;
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end <= 1) return null;
            exe = command[1..end]; rest = command[(end + 1)..];
        }
        else
        {
            var at = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            exe = at > 0 ? command[..(at + 4)] : command.Split(' ')[0];
            rest = command[exe.Length..];
        }
        if (Path.GetFileName(exe).Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase) || exe.Equals("rundll32", StringComparison.OrdinalIgnoreCase))
        {
            var dll = rest.Trim().TrimStart('"');
            var comma = dll.IndexOf(',');
            if (comma > 0) dll = dll[..comma];
            dll = dll.Trim().Trim('"');
            if (dll.Length > 0) return dll;
        }
        return exe;
    }

    /// <summary>Resolve o caminho como o Windows faria: variáveis, "\SystemRoot\", "\??\", System32 e ".exe" implícito.</summary>
    public static string? ResolveImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (path.StartsWith(@"\??\", StringComparison.Ordinal)) path = path[4..];
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(windows, path[12..]);
        else if (path.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(windows, path);
        if (Path.IsPathFullyQualified(path)) return path;
        // Mesma busca do Windows para nomes soltos ("powershell.exe"): System32, pasta do Windows e PATH
        var folders = new[] { Environment.SystemDirectory, windows }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var folder in folders)
        {
            try
            {
                var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(folder), path);
                if (File.Exists(candidate)) return candidate;
                if (!Path.HasExtension(candidate) && File.Exists(candidate + ".exe")) return candidate + ".exe";
            }
            catch (ArgumentException) { } // pasta do PATH com caracteres inválidos
        }
        return path;
    }

    private static string ShortHive(RegistryHive hive) => hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
    private static string LongHive(RegistryHive hive) => hive == RegistryHive.CurrentUser ? "HKEY_CURRENT_USER" : "HKEY_LOCAL_MACHINE";
}
