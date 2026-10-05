using System.Runtime.InteropServices;

namespace PQueirozOptimizer.BiosAdvisor.Detector;

/// <summary>
/// Um contador de desempenho do Windows pelo nome em inglês (vale em qualquer idioma do sistema).
/// Contadores de taxa precisam de duas coletas: a primeira leitura volta null.
/// </summary>
public sealed class PdhCounter : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200, PdhFmtNoCap100 = 0x00008000;
    private IntPtr _query, _counter;
    private bool _primed;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);
    [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFmtCounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }

    private PdhCounter() { }

    public static PdhCounter? TryCreate(string englishPath)
    {
        var c = new PdhCounter();
        if (PdhOpenQuery(null, IntPtr.Zero, out c._query) != 0) return null;
        if (PdhAddEnglishCounter(c._query, englishPath, IntPtr.Zero, out c._counter) != 0) { c.Dispose(); return null; }
        return c;
    }

    public double? Read()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0) return null;
        if (!_primed) { _primed = true; return null; }
        if (PdhGetFormattedCounterValue(_counter, PdhFmtDouble | PdhFmtNoCap100, out _, out var value) != 0 || value.Status != 0) return null;
        return value.Value;
    }

    public void Dispose() { if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; } }

    public const string ProcessorPerformance = @"\Processor Information(_Total)\% Processor Performance";
    public const string PerformanceLimit = @"\Processor Information(_Total)\% Performance Limit";
}
