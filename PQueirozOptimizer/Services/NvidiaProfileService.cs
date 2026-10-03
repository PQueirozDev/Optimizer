using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PQueirozOptimizer.Services;

/// <summary>Configuração do Painel de Controle NVIDIA que o perfil de jogo altera.</summary>
public sealed record NvidiaSetting(uint Id, string Title, string Description, uint Value, string ValueLabel, Func<uint?, string> Describe);

/// <summary>
/// Perfil NVIDIA para jogos gravado direto nas configurações globais do driver (NvAPI DRS, a mesma base do
/// Painel de Controle NVIDIA e do Profile Inspector) — sem baixar ferramentas de terceiros. Antes de mudar,
/// guarda o valor anterior de cada item; restaurar volta exatamente ao que estava.
/// </summary>
public sealed class NvidiaProfileService
{
    private readonly ActivityLog _log;
    private static readonly string BackupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "Tweaks", "nvidia-perfil.json");

    public NvidiaProfileService(ActivityLog log) => _log = log;

    // ---------- Configurações (IDs do NvApiDriverSettings.h) ----------
    public static readonly NvidiaSetting[] Settings =
    {
        new(0x1057EB71, "Modo de gerenciamento de energia", "A placa não reduz o clock no meio do jogo.", 1, "Preferir desempenho máximo",
            v => v switch { 0 => "Normal (adaptável)", 1 => "Preferir desempenho máximo", 5 => "Ideal", null => "Padrão do driver", _ => $"Valor {v}" }),
        new(0x007BA09E, "Modo de baixa latência", "Menos quadros na fila entre a CPU e a GPU: resposta mais rápida aos comandos.", 1, "Ligado",
            v => v switch { 0 => "Desligado", 1 => "Ligado", null => "Padrão do driver", _ => $"{v} quadros" }),
        new(0x00CE2691, "Filtragem de textura – qualidade", "Prioriza o desempenho na filtragem de texturas, com diferença visual mínima.", 20, "Alto desempenho",
            v => v switch { 0xFFFFFFF6 => "Alta qualidade", 0 => "Qualidade", 10 => "Desempenho", 20 => "Alto desempenho", null => "Padrão do driver", _ => $"Valor {v}" }),
        new(0x20C1221E, "Otimização segmentada", "Distribui o trabalho do driver em vários núcleos da CPU.", 1, "Ligada",
            v => v switch { 0 => "Automático", 1 => "Ligada", 2 => "Desligada", null => "Padrão do driver", _ => $"Valor {v}" }),
        new(0x00A879CF, "Sincronização vertical", "Desligada no driver: o jogo decide (use o V-Sync do jogo ou G-Sync se preferir).", 0x08416747, "Forçar desligada",
            v => v switch { 0x60925292 => "Usar configuração do aplicativo", 0x08416747 => "Forçar desligada", 0x47814940 => "Forçar ligada", 0x32610244 => "Rápida", null => "Padrão do driver", _ => $"Valor 0x{v:X8}" }),
        new(0x00AC8497, "Tamanho do cache de sombreador", "Cache ilimitado: menos travadas ao compilar shaders em jogos novos.", 0xFFFFFFFF, "Ilimitado",
            v => v switch { 0xFFFFFFFF => "Ilimitado", 0 => "Desativado", null => "Padrão do driver", _ => $"{v} MB" }),
    };

    // ---------- NvAPI ----------
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitializeFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateSessionFn(out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SessionFn(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetProfileFn(IntPtr session, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSettingFn(IntPtr session, IntPtr profile, uint settingId, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSettingFn(IntPtr session, IntPtr profile, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteSettingFn(IntPtr session, IntPtr profile, uint settingId);

    private const int SettingNotFound = -160;
    // NVDRS_SETTING_V1: versão, nome (2048 caracteres), id, tipo, local, 2 flags e duas uniões de 4100 bytes
    private const int SettingSize = 12320, IdOffset = 4100, TypeOffset = 4104, CurrentValueOffset = 8220;
    private const uint SettingVersion = SettingSize | (1u << 16);

    private static T Fn<T>(uint id) where T : Delegate
    {
        var pointer = QueryInterface(id);
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Função da NvAPI indisponível neste driver.");
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    /// <summary>Abre uma sessão de configurações do driver, executa a ação no perfil global e fecha.</summary>
    private static T WithGlobalProfile<T>(Func<IntPtr, IntPtr, T> action, bool save)
    {
        Check(Fn<InitializeFn>(0x0150E828)(), "inicializar a NvAPI");
        Check(Fn<CreateSessionFn>(0x0694D52E)(out var session), "abrir a sessão de configurações");
        try
        {
            Check(Fn<SessionFn>(0x375DBD6B)(session), "carregar as configurações");
            Check(Fn<GetProfileFn>(0x617BFF9F)(session, out var profile), "ler o perfil global");
            var result = action(session, profile);
            if (save) Check(Fn<SessionFn>(0xFCBC7E14)(session), "salvar as configurações (execute como administrador)");
            return result;
        }
        finally { Fn<SessionFn>(0xDAD9CFF8)(session); }
    }

    private static void Check(int status, string what)
    {
        if (status != 0) throw new InvalidOperationException($"NVIDIA: não foi possível {what} (código {status}).");
    }

    private static uint? ReadDword(IntPtr session, IntPtr profile, uint id)
    {
        var buffer = Marshal.AllocHGlobal(SettingSize);
        try
        {
            Marshal.Copy(new byte[SettingSize], 0, buffer, SettingSize);
            Marshal.WriteInt32(buffer, 0, unchecked((int)SettingVersion));
            var status = Fn<GetSettingFn>(0x73BF8338)(session, profile, id, buffer);
            if (status == SettingNotFound) return null;
            Check(status, $"ler a configuração 0x{id:X8}");
            return unchecked((uint)Marshal.ReadInt32(buffer, CurrentValueOffset));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void WriteDword(IntPtr session, IntPtr profile, uint id, uint value)
    {
        var buffer = Marshal.AllocHGlobal(SettingSize);
        try
        {
            Marshal.Copy(new byte[SettingSize], 0, buffer, SettingSize);
            Marshal.WriteInt32(buffer, 0, unchecked((int)SettingVersion));
            Marshal.WriteInt32(buffer, IdOffset, unchecked((int)id));
            Marshal.WriteInt32(buffer, TypeOffset, 0); // NVDRS_DWORD_TYPE
            Marshal.WriteInt32(buffer, CurrentValueOffset, unchecked((int)value));
            Check(Fn<SetSettingFn>(0x577DD202)(session, profile, buffer), $"gravar a configuração 0x{id:X8}");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // ---------- API pública ----------
    public static bool IsAvailable()
    {
        try { return QueryInterface(0x0150E828) != IntPtr.Zero && Fn<InitializeFn>(0x0150E828)() == 0; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return false; }
    }

    /// <summary>Valor atual de cada configuração no perfil global (nulo = padrão do driver).</summary>
    public static Dictionary<uint, uint?> ReadCurrent() =>
        WithGlobalProfile((session, profile) => Settings.ToDictionary(s => s.Id, s => ReadDword(session, profile, s.Id)), save: false);

    public static bool IsApplied() => File.Exists(BackupPath);

    public void Apply(IReadOnlyCollection<uint> ids)
    {
        WithGlobalProfile((session, profile) =>
        {
            // O "antes" é guardado na primeira aplicação e mantido nas seguintes
            var backup = LoadBackup();
            foreach (var s in Settings.Where(s => ids.Contains(s.Id)))
                if (!backup.ContainsKey(s.Id)) backup[s.Id] = ReadDword(session, profile, s.Id);
            SaveBackup(backup);
            foreach (var s in Settings.Where(s => ids.Contains(s.Id))) WriteDword(session, profile, s.Id, s.Value);
            return 0;
        }, save: true);
        _log.Write("SUCCESS", $"Perfil NVIDIA para jogos aplicado ({ids.Count} configurações)");
    }

    public void Restore()
    {
        var backup = LoadBackup();
        if (backup.Count == 0) return;
        var allowed = Settings.Select(s => s.Id).ToHashSet();
        WithGlobalProfile((session, profile) =>
        {
            foreach (var (id, value) in backup.Where(b => allowed.Contains(b.Key)))
            {
                if (value is { } v) WriteDword(session, profile, id, v);
                else
                {
                    var status = Fn<DeleteSettingFn>(0xE4A26362)(session, profile, id);
                    if (status != 0 && status != SettingNotFound) Check(status, $"restaurar a configuração 0x{id:X8}");
                }
            }
            return 0;
        }, save: true);
        File.Delete(BackupPath);
        _log.Write("SUCCESS", "Configurações NVIDIA anteriores restauradas");
    }

    private static Dictionary<uint, uint?> LoadBackup()
    {
        try { return File.Exists(BackupPath) ? JsonSerializer.Deserialize<Dictionary<uint, uint?>>(File.ReadAllText(BackupPath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }

    private static void SaveBackup(Dictionary<uint, uint?> backup)
    {
        var dir = Path.GetDirectoryName(BackupPath)!;
        if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); RegistryTweakStore.ProtectDirectory(dir); }
        File.WriteAllText(BackupPath, JsonSerializer.Serialize(backup));
    }
}
