using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Plano de energia Qrz: as 182 configurações de um plano de baixa latência (Assets/Qrz.powerplan.txt),
/// gravadas pela API do Windows sobre uma cópia do Equilibrado. Ativar guarda o plano anterior; restaurar
/// volta a ele e apaga o Qrz.
/// </summary>
public sealed class PowerPlanService
{
    public static readonly Guid QrzGuid = new("0a7f1b2c-5172-4e0a-9c11-517a00000001");
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly string StatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "Tweaks", "plano-qrz.json");
    private readonly ActivityLog _log;

    public PowerPlanService(ActivityLog log) => _log = log;

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerDuplicateScheme(IntPtr root, ref Guid source, ref IntPtr destination);
    [DllImport("powrprof.dll")] private static extern uint PowerDeleteScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[] buffer, uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDescription(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[] buffer, uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalAlloc(uint flags, UIntPtr bytes);

    public sealed record PlanSetting(Guid Subgroup, Guid Setting, uint Ac, uint Dc);

    /// <summary>Lê as configurações embutidas (linhas "subgrupo configuração AC DC  # comentário").</summary>
    public static IReadOnlyList<PlanSetting> LoadSettings(TextReader reader)
    {
        var list = new List<PlanSetting>();
        while (reader.ReadLine() is { } line)
        {
            var data = line.Split('#')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (data.Length == 4 && Guid.TryParse(data[0], out var sub) && Guid.TryParse(data[1], out var setting) && uint.TryParse(data[2], out var ac) && uint.TryParse(data[3], out var dc))
                list.Add(new PlanSetting(sub, setting, ac, dc));
        }
        return list;
    }

    private static IReadOnlyList<PlanSetting> EmbeddedSettings()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/PQueirozOptimizer;component/Assets/Qrz.powerplan.txt"))
            ?? throw new InvalidOperationException("Configurações do plano Qrz não encontradas.");
        using var reader = new StreamReader(resource.Stream, Encoding.UTF8);
        return LoadSettings(reader);
    }

    public static Guid? ActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    public static bool IsQrzInstalled()
    {
        var guid = QrzGuid;
        uint size = 0;
        return PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, null, ref size) == 0 && size > 0;
    }

    public static bool IsQrzActive() => ActiveScheme() == QrzGuid;

    /// <summary>Cria (ou recria) o plano Qrz e o ativa. Retorna quantas configurações foram aplicadas.</summary>
    public int InstallAndActivate()
    {
        var previous = ActiveScheme();
        var qrz = QrzGuid;
        if (previous == qrz) { var balanced = Balanced; PowerSetActiveScheme(IntPtr.Zero, ref balanced); }
        // Recria do zero: reaplicar não herda mudanças feitas à mão no plano antigo
        if (IsQrzInstalled()) PowerDeleteScheme(IntPtr.Zero, ref qrz);

        var source = Balanced;
        var destination = LocalAlloc(0x40, (UIntPtr)16);
        Marshal.StructureToPtr(qrz, destination, false);
        try
        {
            var code = PowerDuplicateScheme(IntPtr.Zero, ref source, ref destination);
            if (code != 0) throw new InvalidOperationException($"Não foi possível criar o plano de energia (código {code}).");
        }
        finally { LocalFree(destination); }

        WriteText(PowerWriteFriendlyName, "Qrz");
        WriteText(PowerWriteDescription, "Plano de energia de baixa latência do PQueiroz Optimizer.");

        var applied = 0;
        foreach (var s in EmbeddedSettings())
        {
            var sub = s.Subgroup; var setting = s.Setting;
            // Configurações que este hardware não tem são recusadas pelo Windows; as demais seguem
            var ac = PowerWriteACValueIndex(IntPtr.Zero, ref qrz, ref sub, ref setting, s.Ac);
            var dc = PowerWriteDCValueIndex(IntPtr.Zero, ref qrz, ref sub, ref setting, s.Dc);
            if (ac == 0 || dc == 0) applied++;
        }
        var activate = PowerSetActiveScheme(IntPtr.Zero, ref qrz);
        if (activate != 0) throw new InvalidOperationException($"O plano Qrz foi criado, mas não pôde ser ativado (código {activate}).");

        // Guarda o plano anterior só na primeira ativação (reinstalar não troca o "antes" pelo próprio Qrz)
        if (previous is { } p && p != qrz && !File.Exists(StatePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(p));
        }
        _log.Write("SUCCESS", $"Plano de energia Qrz ativado ({applied} configurações)");
        return applied;
    }

    /// <summary>Volta ao plano que estava ativo antes do Qrz (ou ao Equilibrado) e remove o Qrz.</summary>
    public void Restore()
    {
        var target = Balanced;
        try { if (File.Exists(StatePath) && JsonSerializer.Deserialize<Guid>(File.ReadAllText(StatePath)) is var saved && saved != Guid.Empty && saved != QrzGuid) target = saved; }
        catch (JsonException) { }
        if (PowerSetActiveScheme(IntPtr.Zero, ref target) != 0) { target = Balanced; PowerSetActiveScheme(IntPtr.Zero, ref target); }
        var qrz = QrzGuid;
        if (IsQrzInstalled()) PowerDeleteScheme(IntPtr.Zero, ref qrz);
        if (File.Exists(StatePath)) File.Delete(StatePath);
        _log.Write("SUCCESS", "Plano de energia anterior restaurado e plano Qrz removido");
    }

    private delegate uint WriteTextApi(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[] buffer, uint size);

    private static void WriteText(WriteTextApi api, string text)
    {
        var guid = QrzGuid;
        var bytes = Encoding.Unicode.GetBytes(text + "\0");
        api(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, bytes, (uint)bytes.Length);
    }
}
