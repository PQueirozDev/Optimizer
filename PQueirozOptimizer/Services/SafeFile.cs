using System.IO;
using System.Text.Json;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Gravação e leitura dos backups de "antes" das alterações. Gravar direto no arquivo final deixava um JSON
/// cortado se o app fechasse no meio, e ler um backup ilegível como vazio fazia a próxima aplicação guardar o
/// estado já otimizado como original, perdendo para sempre o valor do usuário.
/// </summary>
internal static class SafeFile
{
    /// <summary>Grava ao lado e troca: uma queda no meio não apaga o arquivo que já existia.</summary>
    public static void WriteAllText(string path, string text)
    {
        var temp = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Backup ausente = vazio. Backup que existe mas não pode ser lido = erro: nada deve ser alterado.</summary>
    public static T ReadBackup<T>(string path, string what) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"O backup {what} ({path}) não pôde ser lido. Nada foi alterado, para não perder os valores originais.", ex);
        }
    }
}
