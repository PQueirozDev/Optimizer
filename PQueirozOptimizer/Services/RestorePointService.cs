using System.IO;
using System.Text.Json;

namespace PQueirozOptimizer.Services;

/// <summary>Ponto de restauração do Windows, com as etiquetas e a cor que o app guarda localmente.</summary>
public sealed record RestorePoint(int Sequence, string Description, DateTime CreatedAt, string[] Tags, string Color);

/// <summary>
/// Pontos de restauração: lista, cria (com nome, descrição, etiquetas e cor) e restaura. O Windows só guarda a
/// descrição; etiquetas e cor ficam num arquivo do app ligado ao número do ponto.
/// </summary>
public sealed class RestorePointService
{
    private readonly ActivityLog _log;
    private static readonly string MetaPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "pontos-restauracao.json");

    public RestorePointService(ActivityLog log) => _log = log;

    private sealed class Meta { public string[] Tags { get; set; } = Array.Empty<string>(); public string Color { get; set; } = "Accent"; }

    private static Dictionary<int, Meta> LoadMeta()
    {
        try { return File.Exists(MetaPath) ? JsonSerializer.Deserialize<Dictionary<int, Meta>>(File.ReadAllText(MetaPath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }

    public static async Task<IReadOnlyList<RestorePoint>> ListAsync()
    {
        var json = await PowerShellBridge.RunScriptAsync(
            "@(Get-ComputerRestorePoint -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Seq = [int]$_.SequenceNumber; Desc = $_.Description; When = ([Management.ManagementDateTimeConverter]::ToDateTime($_.CreationTime)).ToString('o') } }) | ConvertTo-Json -Compress",
            timeout: TimeSpan.FromSeconds(60));
        var meta = LoadMeta();
        var list = new List<RestorePoint>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
        foreach (var e in items)
        {
            var seq = e.GetProperty("Seq").GetInt32();
            var m = meta.TryGetValue(seq, out var x) ? x : new Meta();
            list.Add(new RestorePoint(seq, e.GetProperty("Desc").GetString() ?? "", DateTime.TryParse(e.GetProperty("When").GetString(), out var d) ? d : DateTime.MinValue, m.Tags, m.Color));
        }
        return list.OrderByDescending(p => p.CreatedAt).ToList();
    }

    /// <summary>
    /// Cria o ponto. O Windows normalmente aceita só um por dia; o limite é suspenso durante a criação e
    /// volta ao valor anterior em seguida.
    /// </summary>
    public async Task CreateAsync(string name, string description, string[] tags, string color)
    {
        var text = string.IsNullOrWhiteSpace(description) ? name.Trim() : $"{name.Trim()} — {description.Trim()}";
        await PowerShellBridge.RunScriptAsync(
            "$k = 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore';" +
            "$old = (Get-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency;" +
            "Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" -ErrorAction SilentlyContinue;" +
            "Set-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord;" +
            "try { Checkpoint-Computer -Description $env:PQO_DESC -RestorePointType MODIFY_SETTINGS }" +
            "finally { if ($null -ne $old) { Set-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -Value $old -Type DWord } else { Remove-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue } }",
            new Dictionary<string, string> { ["PQO_DESC"] = text.Length > 250 ? text[..250] : text }, TimeSpan.FromMinutes(5));
        // O ponto recém-criado é o de maior número
        var created = (await ListAsync()).OrderByDescending(p => p.Sequence).FirstOrDefault();
        if (created != null && (tags.Length > 0 || color != "Accent"))
        {
            var meta = LoadMeta();
            meta[created.Sequence] = new Meta { Tags = tags, Color = color };
            Directory.CreateDirectory(Path.GetDirectoryName(MetaPath)!);
            File.WriteAllText(MetaPath, JsonSerializer.Serialize(meta));
        }
        _log.Write("SUCCESS", $"Ponto de restauração criado: {text}");
    }

    /// <summary>Restaura o sistema para o ponto. O Windows reinicia na hora para concluir.</summary>
    public async Task RestoreAsync(int sequence)
    {
        _log.Write("WARN", $"Restaurando o sistema para o ponto {sequence}; o PC vai reiniciar");
        await PowerShellBridge.RunScriptAsync("Restore-Computer -RestorePoint ([int]$env:PQO_SEQ) -Confirm:$false",
            new Dictionary<string, string> { ["PQO_SEQ"] = sequence.ToString() }, TimeSpan.FromMinutes(5));
    }
}
