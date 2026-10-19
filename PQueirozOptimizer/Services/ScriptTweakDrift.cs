using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>Valor do backup das otimizações cujo registro não está mais como a otimização deixou.</summary>
public sealed record DriftedValue(string Step, string Path, string Name);

/// <summary>
/// Otimizações do script (Padrão, Avançada, Debloat, Inteligente): no fim de cada uma o script guarda o valor
/// que cada chave ficou (<c>ValorAplicado</c>). Aqui o app só compara com o registro atual; reaplicar é feito pelo
/// próprio script ("reaplicar"), que confere a mesma lista permitida do Reverter.
/// </summary>
public static class ScriptTweakDrift
{
    public static IReadOnlyList<DriftedValue> Find(string snapshotPath)
    {
        if (!File.Exists(snapshotPath)) return Array.Empty<DriftedValue>();
        using var json = JsonDocument.Parse(File.ReadAllText(snapshotPath));
        if (!json.RootElement.TryGetProperty("Itens", out var items) || items.ValueKind != JsonValueKind.Array) return Array.Empty<DriftedValue>();
        var result = new List<DriftedValue>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Text(item, "Tipo") != "Registro" || !item.TryGetProperty("AplicadoExiste", out var exists)
                || exists.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) continue;
            if (Text(item, "Caminho") is not { } path || Text(item, "Nome") is not { } name || Open(path) is not { } location) continue;
            if (!Matches(location.Hive, location.SubKey, name, exists.GetBoolean(), item))
                result.Add(new DriftedValue(Text(item, "Etapa") ?? "", path, name));
        }
        return result;
    }

    private static string? Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>"HKLM:\SOFTWARE\..." do PowerShell para a raiz e a subchave do registro.</summary>
    private static (RegistryHive Hive, string SubKey)? Open(string path)
    {
        var parts = path.Split(":\\", 2);
        if (parts.Length != 2) return null;
        return parts[0].ToUpperInvariant() switch
        {
            "HKLM" => (RegistryHive.LocalMachine, parts[1]),
            "HKCU" => (RegistryHive.CurrentUser, parts[1]),
            _ => null,
        };
    }

    private static bool Matches(RegistryHive hive, string subKey, string name, bool shouldExist, JsonElement item)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(subKey);
            var current = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (!shouldExist) return current is null;
            if (current is null || !item.TryGetProperty("ValorAplicado", out var applied)) return false;
            return key!.GetValueKind(name) switch
            {
                // O PowerShell grava DWORD como Int32 (0xFFFFFFFF vira -1); comparar os 32 bits evita falso alarme
                RegistryValueKind.DWord => applied.ValueKind == JsonValueKind.Number && applied.TryGetInt64(out var d) && unchecked((int)d) == Convert.ToInt32(current),
                RegistryValueKind.QWord => applied.ValueKind == JsonValueKind.Number && applied.TryGetInt64(out var q) && q == Convert.ToInt64(current),
                RegistryValueKind.String or RegistryValueKind.ExpandString => applied.ValueKind == JsonValueKind.String && applied.GetString() == (string)current,
                RegistryValueKind.MultiString => applied.ValueKind == JsonValueKind.Array && applied.EnumerateArray().Select(e => e.GetString()).SequenceEqual((string[])current),
                RegistryValueKind.Binary => applied.ValueKind == JsonValueKind.Array && applied.EnumerateArray().Select(e => e.TryGetInt32(out var b) ? b : -1).SequenceEqual(((byte[])current).Select(b => (int)b)),
                // Tipo que o app não compara: sem aviso
                _ => true,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidCastException) { return true; }
    }
}
