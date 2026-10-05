using System.Management;

namespace PQueirozOptimizer.BiosAdvisor.Detector;

/// <summary>Uma linha de resultado do WMI/CIM (propriedade → valor).</summary>
public sealed class WmiRow
{
    private readonly IReadOnlyDictionary<string, object?> _values;
    public WmiRow(IReadOnlyDictionary<string, object?> values) => _values = values;

    public string Text(string name) => _values.TryGetValue(name, out var v) && v != null ? (v.ToString() ?? "").Trim() : "";
    public long? Long(string name) => _values.TryGetValue(name, out var v) && v != null && long.TryParse(v.ToString(), out var n) ? n : null;
    public int? Int(string name) => Long(name) is { } n && n is >= int.MinValue and <= int.MaxValue ? (int)n : null;
    public bool? Bool(string name) => _values.TryGetValue(name, out var v) && v is bool b ? b : v is not null && bool.TryParse(v.ToString(), out var p) ? p : null;
    public object? Raw(string name) => _values.TryGetValue(name, out var v) ? v : null;
}

/// <summary>Fonte de consultas WMI. A implementação real usa System.Management; os testes usam linhas montadas à mão.</summary>
public interface IWmiSource
{
    /// <summary>Executa a consulta; devolve lista vazia se a classe não existir ou o acesso falhar.</summary>
    IReadOnlyList<WmiRow> Query(string className, string[] properties, string scope = @"root\cimv2");
}

/// <summary>WMI de verdade, com tempo limite por consulta. Só consultas fixas (sem texto vindo do usuário).</summary>
public sealed class SystemWmiSource : IWmiSource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public IReadOnlyList<WmiRow> Query(string className, string[] properties, string scope = @"root\cimv2")
    {
        var rows = new List<WmiRow>();
        try
        {
            var options = new EnumerationOptions { Timeout = Timeout, ReturnImmediately = true, Rewindable = false };
            var query = new SelectQuery(className, null, properties);
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), query, options);
            using var results = searcher.Get();
            foreach (var item in results)
            {
                using (item)
                {
                    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in properties)
                    {
                        try { values[p] = item[p]; }
                        catch (ManagementException) { values[p] = null; }
                    }
                    rows.Add(new WmiRow(values));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or TimeoutException) { }
        return rows;
    }
}
