using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Win32;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Tudo o que o Windows executa sozinho, como o Autoruns mostra: itens de logon, tarefas agendadas e
/// serviços automáticos, com descrição, editor, assinatura digital e caminho do arquivo.
/// </summary>
public sealed class AutorunsService
{
    /// <summary>Serviços que este app passou para Manual e o tipo de início original de cada um.</summary>
    private const string DisabledServicesKey = @"SOFTWARE\PQueirozOptimizer\ServicosDesativados";
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";
    // Verificar assinaturas é a parte lenta; o resultado vale enquanto o arquivo não mudar
    private static readonly Dictionary<string, (DateTime Stamp, SignatureStatus Status, string Signer)> SignatureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ActivityLog _log;
    private readonly StartupService _startup;

    public AutorunsService(ActivityLog log)
    {
        _log = log;
        _startup = new StartupService(log);
    }

    /// <summary>Roda numa thread STA própria: ícones do shell, atalhos e o Agendador de Tarefas usam COM.</summary>
    public Task<List<AutorunEntry>> ScanAsync()
    {
        var completion = new TaskCompletionSource<List<AutorunEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Scan()); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "PQO Inicialização" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private List<AutorunEntry> Scan()
    {
        var entries = new List<AutorunEntry>(_startup.GetEntries());
        try { entries.AddRange(ScanTasks()); }
        catch (Exception ex) when (IsComFailure(ex)) { _log.Write("WARN", "Inicialização: não foi possível ler as tarefas agendadas: " + ex.Message); }
        try { entries.AddRange(ScanServices()); }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { _log.Write("WARN", "Inicialização: não foi possível ler os serviços: " + ex.Message); }
        FillFileDetails(entries);
        return entries;
    }

