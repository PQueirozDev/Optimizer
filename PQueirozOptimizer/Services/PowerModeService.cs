using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PQueirozOptimizer.Services;

/// <summary>Plano de energia instalado no Windows.</summary>
public sealed record PowerPlan(Guid Id, string Name);

/// <summary>
/// "Modo de energia" do Windows 10/11 (Configurações > Sistema > Energia). Ele vale sobre o plano
/// Equilibrado e é o jeito recomendado de trocar desempenho por bateria em notebooks.
/// </summary>
public enum PowerMode { Efficiency, Balanced, Performance }

/// <summary>Tomada/bateria no momento da leitura.</summary>
public sealed record PowerSource(bool HasBattery, bool PluggedIn, int? Percent, TimeSpan? Remaining, bool BatterySaver);

/// <summary>Lê e troca o plano e o modo de energia. Nada aqui exige administrador.</summary>
public static class PowerModeService
{
    public static readonly Guid BalancedPlan = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    // Valores usados pelo próprio Windows para o modo de energia (o Equilibrado é o GUID vazio)
    private static readonly Guid OverlayEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid OverlayPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static Guid OverlayFor(PowerMode mode) => mode switch
    {
        PowerMode.Efficiency => OverlayEfficiency,
        PowerMode.Performance => OverlayPerformance,
        _ => Guid.Empty,
    };

    public static PowerMode ModeFromOverlay(Guid overlay) =>
        overlay == OverlayEfficiency ? PowerMode.Efficiency : overlay == OverlayPerformance ? PowerMode.Performance : PowerMode.Balanced;

    public static IReadOnlyList<PowerPlan> GetPlans()
    {
        var plans = new List<PowerPlan>();
        for (uint index = 0; ; index++)
        {
            var buffer = new byte[16];
            var size = (uint)buffer.Length;
            if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref size) != 0) break;
            var id = new Guid(buffer);
            plans.Add(new PowerPlan(id, ReadPlanName(id)));
        }
        return plans;
    }

    public static Guid? GetActivePlan()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    public static void SetActivePlan(Guid plan)
    {
        var result = PowerSetActiveScheme(IntPtr.Zero, ref plan);
        if (result != 0) throw new InvalidOperationException($"O Windows recusou a troca do plano de energia (código {result}).");
    }

    /// <summary>O modo de energia existe no Windows 10 1709+ e precisa do plano Equilibrado instalado.</summary>
    public static bool ModesAvailable => GetMode() != null && GetPlans().Any(p => p.Id == BalancedPlan);

    /// <summary>Modo atual, ou null quando esta versão do Windows não oferece o modo de energia.</summary>
    public static PowerMode? GetMode()
    {
        try { return PowerGetEffectiveOverlayScheme(out var overlay) == 0 ? ModeFromOverlay(overlay) : null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    /// <summary>O modo só tem efeito com o plano Equilibrado ativo, então ele é ativado antes.</summary>
    public static void SetMode(PowerMode mode)
    {
        if (GetActivePlan() != BalancedPlan) SetActivePlan(BalancedPlan);
        var result = PowerSetActiveOverlayScheme(OverlayFor(mode));
        if (result != 0) throw new InvalidOperationException($"O Windows recusou a troca do modo de energia (código {result}).");
    }

    public static PowerSource GetSource()
    {
        if (!GetSystemPowerStatus(out var status)) return new PowerSource(false, true, null, null, false);
        var hasBattery = (status.BatteryFlag & 128) == 0 && status.BatteryFlag != 255;
        return new PowerSource(
            hasBattery,
            status.ACLineStatus != 0,
            hasBattery && status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null,
            hasBattery && status.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(status.BatteryLifeTime) : null,
            status.SystemStatusFlag == 1);
    }

    /// <summary>Texto curto para o topo do seletor: "Na tomada · 80%", "Na bateria · 45% · cerca de 2h 10min restantes".</summary>
    public static string Describe(PowerSource source)
    {
        if (!source.HasBattery) return "Computador sem bateria (desktop)";
        var parts = new List<string> { source.PluggedIn ? "Na tomada" : "Na bateria" };
        if (source.Percent is { } percent) parts.Add($"{percent}%");
        if (!source.PluggedIn && source.Remaining is { } left) parts.Add($"cerca de {(int)left.TotalHours}h {left.Minutes:00}min restantes");
        if (source.BatterySaver) parts.Add("Economia de bateria ativa");
        return string.Join(" · ", parts);
    }

    #region Atalho na Área de Trabalho
    public static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Modo de Energia.lnk");
    public static bool IsShortcutCreated() => File.Exists(ShortcutPath);

    /// <summary>
    /// O executável pede administrador ao abrir; trocar o modo de energia não precisa disso. O atalho
    /// passa pelo cmd com __COMPAT_LAYER=RunAsInvoker, que abre o seletor com as permissões do
    /// usuário e sem a janela do UAC a cada clique.
    /// </summary>
    public static string ShortcutArguments(string exe) => $"/c set \"__COMPAT_LAYER=RunAsInvoker\" && start \"\" \"{exe}\" --power-mode";

    public static void CreateShortcut(ActivityLog log)
    {
        var exe = ShortcutFile.AppExecutable;
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var icon = Path.Combine(system, "powercpl.dll");
        // Janela minimizada (7): o cmd só repassa o comando e fecha
        ShortcutFile.Create(ShortcutPath, Path.Combine(system, "cmd.exe"), ShortcutArguments(exe), AppContext.BaseDirectory,
            File.Exists(icon) ? icon + ",0" : exe + ",0", "Modo de energia - Qrztweaks", windowStyle: 7);
        log.Write("SUCCESS", "Atalho de modo de energia criado na Área de Trabalho");
    }

    public static void RemoveShortcut(ActivityLog log)
    {
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        log.Write("INFO", "Atalho de modo de energia removido");
    }
    #endregion

    private static string ReadPlanName(Guid plan)
    {
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, ref plan, IntPtr.Zero, IntPtr.Zero, null, ref size) != 0 || size == 0) return plan.ToString();
        var buffer = new byte[size];
        return PowerReadFriendlyName(IntPtr.Zero, ref plan, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
            ? Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : plan.ToString();
    }

    private const uint AccessScheme = 16; // ACCESS_SCHEME

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, uint accessFlags, uint index, [Out] byte[] buffer, ref uint bufferSize);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupGuid, IntPtr powerSettingGuid, [Out] byte[]? buffer, ref uint bufferSize);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);
    // Funções do modo de energia (Windows 10 1709+); ficam fora da documentação pública, mas são as que o Configurações usa
    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlaySchemeGuid);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
