using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>Um valor do registro que um ajuste grava.</summary>
public sealed record RegistryWrite(RegistryHive Hive, string Key, string Name, RegistryValueKind Kind, object Value);

/// <summary>
/// Ajustes de registro com reversão em um clique: antes de gravar, guarda o valor original de cada item
/// (ou que ele não existia) num arquivo por ajuste em ProgramData. O aplicativo roda como administrador e o
/// arquivo poderia ser editado, então a reversão só restaura chaves que o próprio ajuste declara (<c>allowed</c>).
/// </summary>
public sealed class RegistryTweakStore
{
    private readonly string _directory;

    public RegistryTweakStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "Tweaks");
    }

    private sealed class Entry
    {
        public string Hive { get; set; } = "";
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Existed { get; set; }
        public bool KeyCreated { get; set; }
        public string? Kind { get; set; }
        public JsonElement? Data { get; set; }
    }

    private sealed class Backup
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public Dictionary<string, string> Meta { get; set; } = new();
        public DateTime AppliedAtUtc { get; set; }
        public List<Entry> Entries { get; set; } = new();
    }

    public sealed record AppliedTweak(string Id, string Title, IReadOnlyDictionary<string, string> Meta, DateTime AppliedAtUtc);

    private string PathFor(string id) => Path.Combine(_directory, Regex.Replace(id, @"[^A-Za-z0-9_.-]", "_") + ".json");

    public bool IsApplied(string id) => File.Exists(PathFor(id));

    public IReadOnlyList<AppliedTweak> List(string idPrefix)
    {
        if (!Directory.Exists(_directory)) return Array.Empty<AppliedTweak>();
        var result = new List<AppliedTweak>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(file));
                if (backup != null && backup.Id.StartsWith(idPrefix, StringComparison.Ordinal)) result.Add(new AppliedTweak(backup.Id, backup.Title, backup.Meta, backup.AppliedAtUtc));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return result.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Grava os valores. Se o ajuste já estava aplicado, o backup original é mantido (reaplicar não
    /// troca o "antes" por um valor já alterado).
    /// </summary>
    public void Apply(string id, string title, IReadOnlyList<RegistryWrite> writes, IReadOnlyDictionary<string, string>? meta = null)
    {
        var path = PathFor(id);
        var backup = File.Exists(path) ? JsonSerializer.Deserialize<Backup>(File.ReadAllText(path)) ?? new Backup() : new Backup();
        backup.Id = id; backup.Title = title; backup.AppliedAtUtc = DateTime.UtcNow;
        if (meta != null) backup.Meta = new Dictionary<string, string>(meta);
        foreach (var w in writes)
        {
            var hive = w.Hive.ToString();
            if (backup.Entries.Any(e => e.Hive == hive && e.Key.Equals(w.Key, StringComparison.OrdinalIgnoreCase) && e.Name.Equals(w.Name, StringComparison.OrdinalIgnoreCase))) continue;
            using var root = RegistryKey.OpenBaseKey(w.Hive, RegistryView.Registry64);
            using var existing = root.OpenSubKey(w.Key);
            var entry = new Entry { Hive = hive, Key = w.Key, Name = w.Name, KeyCreated = existing is null };
            if (existing?.GetValue(w.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is { } value)
            {
                var kind = existing.GetValueKind(w.Name);
                entry.Existed = true; entry.Kind = kind.ToString(); entry.Data = Serialize(kind, value);
            }
            backup.Entries.Add(entry);
        }
        // O backup vai para o disco antes de qualquer mudança: se algo falhar no meio, ainda dá para reverter
        Save(path, backup);
        foreach (var w in writes)
        {
            using var root = RegistryKey.OpenBaseKey(w.Hive, RegistryView.Registry64);
            using var key = root.CreateSubKey(w.Key, writable: true) ?? throw new InvalidOperationException("Não foi possível abrir " + w.Key);
            key.SetValue(w.Name, w.Value, w.Kind);
        }
    }

    /// <summary>Restaura o que havia antes. Itens fora de <paramref name="allowed"/> são ignorados.</summary>
    public int Revert(string id, Func<RegistryHive, string, string, bool> allowed)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return 0;
        var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(path)) ?? new Backup();
        var restored = 0;
        var failures = new List<string>();
        foreach (var e in backup.Entries)
        {
            if (!Enum.TryParse<RegistryHive>(e.Hive, out var hive) || hive is not (RegistryHive.LocalMachine or RegistryHive.CurrentUser) || !allowed(hive, e.Key, e.Name)) continue;
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                if (e.Existed && Enum.TryParse<RegistryValueKind>(e.Kind, out var kind) && e.Data is { } data)
                {
                    using var key = root.CreateSubKey(e.Key, writable: true);
                    key?.SetValue(e.Name, Deserialize(kind, data), kind);
                }
                else
                {
                    using (var key = root.OpenSubKey(e.Key, writable: true)) key?.DeleteValue(e.Name, throwOnMissingValue: false);
                    // A chave criada pelo ajuste sai junto, se ficou vazia
                    if (e.KeyCreated)
                    {
                        using var key = root.OpenSubKey(e.Key);
                        if (key is { ValueCount: 0, SubKeyCount: 0 }) { key.Dispose(); root.DeleteSubKey(e.Key, throwOnMissingSubKey: false); }
                    }
                }
                restored++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { failures.Add($"{e.Key}\\{e.Name}: {ex.Message}"); }
        }
        if (failures.Count > 0) throw new InvalidOperationException("Alguns valores não puderam ser restaurados: " + string.Join("; ", failures));
        File.Delete(path);
        return restored;
    }

    private void Save(string path, Backup backup)
    {
        if (!Directory.Exists(_directory))
        {
            Directory.CreateDirectory(_directory);
            ProtectDirectory(_directory);
        }
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Só administradores e o sistema alteram os backups; usuários comuns apenas leem.</summary>
    internal static void ProtectDirectory(string directory)
    {
        // Sem elevação, restringir a pasta aos administradores trancaria o próprio processo do lado de fora
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return;
        try
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
        }
        // Sem administrador (ex.: testes) a pasta continua funcionando com as permissões herdadas
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException) { }
    }

    private static JsonElement Serialize(RegistryValueKind kind, object value) => kind switch
    {
        RegistryValueKind.Binary => JsonSerializer.SerializeToElement(Convert.ToBase64String((byte[])value)),
        RegistryValueKind.MultiString => JsonSerializer.SerializeToElement((string[])value),
        RegistryValueKind.DWord => JsonSerializer.SerializeToElement(Convert.ToInt32(value)),
        RegistryValueKind.QWord => JsonSerializer.SerializeToElement(Convert.ToInt64(value)),
        _ => JsonSerializer.SerializeToElement(value.ToString()),
    };

    private static object Deserialize(RegistryValueKind kind, JsonElement data) => kind switch
    {
        RegistryValueKind.Binary => Convert.FromBase64String(data.GetString() ?? ""),
        RegistryValueKind.MultiString => data.Deserialize<string[]>() ?? Array.Empty<string>(),
        RegistryValueKind.DWord => data.GetInt32(),
        RegistryValueKind.QWord => data.GetInt64(),
        _ => data.GetString() ?? "",
    };
}
