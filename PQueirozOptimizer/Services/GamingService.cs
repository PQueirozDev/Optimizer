using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>Processador AMD com 3D V-Cache. Nos modelos com dois CCDs o Windows depende do driver de chipset, da Game Bar e do plano Equilibrado para mandar o jogo ao CCD com cache.</summary>
public sealed record X3dInfo(string Model, bool DualCcd);

/// <summary>Programa em segundo plano que o Modo Jogo pode fechar.</summary>
public sealed record BackgroundApp(string Name, string[] Processes, string Description);

/// <summary>Serviço que o Modo Jogo pausa durante a sessão e reinicia ao sair.</summary>
public sealed record PausableService(string Name, string Title, string Description, bool Default);

/// <summary>Runtime instalado pelo winget.</summary>
public sealed record Redistributable(string WingetId, string Title, string Description);

/// <summary>
/// Recursos de jogo: Modo Jogo temporário (fecha programas e pausa serviços enquanto você joga, e devolve tudo
/// ao sair), perfis por executável, detecção de processadores X3D, limpeza da memória em espera e runtimes.
/// </summary>
public sealed class GamingService
{
    private readonly ActivityLog _log;
    private readonly RegistryTweakStore _tweaks;
    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "GameMode");
    private static readonly string SessionPath = Path.Combine(DataDirectory, "sessao.json");

    public GamingService(ActivityLog log, RegistryTweakStore? tweaks = null) { _log = log; _tweaks = tweaks ?? new RegistryTweakStore(); }

    // ================= X3D =================
    public static string ProcessorName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
    }

    /// <summary>Reconhece Ryzen X3D pelo nome. 7900X3D, 7950X3D, 9900X3D e 9950X3D têm dois CCDs (só um com o cache extra).</summary>
    public static X3dInfo? DetectX3d(string processorName)
    {
        var m = Regex.Match(processorName, @"Ryzen\s+\d+\s+(\d{4})X3D", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var model = m.Groups[1].Value;
        return new X3dInfo(model + "X3D", model is "7900" or "7950" or "9900" or "9950");
    }

    /// <summary>Driver "AMD 3D V-Cache Performance Optimizer", instalado pelo pacote de chipset da AMD.</summary>
    public static bool HasX3dChipsetDriver()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\amd3dvcache");
        return key != null;
    }

    public static bool IsGameBarAllowed()
    {
        using var policy = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR");
        return policy?.GetValue("AllowGameDVR") is not 0;
    }

    // ================= Modo Jogo (sessão temporária) =================
    public static readonly BackgroundApp[] BackgroundApps =
    {
        new("OneDrive", new[] { "OneDrive" }, "Sincronização de arquivos na nuvem"),
        new("Microsoft Teams", new[] { "ms-teams", "Teams" }, "Chat e reuniões"),
        new("Vincular ao Celular", new[] { "PhoneExperienceHost", "YourPhone" }, "Notificações do celular no PC"),
        new("Widgets", new[] { "Widgets", "WidgetService" }, "Painel de notícias e clima"),
        new("Microsoft Edge (em segundo plano)", new[] { "msedge" }, "Feche só se não estiver usando o navegador"),
        new("Google Chrome", new[] { "chrome" }, "Feche só se não estiver usando o navegador"),
        new("Adobe Creative Cloud", new[] { "Creative Cloud", "CCXProcess", "CCLibrary", "AdobeIPCBroker", "AdobeCollabSync" }, "Sincronização e atualizações da Adobe"),
        new("Google Drive", new[] { "GoogleDriveFS" }, "Sincronização de arquivos na nuvem"),
        new("Dropbox", new[] { "Dropbox" }, "Sincronização de arquivos na nuvem"),
        new("Spotify", new[] { "Spotify" }, "Feche só se não estiver ouvindo música"),
        new("Copilot", new[] { "Copilot", "M365Copilot" }, "Assistente da Microsoft"),
    };

    /// <summary>Programas que o Modo Jogo nunca fecha (jogos, launchers, voz e o próprio Windows).</summary>
    private static readonly HashSet<string> NeverClose = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "csrss", "winlogon", "lsass", "svchost", "services", "System", "Idle", "steam", "steamwebhelper",
        "EpicGamesLauncher", "Battle.net", "RiotClientServices", "Discord", "PQueirozOptimizer",
    };

    public static readonly PausableService[] PausableServices =
    {
        new("wuauserv", "Windows Update", "Evita downloads e instalações de atualizações no meio da partida.", true),
        new("BITS", "Transferência inteligente em segundo plano", "Usado pelo Windows Update e por outros downloads automáticos.", true),
        new("DoSvc", "Otimização de Entrega", "Compartilha atualizações com outros PCs pela internet.", true),
        new("SysMain", "SysMain (Superfetch)", "Pré-carrega programas na memória e usa o disco em segundo plano.", true),
        new("WSearch", "Windows Search", "Indexação de arquivos para a pesquisa.", true),
        new("DiagTrack", "Telemetria (DiagTrack)", "Envio de dados de diagnóstico para a Microsoft.", true),
        new("Spooler", "Spooler de impressão", "Só pause se não for imprimir durante a sessão.", false),
    };

    public sealed class GameSession
    {
        public DateTime StartedAtUtc { get; set; }
        public List<string> StoppedServices { get; set; } = new();
        public string? PreviousPowerPlan { get; set; }
        public List<string> ClosedApps { get; set; } = new();
        public bool RecoveryPending { get; set; }
    }

    public static GameSession? ActiveSession()
    {
        try { return File.Exists(SessionPath) ? JsonSerializer.Deserialize<GameSession>(File.ReadAllText(SessionPath)) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Nomes amigáveis dos programas da lista que estão abertos agora.</summary>
    public static HashSet<string> RunningBackgroundApps()
    {
        var running = Process.GetProcesses().Select(p => { try { return p.ProcessName; } finally { p.Dispose(); } }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return BackgroundApps.Where(a => a.Processes.Any(running.Contains)).Select(a => a.Name).ToHashSet();
    }

    public async Task<GameSession> StartSessionAsync(IReadOnlyCollection<string> appsToClose, IReadOnlyCollection<string> servicesToPause, bool highPerformancePlan, bool purgeStandby, IProgress<string>? progress = null)
    {
        if (ActiveSession() != null) throw new InvalidOperationException("O Modo Jogo já está ativo. Desative-o antes de iniciar outra sessão.");
        var session = new GameSession { StartedAtUtc = DateTime.UtcNow };
        // A sessão é salva antes de mexer em qualquer coisa e a cada serviço pausado: se o PC travar, dá para restaurar
        SaveSession(session);

        await Task.Run(() =>
        {
            foreach (var app in BackgroundApps.Where(a => appsToClose.Contains(a.Name)))
            {
                var closed = 0;
                foreach (var name in app.Processes.Where(n => !NeverClose.Contains(n)))
                    foreach (var process in Process.GetProcessesByName(name))
                        using (process)
                        {
                            try
                            {
                                // Pede para fechar como o usuário faria; só força se o programa não responder
                                if (!process.CloseMainWindow() || !process.WaitForExit(3000)) process.Kill(entireProcessTree: true);
                                closed++;
                            }
                            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
                        }
                if (closed > 0) { session.ClosedApps.Add(app.Name); progress?.Report($"Fechado: {app.Name}"); }
            }

            foreach (var service in PausableServices.Where(s => servicesToPause.Contains(s.Name)))
            {
                try
                {
                    using var controller = new ServiceController(service.Name);
                    if (controller.Status != ServiceControllerStatus.Running) continue;
                    // Registra antes de parar: uma interrupção ou timeout não pode perder a recuperação.
                    session.StoppedServices.Add(service.Name);
                    SaveSession(session);
                    controller.Stop();
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    progress?.Report($"Pausado: {service.Title}");
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or System.ServiceProcess.TimeoutException)
                {
                    progress?.Report($"Não foi possível pausar {service.Title}: {ex.Message}");
                }
            }

            if (highPerformancePlan)
            {
                // O plano Qrz, quando instalado, é o plano de jogo do app; senão, o Alto desempenho do Windows
                var qrz = PowerPlanService.IsQrzInstalled();
                var target = qrz ? PowerPlanService.QrzGuid.ToString() : HighPerformancePlan;
                var current = ActivePowerPlan();
                if (current != null && !current.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    session.PreviousPowerPlan = current;
                    SaveSession(session);
                    if (RunTool("powercfg.exe", "/setactive", target) == 0)
                        progress?.Report(qrz ? "Plano de energia: Qrz" : "Plano de energia: Alto desempenho");
                }
            }
            SaveSession(session);

            if (purgeStandby)
            {
                var freed = PurgeStandbyList();
                progress?.Report(freed ? "Memória em espera liberada" : "Não foi possível liberar a memória em espera");
            }
        });
        _log.Write("SUCCESS", $"Modo Jogo ativado: {session.ClosedApps.Count} programa(s) fechado(s), {session.StoppedServices.Count} serviço(s) pausado(s)");
        return session;
    }

    public async Task<int> EndSessionAsync(IProgress<string>? progress = null)
    {
        var session = ActiveSession();
        if (session is null) return 0;
        var restored = await Task.Run(() => RestoreSession(session, name =>
        {
            using var controller = new ServiceController(name);
            if (controller.Status == ServiceControllerStatus.Running) return;
            if (controller.Status == ServiceControllerStatus.StopPending)
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            controller.Refresh();
            if (controller.Status is not (ServiceControllerStatus.StartPending or ServiceControllerStatus.Running)) controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }, plan => RunTool("powercfg.exe", "/setactive", plan) == 0, SaveSession, progress));
        if (session.StoppedServices.Count > 0 || session.PreviousPowerPlan != null)
        {
            const string message = "Restauração incompleta. As pendências foram salvas; tente novamente no Modo Jogo.";
            _log.Write("WARN", message);
            throw new InvalidOperationException(message);
        }
        File.Delete(SessionPath);
        _log.Write("SUCCESS", "Modo Jogo desativado: serviços e plano de energia restaurados");
        return restored;
    }

    internal static int RestoreSession(GameSession session, Action<string> restoreService,
        Func<string, bool> restorePlan, Action<GameSession> save, IProgress<string>? progress = null)
    {
        session.RecoveryPending = true;
        save(session);
        var restored = 0;
        var allowed = PausableServices.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in session.StoppedServices.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            if (!allowed.Contains(name)) continue;
            try { restoreService(name); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or System.ServiceProcess.TimeoutException)
            {
                progress?.Report($"Restauração pendente: {name}: {ex.Message}");
                continue;
            }
            session.StoppedServices.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            save(session);
            restored++;
            progress?.Report($"Reiniciado: {name}");
        }
        if (session.PreviousPowerPlan is { } plan && Guid.TryParse(plan, out _))
        {
            var success = false;
            try { success = restorePlan(plan); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            { progress?.Report("Restauração do plano pendente: " + ex.Message); }
            if (success)
            {
                session.PreviousPowerPlan = null;
                save(session);
                progress?.Report("Plano de energia anterior restaurado");
            }
        }
        return restored;
    }

    private static void SaveSession(GameSession session)
    {
        if (!Directory.Exists(DataDirectory)) { Directory.CreateDirectory(DataDirectory); RegistryTweakStore.ProtectDirectory(DataDirectory); }
        var temp = SessionPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(session));
        File.Move(temp, SessionPath, overwrite: true);
    }

    private const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public static string? ActivePowerPlan()
    {
        var output = RunToolOutput("powercfg.exe", "/getactivescheme");
        var m = Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return m.Success ? m.Value : null;
    }

    // ================= Memória em espera =================
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int length, IntPtr previous, IntPtr returnLength);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct TokenPrivileges { public int Count; public long Luid; public int Attributes; }

    /// <summary>
    /// Esvazia a lista de memória em espera (o mesmo que o ISLC/RAMMap fazem). O Windows volta a enchê-la
    /// conforme os arquivos são usados; ajuda em jogos que sofrem engasgos quando a RAM está quase cheia.
    /// </summary>
    public static bool PurgeStandbyList()
    {
        const uint adjust = 0x20, query = 0x8;
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, adjust | query, out var token)) return false;
        try
        {
            if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid)) return false;
            var tp = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() != 0) return false;
            var command = 4; // MemoryPurgeStandbyList
            return NtSetSystemInformation(80 /* SystemMemoryListInformation */, ref command, sizeof(int)) == 0;
        }
        finally { CloseHandle(token); }
    }

    // ================= Perfis por jogo =================
    private const string GpuPrefsKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string IfeoKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    public static string GameProfileId(string exePath) =>
        "game-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(exePath.ToLowerInvariant())))[..16];

    public IReadOnlyList<RegistryTweakStore.AppliedTweak> GameProfiles() => _tweaks.List("game-");

    /// <summary>
    /// Perfil de jogo: placa de vídeo dedicada, sem otimizações de tela cheia (menos atraso em alguns jogos
    /// antigos) e prioridade de CPU alta sempre que o executável abrir. Tudo reversível.
    /// </summary>
    public void ApplyGameProfile(string exePath, bool gpuHighPerformance, bool disableFullscreenOptimizations, bool highPriority)
    {
        if (!File.Exists(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("Escolha o arquivo .exe do jogo.", exePath);
        var exeName = Path.GetFileName(exePath);
        if (NeverClose.Contains(Path.GetFileNameWithoutExtension(exePath))) throw new InvalidOperationException("Esse programa é do sistema e não pode receber um perfil de jogo.");
        var writes = new List<RegistryWrite>();
        if (gpuHighPerformance) writes.Add(new(RegistryHive.CurrentUser, GpuPrefsKey, exePath, RegistryValueKind.String, "GpuPreference=2;"));
        if (disableFullscreenOptimizations) writes.Add(new(RegistryHive.CurrentUser, LayersKey, exePath, RegistryValueKind.String, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE"));
        if (highPriority) writes.Add(new(RegistryHive.LocalMachine, $@"{IfeoKey}\{exeName}\PerfOptions", "CpuPriorityClass", RegistryValueKind.DWord, 3));
        if (writes.Count == 0) throw new InvalidOperationException("Marque pelo menos um ajuste para o perfil.");
        var id = GameProfileId(exePath);
        // Reaplicar com outras opções: desfaz o perfil antigo para o novo não carregar ajustes desmarcados
        if (_tweaks.IsApplied(id)) RevertGameProfile(exePath);
        var options = string.Join(", ", new[] { gpuHighPerformance ? "GPU dedicada" : null, disableFullscreenOptimizations ? "sem otimização de tela cheia" : null, highPriority ? "prioridade alta" : null }.Where(o => o != null));
        _tweaks.Apply(id, Path.GetFileNameWithoutExtension(exePath), writes, new Dictionary<string, string> { ["path"] = exePath, ["options"] = options });
        _log.Write("SUCCESS", $"Perfil de jogo aplicado: {exeName} ({options})");
    }

    public void RevertGameProfile(string exePath)
    {
        var exeName = Path.GetFileName(exePath);
        _tweaks.Revert(GameProfileId(exePath), (hive, key, name) =>
            (hive == RegistryHive.CurrentUser && (key.Equals(GpuPrefsKey, StringComparison.OrdinalIgnoreCase) || key.Equals(LayersKey, StringComparison.OrdinalIgnoreCase)) && name.Equals(exePath, StringComparison.OrdinalIgnoreCase))
            || (hive == RegistryHive.LocalMachine && key.Equals($@"{IfeoKey}\{exeName}\PerfOptions", StringComparison.OrdinalIgnoreCase) && name == "CpuPriorityClass"));
        _log.Write("SUCCESS", $"Perfil de jogo removido: {exeName}");
    }

    // ================= Menos processos svchost =================
    private const string ControlKey = @"SYSTEM\CurrentControlSet\Control";
    public const string SvchostTweakId = "svchost-split";
    public bool IsSvchostReductionApplied => _tweaks.IsApplied(SvchostTweakId);

    /// <summary>
    /// Agrupa os serviços em menos processos svchost (o limite passa a ser a RAM instalada). Reduz a
    /// contagem de processos e um pouco de memória; vale após reiniciar.
    /// </summary>
    public void ApplySvchostReduction()
    {
        var status = new HardwareMonitorServiceMemory();
        var kb = (int)Math.Min(int.MaxValue, status.TotalKb);
        _tweaks.Apply(SvchostTweakId, "Menos processos svchost", new[] { new RegistryWrite(RegistryHive.LocalMachine, ControlKey, "SvcHostSplitThresholdInKB", RegistryValueKind.DWord, kb) });
        _log.Write("SUCCESS", "Agrupamento de serviços svchost aplicado (vale após reiniciar)");
    }

    public void RevertSvchostReduction()
    {
        _tweaks.Revert(SvchostTweakId, (hive, key, name) => hive == RegistryHive.LocalMachine && key.Equals(ControlKey, StringComparison.OrdinalIgnoreCase) && name == "SvcHostSplitThresholdInKB");
        _log.Write("SUCCESS", "Agrupamento de serviços svchost revertido (vale após reiniciar)");
    }

    private sealed class HardwareMonitorServiceMemory
    {
        [StructLayout(LayoutKind.Sequential)] private struct MemoryStatusEx { public uint Length, MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual; }
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
        public ulong TotalKb { get; }
        public HardwareMonitorServiceMemory() { var s = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() }; TotalKb = GlobalMemoryStatusEx(ref s) ? s.TotalPhys / 1024 : 3670016; }
    }

    // ================= Runtimes =================
    public static readonly Redistributable[] Redistributables =
    {
        new("Microsoft.VCRedist.2015+.x64", "Visual C++ 2015–2022 (64 bits)", "Exigido pela maioria dos jogos atuais."),
        new("Microsoft.VCRedist.2015+.x86", "Visual C++ 2015–2022 (32 bits)", "Jogos e launchers de 32 bits."),
        new("Microsoft.VCRedist.2013.x64", "Visual C++ 2013 (64 bits)", "Jogos de 2013 a 2016."),
        new("Microsoft.VCRedist.2013.x86", "Visual C++ 2013 (32 bits)", "Jogos de 2013 a 2016."),
        new("Microsoft.VCRedist.2012.x64", "Visual C++ 2012 (64 bits)", "Jogos mais antigos."),
        new("Microsoft.VCRedist.2010.x64", "Visual C++ 2010 (64 bits)", "Jogos mais antigos."),
        new("Microsoft.DirectX", "DirectX (runtime de usuário final)", "Bibliotecas do DirectX 9/10/11 usadas por jogos antigos."),
        new("Microsoft.DotNet.DesktopRuntime.8", ".NET Desktop Runtime 8", "Launchers e ferramentas feitas em .NET."),
        new("Microsoft.XNARedist", "XNA Framework 4.0", "Jogos indie como Terraria e Stardew Valley (versões antigas)."),
    };

    public static bool IsWingetAvailable()
    {
        try { return RunTool("winget.exe", "--version") == 0; }
        catch (Win32Exception) { return false; }
    }

    /// <summary>Instala pelo winget. "Já instalado" e "sem atualização" contam como sucesso.</summary>
    public async Task<(int Ok, int Failed)> InstallRedistributablesAsync(IReadOnlyCollection<string> ids, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var allowed = Redistributables.Select(r => r.WingetId).ToHashSet(StringComparer.Ordinal);
        int ok = 0, failed = 0;
        foreach (var r in Redistributables.Where(r => ids.Contains(r.WingetId) && allowed.Contains(r.WingetId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Instalando {r.Title}...");
            var code = await Task.Run(() => RunTool("winget.exe", "install", "--id", r.WingetId, "-e", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity", "--source", "winget"), cancellationToken);
            // 0x8A15002B: nenhuma atualização aplicável (já instalado); 0x8A150061: pacote já instalado
            if (code is 0 or unchecked((int)0x8A15002B) or unchecked((int)0x8A150061) or 3010) { ok++; progress?.Report($"OK: {r.Title}"); }
            else { failed++; progress?.Report($"[ERRO] {r.Title}: código {code}"); }
        }
        _log.Write(failed == 0 ? "SUCCESS" : "WARN", $"Runtimes: {ok} instalado(s) ou já presente(s), {failed} falha(s)");
        return (ok, failed);
    }

    // ================= Utilitários =================
    internal static int RunTool(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar " + file);
        p.StandardOutput.ReadToEndAsync(); p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(600_000)) { try { p.Kill(true); } catch (InvalidOperationException) { } return -1; }
        return p.ExitCode;
    }

    internal static string RunToolOutput(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return "";
            var output = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            return p.WaitForExit(30_000) ? output.GetAwaiter().GetResult() : "";
        }
        catch (Win32Exception) { return ""; }
    }
}