    #region Tarefas agendadas
    private static List<AutorunEntry> ScanTasks()
    {
        var entries = new List<AutorunEntry>();
        var type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null) return entries;
        dynamic service = Activator.CreateInstance(type)!;
        try
        {
            service.Connect();
            var folders = new Stack<dynamic>();
            folders.Push(service.GetFolder("\\"));
            while (folders.Count > 0)
            {
                var folder = folders.Pop();
                string folderPath = folder.Path;
                var tasks = folder.GetTasks(1); // TASK_ENUM_HIDDEN: inclui as ocultas, como o Autoruns
                for (var i = 1; i <= (int)tasks.Count; i++)
                {
                    // Uma tarefa com definição corrompida não pode impedir a leitura das outras
                    try { entries.Add(TaskEntry(tasks.Item(i), folderPath)); }
                    catch (Exception ex) when (IsComFailure(ex)) { }
                }
                var subfolders = folder.GetFolders(0);
                for (var i = 1; i <= (int)subfolders.Count; i++) folders.Push(subfolders.Item(i));
            }
        }
        finally { Marshal.FinalReleaseComObject(service); }
        return entries;
    }

    private static AutorunEntry TaskEntry(dynamic task, string folderPath)
    {
        dynamic definition = task.Definition;
        string command = "";
        string? image = null;
        var actions = definition.Actions;
        for (var i = 1; i <= (int)actions.Count; i++)
        {
            var action = actions.Item(i);
            int kind = action.Type;
            if (kind == 0) // programa
            {
                string exe = action.Path ?? "";
                string arguments = action.Arguments ?? "";
                command = $"{exe} {arguments}".Trim();
                image = StartupService.ResolveImage(exe);
                break;
            }
            if (kind == 5) // manipulador COM: o que roda é a DLL registrada para a classe
            {
                string classId = action.ClassId ?? "";
                command = "COM " + classId;
                image = ComServerPath(classId);
                break;
            }
        }
        var triggers = new List<string>();
        var taskTriggers = definition.Triggers;
        for (var i = 1; i <= (int)taskTriggers.Count; i++)
        {
            var label = TriggerLabel((int)taskTriggers.Item(i).Type);
            if (!triggers.Contains(label)) triggers.Add(label);
        }
        string path = task.Path;
        string description = definition.RegistrationInfo.Description ?? "";
        return new AutorunEntry
        {
            Category = AutorunCategory.Tasks,
            Name = (string)task.Name,
            Key = path,
            Location = folderPath == "\\" ? "Agendador de Tarefas" : "Agendador de Tarefas " + folderPath,
            Command = command,
            ImagePath = image,
            Detail = triggers.Count == 0 ? "Sem gatilho (só manual)" : string.Join(" · ", triggers),
            Enabled = (bool)task.Enabled,
            Description = LoadIndirect(description),
        };
    }

    public static string TriggerLabel(int type) => type switch
    {
        0 => "Em um evento",
        1 => "Uma vez",
        2 => "Diariamente",
        3 => "Semanalmente",
        4 or 5 => "Mensalmente",
        6 => "Com o PC ocioso",
        7 => "Ao ser criada",
        8 => "Ao iniciar o Windows",
        9 => "Ao fazer logon",
        11 => "Ao bloquear ou desbloquear",
        _ => "Gatilho personalizado",
    };

    private static string? ComServerPath(string classId)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using (var inproc = hive.OpenSubKey($@"SOFTWARE\Classes\CLSID\{classId}\InprocServer32"))
            if (inproc?.GetValue(null) is string dll && dll.Length > 0) return StartupService.ResolveImage(dll);
        // Servidor em processo próprio: o valor é uma linha de comando, pode ter argumentos
        using var local = hive.OpenSubKey($@"SOFTWARE\Classes\CLSID\{classId}\LocalServer32");
        return local?.GetValue(null) is string command && command.Length > 0 ? StartupService.ResolveImage(StartupService.ExecutablePath(command)) : null;
    }

    private void SetTaskEnabled(AutorunEntry entry, bool enabled)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("O Agendador de Tarefas não está disponível.");
        dynamic service = Activator.CreateInstance(type)!;
        try
        {
            service.Connect();
            var cut = entry.Key.LastIndexOf('\\');
            var folder = service.GetFolder(cut <= 0 ? "\\" : entry.Key[..cut]);
            var task = folder.GetTask(entry.Key[(cut + 1)..]);
            task.Enabled = enabled;
        }
        finally { Marshal.FinalReleaseComObject(service); }
        _log.Write("SUCCESS", $"Inicialização: {entry.Name} {(enabled ? "ativado" : "desativado")}");
    }
    #endregion

    #region Serviços
    private static List<AutorunEntry> ScanServices()
    {
        var entries = new List<AutorunEntry>();
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var services = hive.OpenSubKey(ServicesKey);
        using var disabledHere = hive.OpenSubKey(DisabledServicesKey);
        if (services is null) return entries;
        foreach (var name in services.GetSubKeyNames())
        {
            // Algumas chaves de serviço negam leitura; elas são puladas sem esconder as demais
            try { if (ServiceEntry(services, name, disabledHere) is { } entry) entries.Add(entry); }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return entries;
    }

    private static AutorunEntry? ServiceEntry(RegistryKey services, string name, RegistryKey? disabledHere)
    {
        using var key = services.OpenSubKey(name);
        if (key is null) return null;
        var type = key.GetValue("Type") as int? ?? 0;
        // Só serviços de programas: drivers e modelos de serviços por usuário ficam de fora
        if ((type & 0x30) == 0 || (type & 0x40) != 0) return null;
        var start = key.GetValue("Start") as int? ?? 3;
        // Automáticos, mais os que este app passou para Manual (aparecem desmarcados, como no Autoruns)
        if (start != 2 && !(start == 3 && disabledHere?.GetValue(name) is string)) return null;
        var imagePath = key.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        using var parameters = key.OpenSubKey("Parameters");
        var serviceDll = parameters?.GetValue("ServiceDll", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        var startText = start != 2 ? "Manual (desativado aqui)" : key.GetValue("DelayedAutostart") as int? == 1 ? "Automático (atraso na inicialização)" : "Automático";
        var displayName = LoadIndirect(key.GetValue("DisplayName") as string);
        return new AutorunEntry
        {
            Category = AutorunCategory.Services,
            Name = displayName.Length > 0 ? displayName : name,
            Key = name,
            Location = @"HKLM\" + ServicesKey,
            RegistryPath = @"HKEY_LOCAL_MACHINE\" + ServicesKey + "\\" + name,
            Command = imagePath,
            ImagePath = ServiceImage(imagePath, serviceDll),
            Detail = $"{startText} · {name}",
            Enabled = start == 2,
            Description = LoadIndirect(key.GetValue("Description") as string),
        };
    }

    /// <summary>Arquivo que representa o serviço. Nos serviços hospedados pelo svchost, é a DLL (como no Autoruns).</summary>
    public static string? ServiceImage(string imagePath, string? serviceDll) =>
        !string.IsNullOrWhiteSpace(serviceDll) ? StartupService.ResolveImage(serviceDll) : StartupService.ResolveImage(StartupService.ExecutablePath(imagePath));

    /// <summary>
    /// "Desativar" um serviço aqui é passá-lo para Manual: ele deixa de iniciar com o Windows, mas ainda
    /// inicia se um programa precisar. O tipo original fica guardado para "ativar" voltar exatamente a ele.
    /// </summary>
    private void SetServiceEnabled(AutorunEntry entry, bool enabled)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        if (enabled)
        {
            using var record = hive.OpenSubKey(DisabledServicesKey, writable: true);
            var delayed = record?.GetValue(entry.Key) as string == "delayed-auto";
            SetServiceStart(entry.Key, ServiceAutoStart, delayed);
            record?.DeleteValue(entry.Key, throwOnMissingValue: false);
        }
        else
        {
            using var service = hive.OpenSubKey(ServicesKey + "\\" + entry.Key);
            var original = service?.GetValue("DelayedAutostart") as int? == 1 ? "delayed-auto" : "auto";
            using var record = hive.CreateSubKey(DisabledServicesKey, writable: true);
            record.SetValue(entry.Key, original);
            try { SetServiceStart(entry.Key, ServiceDemandStart, delayed: false); }
            catch { record.DeleteValue(entry.Key, throwOnMissingValue: false); throw; }
        }
        _log.Write("SUCCESS", $"Inicialização: {entry.Name} {(enabled ? "ativado" : "desativado")}");
    }

    private const uint ServiceAutoStart = 2, ServiceDemandStart = 3, ServiceNoChange = 0xFFFFFFFF;

    private static void SetServiceStart(string name, uint start, bool delayed)
    {
        var manager = OpenSCManager(null, null, 0x0001); // SC_MANAGER_CONNECT
        if (manager == IntPtr.Zero) throw new Win32Exception();
        try
        {
            var service = OpenService(manager, name, 0x0002); // SERVICE_CHANGE_CONFIG
            if (service == IntPtr.Zero) throw new Win32Exception();
            try
            {
                if (!ChangeServiceConfig(service, ServiceNoChange, start, ServiceNoChange, null, null, IntPtr.Zero, null, null, null, null)) throw new Win32Exception();
                if (start == ServiceAutoStart)
                {
                    var info = new DelayedAutoStartInfo { Delayed = delayed };
                    if (!ChangeServiceConfig2(service, 3, ref info)) throw new Win32Exception(); // SERVICE_CONFIG_DELAYED_AUTO_START_INFO
                }
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
    #endregion

    #region Arquivos: descrição, editor, ícone e assinatura
    private static void FillFileDetails(List<AutorunEntry> entries)
    {
        var cache = new Dictionary<string, (bool Exists, string Description, string Company, ImageSource? Icon)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.ImagePath is not { Length: > 0 } image)
            {
                entry.Signature = SignatureStatus.NotApplicable;
                entry.IsWindows = IsWindowsEntry(entry);
                continue;
            }
            if (!cache.TryGetValue(image, out var info))
            {
                var exists = File.Exists(image);
                string description = "", company = "";
                if (exists)
                {
                    try
                    {
                        var version = FileVersionInfo.GetVersionInfo(image);
                        description = version.FileDescription?.Trim() ?? "";
                        company = version.CompanyName?.Trim() ?? "";
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                }
                info = (exists, description, company, exists ? FileIcon(image) : null);
                cache[image] = info;
            }
            entry.FileExists = info.Exists;
            if (entry.Description.Length == 0) entry.Description = info.Description;
            entry.Company = info.Company;
            entry.Icon = info.Icon;
            entry.Signature = info.Exists ? SignatureStatus.Pending : SignatureStatus.NotApplicable;
            entry.IsWindows = IsWindowsEntry(entry);
        }
    }

    /// <summary>
    /// Confere as assinaturas digitais (Authenticode, inclusive os catálogos do Windows) em um só PowerShell,
    /// como a verificação do Autoruns. Arquivos já verificados e não alterados não são lidos de novo.
    /// </summary>
    public async Task VerifySignaturesAsync(IReadOnlyList<AutorunEntry> entries, CancellationToken token = default)
    {
        var pending = new List<string>();
        foreach (var path in entries.Where(e => e.FileExists && e.ImagePath != null).Select(e => e.ImagePath!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!(SignatureCache.TryGetValue(path, out var cached) && cached.Stamp == LastWrite(path))) pending.Add(path);
        }
        // Lotes para não passar do limite de uma variável de ambiente
        foreach (var batch in Batches(pending, 24000))
        {
            var json = await PowerShellBridge.RunScriptAsync(SignatureScript, new Dictionary<string, string> { ["PQO_FILES"] = string.Join('\n', batch) }, TimeSpan.FromMinutes(3), token);
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var path = item.GetProperty("p").GetString() ?? "";
                var status = item.GetProperty("s").GetString() switch
                {
                    "Valid" => SignatureStatus.Verified,
                    "NotSigned" => SignatureStatus.NotSigned,
                    _ => SignatureStatus.NotVerified,
                };
                SignatureCache[path] = (LastWrite(path), status, item.GetProperty("n").GetString() ?? "");
            }
        }
        foreach (var entry in entries)
        {
            if (entry.ImagePath is { } path && entry.FileExists && SignatureCache.TryGetValue(path, out var result))
            {
                entry.Signature = result.Status;
                entry.Signer = result.Signer;
            }
            entry.IsWindows = IsWindowsEntry(entry);
        }
    }

    private const string SignatureScript = """
        $ProgressPreference = 'SilentlyContinue'
        $saida = foreach ($arquivo in ($env:PQO_FILES -split "`n")) {
            if (-not $arquivo) { continue }
            try {
                $assinatura = Get-AuthenticodeSignature -LiteralPath $arquivo -ErrorAction Stop
                $nome = if ($assinatura.SignerCertificate) { $assinatura.SignerCertificate.GetNameInfo('SimpleName', $false) } else { '' }
                [pscustomobject]@{ p = $arquivo; s = "$($assinatura.Status)"; n = $nome }
            } catch { [pscustomobject]@{ p = $arquivo; s = 'Error'; n = '' } }
        }
        ConvertTo-Json -InputObject @($saida) -Compress
        """;

    /// <summary>
    /// Parte do próprio Windows: assinado pela Microsoft como "Microsoft Windows". Programas da Microsoft
    /// fora do sistema (Edge, OneDrive, Teams) são assinados como "Microsoft Corporation" e continuam visíveis.
    /// Antes da verificação terminar, vale uma estimativa pela pasta do arquivo e pelo fabricante.
    /// </summary>
    public static bool IsWindowsEntry(AutorunEntry entry)
    {
        if (entry.Category == AutorunCategory.Logon && entry.Location.EndsWith(@"\Winlogon", StringComparison.OrdinalIgnoreCase))
            return StartupService.IsDefaultWinlogon(entry.Key, entry.Command);
        var windowsTask = entry.Category == AutorunCategory.Tasks && entry.Key.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase);
        // cmd, PowerShell e afins são do Windows, mas o que eles executam não: fora das tarefas do próprio
        // Windows, um item que os usa sempre aparece (é assim que muito malware se esconde)
        if (!windowsTask && IsScriptHost(entry.ImagePath)) return false;
        switch (entry.Signature)
        {
            case SignatureStatus.Verified: return IsWindowsSigner(entry.Signer);
            case SignatureStatus.NotSigned or SignatureStatus.NotVerified: return false;
        }
        if (windowsTask) return true;
        return entry.FileExists && entry.ImagePath is { } image
            && image.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)
            && entry.Company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWindowsSigner(string signer) =>
        signer.StartsWith("Microsoft Windows", StringComparison.Ordinal) && !signer.Contains("Hardware Compatibility", StringComparison.Ordinal);

    private static readonly HashSet<string> ScriptHosts = new(StringComparer.OrdinalIgnoreCase)
        { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "regsvr32.exe" };

    public static bool IsScriptHost(string? image) => image != null && ScriptHosts.Contains(Path.GetFileName(image));

    private static DateTime LastWrite(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return DateTime.MinValue; }
    }

    private static IEnumerable<List<string>> Batches(List<string> paths, int maxChars)
    {
        var batch = new List<string>();
        var length = 0;
        foreach (var path in paths)
        {
            if (batch.Count > 0 && length + path.Length + 1 > maxChars) { yield return batch; batch = new List<string>(); length = 0; }
            batch.Add(path);
            length += path.Length + 1;
        }
        if (batch.Count > 0) yield return batch;
    }

    /// <summary>Descrições do registro no formato "@arquivo.dll,-123" (e "$(...)" nas tarefas) vêm do recurso do arquivo.</summary>
    public static string LoadIndirect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var value = text.Trim();
        if (value.StartsWith("$(", StringComparison.Ordinal) && value.EndsWith(')')) value = value[2..^1];
        if (!value.StartsWith('@')) return value;
        var buffer = new StringBuilder(1024);
        return SHLoadIndirectString(value, buffer, buffer.Capacity, IntPtr.Zero) == 0 ? buffer.ToString() : "";
    }

    private static ImageSource? FileIcon(string path)
    {
        var info = new ShFileInfo();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), 0x100) == IntPtr.Zero || info.Icon == IntPtr.Zero) return null; // SHGFI_ICON (32 px)
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); // criado nesta thread, usado na da interface
            return source;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException) { return null; }
        finally { DestroyIcon(info.Icon); }
    }
    #endregion

    #region Ações
    public Task SetEnabledAsync(AutorunEntry entry, bool enabled) => entry.Category switch
    {
        AutorunCategory.Logon => Task.Run(() => _startup.SetEnabled(entry, enabled)),
        AutorunCategory.Tasks => Task.Run(() => SetTaskEnabled(entry, enabled)),
        _ => Task.Run(() => SetServiceEnabled(entry, enabled)),
    };

    /// <summary>"Ir para a entrada": Editor do Registro na chave, pasta do arquivo, Agendador de Tarefas ou Serviços.</summary>
    public static void JumpTo(AutorunEntry entry)
    {
        switch (entry.Category)
        {
            case AutorunCategory.Tasks: Process.Start(new ProcessStartInfo("taskschd.msc") { UseShellExecute = true }); break;
            case AutorunCategory.Services: Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true }); break;
            default:
                if (entry.RegistryPath is { } key) OpenRegistryEditor(key);
                else if (entry.Source?.Folder is { } folder) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.Combine(folder, entry.Key)}\"") { UseShellExecute = true });
                break;
        }
    }

    /// <summary>O Editor do Registro abre na última chave visitada: gravá-la antes leva direto à entrada.</summary>
    private static void OpenRegistryEditor(string key)
    {
        using (var applet = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit"))
        {
            // A raiz ("Computer", "Computador") muda com o idioma do Windows: reaproveita a que já está gravada
            var last = applet.GetValue("LastKey") as string ?? "";
            var root = last.IndexOf(@"\HKEY_", StringComparison.OrdinalIgnoreCase) is > 0 and var cut ? last[..cut] : "Computer";
            applet.SetValue("LastKey", root + "\\" + key);
        }
        Process.Start(new ProcessStartInfo("regedit.exe", "-m") { UseShellExecute = true });
    }
    #endregion

    private static bool IsComFailure(Exception ex) => ex is COMException or UnauthorizedAccessException or InvalidCastException or RuntimeBinderException;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DelayedAutoStartInfo { [MarshalAs(UnmanagedType.Bool)] public bool Delayed; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string source, StringBuilder output, int size, IntPtr reserved);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ChangeServiceConfigW")]
    private static extern bool ChangeServiceConfig(IntPtr service, uint type, uint start, uint errorControl, string? binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? account, string? password, string? displayName);
    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "ChangeServiceConfig2W")] private static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, ref DelayedAutoStartInfo info);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
}
