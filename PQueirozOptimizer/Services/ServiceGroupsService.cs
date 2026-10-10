using System.ComponentModel;
using System.IO;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>Grupo de serviços que a página Serviços liga e desliga de uma vez.</summary>
public sealed record ServiceGroup(string Id, string Category, string Name, string Description, string[] Services, string? Warning = null);

/// <summary>
/// Serviços em segundo plano por grupo (Windows Update, telemetria, descoberta de rede, Bluetooth...). Desligar
/// guarda o tipo de inicialização original de cada serviço; ligar de novo restaura exatamente o que era.
/// </summary>
public sealed class ServiceGroupsService
{
    private readonly ActivityLog _log;
    private static readonly string BackupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "Tweaks", "servicos.json");

    public ServiceGroupsService(ActivityLog log) => _log = log;

    public static readonly ServiceGroup[] Groups =
    {
        new("windows-update", "Sistema e privacidade", "Bloquear o Windows Update", "Para as atualizações automáticas e a Otimização de Entrega. Lembre de ligar de novo de vez em quando para receber correções de segurança.", new[] { "wuauserv", "UsoSvc", "DoSvc" }, "Sem atualizações de segurança enquanto estiver desligado."),
        new("telemetry", "Sistema e privacidade", "Bloquear a telemetria", "Desliga a coleta e o envio de dados de diagnóstico e relatórios de erro para a Microsoft.", new[] { "DiagTrack", "dmwappushservice", "diagnosticshub.standardcollector.service", "WerSvc" }),
        new("discovery", "Rede e conexão", "Desligar a descoberta na rede local", "Para o compartilhamento e a descoberta de dispositivos na rede (outros PCs, TVs e impressoras de rede deixam de aparecer).", new[] { "FDResPub", "fdPHost", "SSDPSRV", "upnphost", "lmhosts" }),
        new("remote", "Rede e conexão", "Desligar o acesso remoto", "Bloqueia a Área de Trabalho Remota, o registro remoto e o roteamento de VPN do Windows.", new[] { "RemoteRegistry", "RemoteAccess", "TermService", "SessionEnv", "UmRdpService" }, "A Área de Trabalho Remota deste PC para de funcionar."),
        new("print", "Hardware e periféricos", "Desligar a impressão", "Para os serviços de impressora e digitalização. Só desligue se não usa impressora.", new[] { "Spooler", "PrintNotify" }),
        new("bluetooth", "Hardware e periféricos", "Desligar o Bluetooth", "Para o serviço de suporte ao Bluetooth. Fones, controles e mouses Bluetooth deixam de conectar.", new[] { "bthserv", "BTAGService" }),
        new("hyperv", "Recursos do Windows", "Desligar o Hyper-V", "Para os serviços de máquinas virtuais. WSL 2, Docker e o Sandbox do Windows precisam deles.", new[] { "HvHost", "vmickvpexchange", "vmicguestinterface", "vmicshutdown", "vmicheartbeat", "vmicvmsession", "vmicrdv", "vmictimesync", "vmicvss" }),
        new("xbox", "Recursos do Windows", "Desligar os serviços do Xbox", "Para a Xbox Live e o salvamento na nuvem de jogos do app Xbox. Controles continuam funcionando.", new[] { "XblAuthManager", "XblGameSave", "XboxNetApiSvc" }, "Jogos do Game Pass/Xbox app podem não abrir com eles desligados."),
    };

    /// <summary>
    /// Backup dos tipos de início originais. Ilegível = null: tratar como vazio faria o próximo "desligar"
    /// gravar como original o estado já desativado, e a reversão nunca mais voltaria ao padrão.
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>>? TryLoadBackup()
    {
        try { return File.Exists(BackupPath) ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(BackupPath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static Dictionary<string, Dictionary<string, string>> LoadBackup() =>
        TryLoadBackup() ?? throw new InvalidDataException($"O backup dos serviços ({BackupPath}) está corrompido. Nada foi alterado; restaure os serviços em Serviços → Estado dos serviços.");

    private static void SaveBackup(Dictionary<string, Dictionary<string, string>> backup)
    {
        var dir = Path.GetDirectoryName(BackupPath)!;
        RegistryTweakStore.EnsureProtectedDirectory(dir);
        // Grava ao lado e troca: uma queda no meio não apaga o backup que já existia
        var temp = BackupPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(backup));
        File.Move(temp, BackupPath, overwrite: true);
    }

    public static bool IsDisabled(ServiceGroup group) => TryLoadBackup()?.ContainsKey(group.Id) == true;

    /// <summary>Serviços do grupo que existem neste Windows.</summary>
    public static string[] Existing(ServiceGroup group) =>
        group.Services.Where(name => { using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}"); return k != null; }).ToArray();

    private static string StartType(string service)
    {
        using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{service}");
        var start = k?.GetValue("Start") as int? ?? 3;
        var delayed = k?.GetValue("DelayedAutostart") as int? == 1;
        return start switch { 2 when delayed => "delayed-auto", 2 => "auto", 4 => "disabled", 0 => "boot", 1 => "system", _ => "demand" };
    }

    public async Task<int> DisableAsync(ServiceGroup group)
    {
        var backup = LoadBackup();
        var services = Existing(group);
        var original = backup.TryGetValue(group.Id, out var saved) ? saved : services.ToDictionary(s => s, StartType);
        backup[group.Id] = original;
        SaveBackup(backup); // o "antes" vai para o disco antes de mudar qualquer serviço
        var changed = 0;
        await Task.Run(() =>
        {
            foreach (var name in services)
            {
                if (GamingService.RunTool("sc.exe", "config", name, "start=", "disabled") == 0) changed++;
                try
                {
                    using var controller = new ServiceController(name);
                    if (controller.Status != ServiceControllerStatus.Stopped) { controller.Stop(); controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15)); }
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or System.ServiceProcess.TimeoutException) { }
            }
        });
        _log.Write("SUCCESS", $"Serviços desligados: {group.Name} ({changed}/{services.Length})");
        return changed;
    }

    public async Task<int> RestoreAsync(ServiceGroup group)
    {
        var backup = LoadBackup();
        if (!backup.TryGetValue(group.Id, out var original)) return 0;
        // Só restaura serviços que o próprio grupo declara: o arquivo não decide o que roda como administrador
        var allowed = group.Services.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validTypes = new HashSet<string> { "auto", "delayed-auto", "demand", "disabled", "boot", "system" };
        var restored = 0;
        var failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await Task.Run(() =>
        {
            foreach (var (name, type) in original.Where(o => allowed.Contains(o.Key) && validTypes.Contains(o.Value)))
            {
                if (GamingService.RunTool("sc.exe", "config", name, "start=", type) != 0) { failed[name] = type; continue; }
                restored++;
                if (type is "auto" or "delayed-auto")
                    try { using var c = new ServiceController(name); if (c.Status == ServiceControllerStatus.Stopped) c.Start(); }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { failed[name] = type; }
            }
        });
        if (failed.Count > 0) backup[group.Id] = failed;
        else backup.Remove(group.Id);
        SaveBackup(backup);
        if (failed.Count > 0)
        {
            _log.Write("WARN", $"Serviços parcialmente restaurados: {group.Name} ({restored}/{original.Count})");
            throw new InvalidOperationException($"Não foi possível restaurar {failed.Count} serviço(s): {string.Join(", ", failed.Keys)}");
        }
        _log.Write("SUCCESS", $"Serviços restaurados: {group.Name} ({restored}/{original.Count})");
        return restored;
    }
}
