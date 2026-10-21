using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>Um dispositivo que o Windows conhece (conectado agora ou só lembrado).</summary>
public sealed record DeviceEntry(string InstanceId, string Name, string Class, bool Present, string? Port, bool HasProblem)
{
    /// <summary>Remover é seguro: o Windows reinstala o driver sozinho se o aparelho voltar a ser conectado.</summary>
    public bool Removable => !Present && !DeviceCleanupService.IsProtected(this);
}

/// <summary>
/// Dispositivos fantasmas: tudo o que já foi conectado e o Windows continua lembrando (pendrives, controles, fones,
/// placas trocadas, portas COM antigas). Ocupam nomes de porta, letras e às vezes confundem drivers. A remoção usa o
/// mesmo instalador de classe do Gerenciador de Dispositivos (DIF_REMOVE) e só vale para o que não está conectado.
/// </summary>
public static class DeviceCleanupService
{
    // Classes de sistema que não devem ser tocadas mesmo ausentes (o Windows recria/gerencia sozinho)
    private static readonly HashSet<string> ProtectedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Computer", "Processor", "Volume", "VolumeSnapshot", "SoftwareDevice", "SoftwareComponent", "LegacyDriver",
        "HDC", "SCSIAdapter", "Firmware", "SecurityDevices", "Battery", "ACPI", "Extension",
    };

    internal static bool IsProtected(DeviceEntry d) =>
        ProtectedClasses.Contains(d.Class) || d.Class.Length == 0 || d.InstanceId.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase)
        || d.InstanceId.StartsWith(@"ACPI\", StringComparison.OrdinalIgnoreCase) || d.InstanceId.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase)
        || d.InstanceId.StartsWith(@"HTREE\", StringComparison.OrdinalIgnoreCase);

    public static List<DeviceEntry> List()
    {
        var list = new List<DeviceEntry>();
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES);
        if (set == InvalidHandle) throw new Win32Exception();
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                var id = InstanceId(set, ref data);
                if (id.Length == 0) continue;
                // CM_Get_DevNode_Status só responde para o que está presente
                var present = CM_Get_DevNode_Status(out var status, out _, data.DevInst, 0) == 0;
                var name = RegString(set, ref data, SPDRP_FRIENDLYNAME) ?? RegString(set, ref data, SPDRP_DEVICEDESC) ?? id;
                list.Add(new DeviceEntry(id, name, RegString(set, ref data, SPDRP_CLASS) ?? "", present, PortName(set, ref data), present && (status & DN_HAS_PROBLEM) != 0));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list.OrderBy(d => d.Present).ThenBy(d => d.Class, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Remove os dispositivos ausentes pedidos; conectados e protegidos são recusados.</summary>
    public static (int Removed, List<string> Failed) Remove(IEnumerable<string> instanceIds)
    {
        var wanted = new HashSet<string>(instanceIds, StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        var failed = new List<string>();
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES);
        if (set == InvalidHandle) throw new Win32Exception();
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                var id = InstanceId(set, ref data);
                if (!wanted.Contains(id)) continue;
                var entry = new DeviceEntry(id, id, RegString(set, ref data, SPDRP_CLASS) ?? "", CM_Get_DevNode_Status(out _, out _, data.DevInst, 0) == 0, null, false);
                if (entry.Present) failed.Add(id + (Translator.IsEnglish ? " (connected)" : " (está conectado)"));
                else if (IsProtected(entry)) failed.Add(id + (Translator.IsEnglish ? " (system device)" : " (dispositivo do sistema)"));
                else if (SetupDiCallClassInstaller(DIF_REMOVE, set, ref data)) removed++;
                else failed.Add($"{id} ({new Win32Exception(Marshal.GetLastWin32Error()).Message})");
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return (removed, failed);
    }

    // ================= Portas COM =================
    private const string ArbiterKey = @"SYSTEM\CurrentControlSet\Control\COM Name Arbiter";

    /// <summary>
    /// Portas COM reservadas no Windows (ComDB) e as que pertencem a algum aparelho instalado (conectado ou não).
    /// Só as reservas sem nenhum aparelho são órfãs: um Arduino desconectado continua com a porta dele.
    /// </summary>
    public static (List<int> Reserved, List<int> InUse) ComStatus()
    {
        var reserved = ReadComDb() is { } db ? ReservedPorts(db).ToList() : new List<int>();
        return (reserved, PortsAssigned());
    }

    /// <summary>ComDB atual (null se a chave não existir).</summary>
    public static byte[]? ReadComDb()
    {
        using var key = Registry.LocalMachine.OpenSubKey(ArbiterKey);
        return key?.GetValue("ComDB") as byte[];
    }

    /// <summary>Lê o bitmap do ComDB: o bit n-1 ligado = COMn reservada.</summary>
    public static IEnumerable<int> ReservedPorts(byte[] db)
    {
        for (var port = 1; port <= db.Length * 8; port++)
            if ((db[(port - 1) / 8] & (1 << ((port - 1) % 8))) != 0) yield return port;
    }

    /// <summary>Monta um ComDB só com as portas em uso (as outras voltam a ficar livres para novos aparelhos).</summary>
    public static byte[] BuildComDb(IEnumerable<int> inUse, int length)
    {
        var db = new byte[Math.Max(32, length)];
        foreach (var port in inUse.Where(p => p >= 1 && p <= db.Length * 8)) db[(port - 1) / 8] |= (byte)(1 << ((port - 1) % 8));
        return db;
    }

    /// <summary>Portas abertas agora (SERIALCOMM) e as atribuídas a qualquer aparelho instalado, mesmo desconectado.</summary>
    private static List<int> PortsAssigned()
    {
        var ports = new HashSet<int>();
        using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
            if (key != null)
                foreach (var name in key.GetValueNames())
                    if (ParseCom(key.GetValue(name) as string) is { } p) ports.Add(p);
        foreach (var d in List())
            if (ParseCom(d.Port) is { } p) ports.Add(p);
        return ports.OrderBy(p => p).ToList();
    }

    private static int? ParseCom(string? s) =>
        s != null && s.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.AsSpan(3), out var n) ? n : null;

    /// <summary>
    /// Libera só as reservas órfãs (nenhum aparelho instalado com aquela porta). O chamador grava o backup
    /// (<see cref="ReadComDb"/>) antes de chamar.
    /// </summary>
    public static void ReleaseOrphanPorts()
    {
        using var key = Registry.LocalMachine.OpenSubKey(ArbiterKey, writable: true) ?? throw new InvalidOperationException(Translator.IsEnglish ? "COM Name Arbiter key not found." : "Chave COM Name Arbiter não encontrada.");
        var current = key.GetValue("ComDB") as byte[] ?? new byte[32];
        var assigned = PortsAssigned();
        // Mantém o que está reservado e tem dono; nunca reserva porta nova
        var keep = ReservedPorts(current).Where(assigned.Contains).ToList();
        key.SetValue("ComDB", BuildComDb(keep, current.Length), RegistryValueKind.Binary);
    }

    /// <summary>Desfaz: volta as reservas do backup sem apagar as que surgiram depois (união dos dois).</summary>
    public static void RestoreComDb(byte[] backup)
    {
        using var key = Registry.LocalMachine.OpenSubKey(ArbiterKey, writable: true) ?? throw new InvalidOperationException(Translator.IsEnglish ? "COM Name Arbiter key not found." : "Chave COM Name Arbiter não encontrada.");
        var current = key.GetValue("ComDB") as byte[] ?? Array.Empty<byte>();
        var merged = new byte[Math.Max(current.Length, backup.Length)];
        for (var i = 0; i < merged.Length; i++) merged[i] = (byte)((i < current.Length ? current[i] : 0) | (i < backup.Length ? backup[i] : 0));
        key.SetValue("ComDB", merged, RegistryValueKind.Binary);
    }

    // ================= SetupAPI =================
    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    private static readonly IntPtr InvalidHandle = new(-1);
    private const int DIGCF_ALLCLASSES = 4;
    private const uint SPDRP_DEVICEDESC = 0, SPDRP_CLASS = 7, SPDRP_FRIENDLYNAME = 12;
    private const uint DN_HAS_PROBLEM = 0x400, DIF_REMOVE = 5;

    private static string InstanceId(IntPtr set, ref SP_DEVINFO_DATA data)
    {
        var sb = new StringBuilder(512);
        return SetupDiGetDeviceInstanceIdW(set, ref data, sb, sb.Capacity, out _) ? sb.ToString() : "";
    }

    private static string? RegString(IntPtr set, ref SP_DEVINFO_DATA data, uint property)
    {
        var buffer = new byte[1024];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, buffer, (uint)buffer.Length, out var required)) return null;
        var text = Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(required, (uint)buffer.Length)).TrimEnd('\0');
        return text.Length > 0 ? text : null;
    }

    private static string? PortName(IntPtr set, ref SP_DEVINFO_DATA data)
    {
        var handle = SetupDiOpenDevRegKey(set, ref data, 1, 0, 1, 0x20019);
        if (handle == InvalidHandle || handle == IntPtr.Zero) return null;
        using var key = RegistryKey.FromHandle(new Microsoft.Win32.SafeHandles.SafeRegistryHandle(handle, ownsHandle: true));
        return key.GetValue("PortName") as string;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder id, int size, out int required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, uint property, out uint regType, byte[] buffer, uint size, out uint required);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiOpenDevRegKey(IntPtr set, ref SP_DEVINFO_DATA data, uint scope, uint hwProfile, uint keyType, uint access);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(uint function, IntPtr set, ref SP_DEVINFO_DATA data);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
}
