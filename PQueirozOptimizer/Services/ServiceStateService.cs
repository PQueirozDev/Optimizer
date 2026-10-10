using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>Estado atual de um serviço do Windows.</summary>
public sealed record ServiceState(string Name, string DisplayName, bool Exists, bool Running, string StartType, bool CanStop)
{
    public bool Disabled => StartType == "disabled";
}

/// <summary>
/// Página Serviços → Estado dos serviços: mostra se os serviços que o app (ou outros otimizadores) costuma
/// parar estão rodando, e inicia ou para cada um. Iniciar um serviço desativado volta o tipo de início ao
/// padrão do Windows antes, senão o Windows recusa.
/// </summary>
public sealed class ServiceStateService
{
    private readonly ActivityLog _log;
    public ServiceStateService(ActivityLog log) => _log = log;

    /// <summary>Serviços que verificações de anti-cheat ("telagem", 2ª etapa) conferem se estão rodando.</summary>
    public static readonly string[] CheckedServices = { "PcaSvc", "DPS", "DiagTrack", "SysMain", "EventLog" };

    // Tipo de início padrão do Windows 10/11 (usado ao reativar um serviço desativado)
    private static readonly Dictionary<string, string> DefaultStart = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PcaSvc"] = "auto", ["DPS"] = "auto", ["DiagTrack"] = "auto", ["SysMain"] = "auto", ["EventLog"] = "auto",
        ["WSearch"] = "delayed-auto", ["Spooler"] = "auto", ["MapsBroker"] = "delayed-auto", ["WerSvc"] = "demand",
        ["wuauserv"] = "demand", ["BITS"] = "demand", ["DoSvc"] = "delayed-auto", ["UsoSvc"] = "delayed-auto",
    };

    // Serviços essenciais que o Windows não deixa parar
    private static readonly HashSet<string> Unstoppable = new(StringComparer.OrdinalIgnoreCase) { "EventLog" };

    /// <summary>Outros serviços que as otimizações, os grupos e o Modo Jogo podem parar.</summary>
    public static IEnumerable<string> OtherServices() =>
        ServiceGroupsService.Groups.SelectMany(g => g.Services)
            .Concat(GamingService.PausableServices.Select(s => s.Name))
            .Concat(new[] { "WSearch", "WerSvc", "dmwappushservice", "diagnosticshub.standardcollector.service", "WdiServiceHost", "WdiSystemHost", "PushToInstall", "MapsBroker", "Fax" })
            .Where(name => !CheckedServices.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    public static ServiceState Read(string name)
    {
        try
        {
            using var service = new ServiceController(name);
            var running = service.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
            return new ServiceState(name, service.DisplayName, true, running, StartType(name), !Unstoppable.Contains(name));
        }
        catch (InvalidOperationException)
        {
            return new ServiceState(name, name, false, false, "", false);
        }
    }

    private static string StartType(string service)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{service}");
        var start = key?.GetValue("Start") as int? ?? 3;
        var delayed = key?.GetValue("DelayedAutostart") as int? == 1;
        return start switch { 2 when delayed => "delayed-auto", 2 => "auto", 4 => "disabled", 0 => "boot", 1 => "system", _ => "demand" };
    }

    public Task StartAsync(string name, CancellationToken token = default) => Task.Run(() =>
    {
        var wasDisabled = StartType(name) == "disabled";
        if (wasDisabled)
        {
            var start = DefaultStart.TryGetValue(name, out var s) ? s : "demand";
            var code = GamingService.RunTool("sc.exe", "config", name, "start=", start);
            if (code != 0) throw new InvalidOperationException($"O Windows não deixou reativar {name} (código {code}).");
            _log.Write("INFO", $"Serviço {name}: tipo de início voltou ao padrão ({start})");
        }
        using var service = new ServiceController(name);
        try
        {
            if (service.Status != ServiceControllerStatus.Running)
            {
                try { service.Start(); }
                catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 1056 }) { } // já está rodando
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            }
        }
        catch (Exception ex) when (wasDisabled && ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            // Não iniciou (dependência, erro do serviço): volta a ficar desativado como o usuário tinha deixado
            GamingService.RunTool("sc.exe", "config", name, "start=", "disabled");
            _log.Write("WARN", $"Serviço {name} não iniciou; o tipo de início voltou a desativado");
            throw;
        }
        _log.Write("SUCCESS", $"Serviço iniciado: {service.DisplayName} ({name})");
    }, token);

    public Task StopAsync(string name, CancellationToken token = default) => Task.Run(() =>
    {
        if (Unstoppable.Contains(name)) throw new InvalidOperationException("O Windows não permite parar este serviço.");
        using var service = new ServiceController(name);
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
        _log.Write("SUCCESS", $"Serviço parado: {service.DisplayName} ({name}) — volta a iniciar sozinho na próxima vez que o Windows precisar dele ou ao reiniciar");
    }, token);
}
