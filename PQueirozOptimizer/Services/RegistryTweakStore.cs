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
        public string? TargetKind { get; set; }
        public JsonElement? TargetData { get; set; }
    }

    private sealed class Backup
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public Dictionary<string, string> Meta { get; set; } = new();
        public DateTime AppliedAtUtc { get; set; }
        /// <summary>null = backup de versões anteriores (sem estado gravado): vale como aplicado, como antes.</summary>
        public string? Status { get; set; }
        public List<Entry> Entries { get; set; } = new();
    }

    public sealed record AppliedTweak(string Id, string Title, IReadOnlyDictionary<string, string> Meta, DateTime AppliedAtUtc);

    private string PathFor(string id) => Path.Combine(_directory, Regex.Replace(id, @"[^A-Za-z0-9_.-]", "_") + ".json");

    public bool IsApplied(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return false;
        try
        {
            var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(path));
            if (backup is null) return false;
            return backup.Status is null || (backup.Status == "Applied" && backup.Entries.All(IsCurrent));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }

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
        backup.Id = id; backup.Title = title; backup.AppliedAtUtc = DateTime.UtcNow; backup.Status = "Pending";
        if (meta != null) backup.Meta = new Dictionary<string, string>(meta);
        foreach (var w in writes)
        {
            var hive = w.Hive.ToString();
            var existingEntry = backup.Entries.FirstOrDefault(e => e.Hive == hive && e.Key.Equals(w.Key, StringComparison.OrdinalIgnoreCase) && e.Name.Equals(w.Name, StringComparison.OrdinalIgnoreCase));
            if (existingEntry != null)
            {
                existingEntry.TargetKind = w.Kind.ToString();
                existingEntry.TargetData = Serialize(w.Kind, w.Value);
                continue;
            }
            using var root = RegistryKey.OpenBaseKey(w.Hive, RegistryView.Registry64);
            using var existing = root.OpenSubKey(w.Key);
            var entry = new Entry { Hive = hive, Key = w.Key, Name = w.Name, KeyCreated = existing is null };
            if (existing?.GetValue(w.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is { } value)
            {
                var kind = existing.GetValueKind(w.Name);
                entry.Existed = true; entry.Kind = kind.ToString(); entry.Data = Serialize(kind, value);
            }
            entry.TargetKind = w.Kind.ToString();
            entry.TargetData = Serialize(w.Kind, w.Value);
            backup.Entries.Add(entry);
        }
        // O backup vai para o disco antes de qualquer mudança: se algo falhar no meio, ainda dá para reverter
        Save(path, backup);
        try
        {
            foreach (var w in writes)
            {
                using var root = RegistryKey.OpenBaseKey(w.Hive, RegistryView.Registry64);
                using var key = root.CreateSubKey(w.Key, writable: true) ?? throw new InvalidOperationException("Não foi possível abrir " + w.Key);
                key.SetValue(w.Name, w.Value, w.Kind);
            }
            backup.Status = backup.Entries.All(IsCurrent) ? "Applied" : "Partial";
            Save(path, backup);
            if (backup.Status != "Applied") throw new InvalidOperationException("O ajuste foi aplicado apenas parcialmente; o backup foi mantido para reversão.");
        }
        catch
        {
            backup.Status = "Partial";
            Save(path, backup);
            throw;
        }
    }

    private bool IsCurrent(Entry entry)
    {
        if (!Enum.TryParse<RegistryHive>(entry.Hive, out var hive) || !Enum.TryParse<RegistryValueKind>(entry.TargetKind, out var kind) || entry.TargetData is not { } data) return false;
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(entry.Key);
        if (key?.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } value) return false;
        try { return key.GetValueKind(entry.Name) == kind && Serialize(kind, value).GetRawText() == data.GetRawText(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
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
