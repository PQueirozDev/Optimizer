using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Uma leitura do monitor. Os campos opcionais ficam nulos quando o Windows (ou o driver) não os informa:
/// frequência real da CPU, número de processos, temperatura e memória da placa de vídeo.
/// </summary>
public sealed record HardwareSample(double Cpu, double Ram, double RamUsedGb, double RamTotalGb, double? Gpu, double DownBytesPerSec, double UpBytesPerSec, double? PingMs,
    double? CpuMhz = null, int? Processes = null, double? GpuTempC = null, double? VramUsedGb = null, double? VramTotalGb = null);

/// <summary>
/// Monitor de hardware em tempo real (uso de CPU, GPU, memória, rede e latência), lido direto das
/// APIs do Windows — sem WMI nem PowerShell, para custar quase nada mesmo atualizando a cada segundo.
/// É compartilhado pelo processo: a janela recriada na troca de idioma reaproveita o mesmo monitor.
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    public static HardwareMonitorService Shared { get; } = new();

    public const int HistoryLength = 60;
    private readonly object _gate = new();
    private readonly Queue<HardwareSample> _history = new();
    private event Action<HardwareSample>? SampledCore;
    private CancellationTokenSource? _loop;
    private int _subscribers;

    /// <summary>Recebe uma leitura por segundo (na thread do monitor). O monitor só roda enquanto houver alguém ouvindo.</summary>
    public event Action<HardwareSample> Sampled
    {
        add { lock (_gate) { SampledCore += value; if (++_subscribers == 1) Start(); } }
        remove { lock (_gate) { SampledCore -= value; if (--_subscribers <= 0) { _subscribers = 0; Stop(); } } }
    }

    public HardwareSample? Latest { get { lock (_gate) return _history.Count == 0 ? null : _history.Last(); } }
    public IReadOnlyList<HardwareSample> History { get { lock (_gate) return _history.ToList(); } }

    private void Start()
    {
        var cts = new CancellationTokenSource();
        _loop = cts;
        _ = Task.Run(() => RunAsync(cts.Token));
    }

    private void Stop() { _loop?.Cancel(); _loop = null; }

    private async Task RunAsync(CancellationToken token)
    {
        using var gpu = GpuUsageCounter.TryCreate();
        using var vram = GpuUsageCounter.TryCreate(@"\GPU Adapter Memory(*)\Dedicated Usage", max: true);
        // Frequência real como no Gerenciador de Tarefas: frequência nominal × % de desempenho (passa de 100% no turbo)
        using var frequency = BiosAdvisor.Detector.PdhCounter.TryCreate(@"\Processor Information(_Total)\Processor Frequency");
        using var performance = BiosAdvisor.Detector.PdhCounter.TryCreate(BiosAdvisor.Detector.PdhCounter.ProcessorPerformance);
        using var processes = BiosAdvisor.Detector.PdhCounter.TryCreate(@"\System\Processes");
        var vramTotal = NvmlReader.MemoryGb()?.TotalGb ?? DedicatedVideoMemoryGb();
        var cpu = new CpuUsage();
        var net = new NetworkThroughput();
        double? ping = null, gpuTemp = null, vramUsed = null;
        var tick = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                // A latência é medida a cada 5 s, sem segurar as outras leituras
                if (tick++ % 5 == 0) _ = Task.Run(async () => ping = await PingAsync("1.1.1.1"), token);
                var (ramPercent, used, total) = ReadMemory();
                var (down, up) = net.Read();
                var nominal = frequency?.Read(); var percent = performance?.Read();
                double? mhz = nominal is > 0 && percent is > 0 ? nominal * percent / 100 : null;
                var processCount = processes?.Read() is { } p ? (int?)Math.Round(p) : null;
                // Temperatura e memória da placa a cada 2 s (NVML; a memória cai para os contadores do Windows nas outras placas)
                if (tick % 2 == 1)
                {
                    gpuTemp = NvmlReader.TemperatureC();
                    vramUsed = NvmlReader.MemoryGb()?.UsedGb ?? (vram?.Read() is { } bytes ? bytes / (1024d * 1024 * 1024) : null);
                }
                var sample = new HardwareSample(cpu.Read(), ramPercent, used, total, gpu?.Read(), down, up, ping, mhz, processCount, gpuTemp, vramUsed, vramTotal);
                Action<HardwareSample>? handlers;
                lock (_gate)
                {
                    _history.Enqueue(sample);
                    while (_history.Count > HistoryLength) _history.Dequeue();
                    handlers = SampledCore;
                }
                handlers?.Invoke(sample);
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) { }
    }

    public static async Task<double?> PingAsync(string host, int timeoutMs = 1500)
    {
        try
        {
            using var p = new Ping();
            var reply = await p.SendPingAsync(host, timeoutMs);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException) { return null; }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Memória dedicada da placa de vídeo principal (a maior entre os adaptadores), informada pelo driver no registro.
    /// Vale para qualquer fabricante; null quando o driver não informa.
    /// </summary>
    public static double? DedicatedVideoMemoryGb()
    {
        try
        {
            using var classKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null) return null;
            double best = 0;
            foreach (var name in classKey.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var adapter = classKey.OpenSubKey(name);
                var value = adapter?.GetValue("HardwareInformation.qwMemorySize") ?? adapter?.GetValue("HardwareInformation.MemorySize");
                var bytes = value switch { long l => l, int i => (uint)i, byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0), byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0), _ => 0L };
                best = Math.Max(best, bytes / (1024d * 1024 * 1024));
            }
            return best >= 0.25 ? best : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return null; }
    }

    // ---------- Memória ----------
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static (double Percent, double UsedGb, double TotalGb) ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0) return (0, 0, 0);
        const double gb = 1024d * 1024 * 1024;
        var total = status.TotalPhys / gb;
        var used = (status.TotalPhys - status.AvailPhys) / gb;
        return (used / total * 100, used, total);
    }

    // ---------- CPU ----------
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    private sealed class CpuUsage
    {
        private long _idle, _total;
        public double Read()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
            var total = kernel + user; // o tempo de kernel já inclui o ocioso
            var dIdle = idle - _idle; var dTotal = total - _total;
            var first = _total == 0;
            _idle = idle; _total = total;
            return first || dTotal <= 0 ? 0 : Math.Clamp((1 - (double)dIdle / dTotal) * 100, 0, 100);
        }
    }

    // ---------- Rede ----------
    private sealed class NetworkThroughput
    {
        private long _received, _sent;
        private DateTime _at;
        public (double Down, double Up) Read()
        {
            long received = 0, sent = 0;
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    var stats = nic.GetIPStatistics();
                    received += stats.BytesReceived; sent += stats.BytesSent;
                }
            }
            catch (NetworkInformationException) { return (0, 0); }
            var now = DateTime.UtcNow;
            var seconds = (now - _at).TotalSeconds;
            var first = _at == default;
            var result = first || seconds <= 0 ? (0d, 0d) : (Math.Max(0, (received - _received) / seconds), Math.Max(0, (sent - _sent) / seconds));
            _received = received; _sent = sent; _at = now;
            return result;
        }
    }

    // ---------- GPU (contadores "GPU Engine" e "GPU Adapter Memory" do Windows 10 1709+, os mesmos do Gerenciador de Tarefas) ----------
    private sealed class GpuUsageCounter : IDisposable
    {
        private bool _max;
        private const uint PdhFmtDouble = 0x00000200, PdhFmtNoCap100 = 0x00008000;
        private const int PdhMoreData = unchecked((int)0x800007D2);
        private IntPtr _query, _counter;

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint count, IntPtr buffer);
        [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);

        [StructLayout(LayoutKind.Sequential)]
        private struct CounterValueItem { public IntPtr Name; public uint Status; public double Value; }

        /// <summary>Contador de várias instâncias: soma (uso do motor 3D por processo) ou maior valor (memória por adaptador).</summary>
        public static GpuUsageCounter? TryCreate(string path = @"\GPU Engine(*engtype_3D)\Utilization Percentage", bool max = false)
        {
            var c = new GpuUsageCounter { _max = max };
            if (PdhOpenQuery(null, IntPtr.Zero, out c._query) != 0) return null;
            if (PdhAddEnglishCounter(c._query, path, IntPtr.Zero, out c._counter) != 0) { c.Dispose(); return null; }
            PdhCollectQueryData(c._query);
            return c;
        }

        public double? Read()
        {
            if (PdhCollectQueryData(_query) != 0) return null;
            uint size = 0;
            var status = PdhGetFormattedCounterArray(_counter, PdhFmtDouble | PdhFmtNoCap100, ref size, out _, IntPtr.Zero);
            if (status != PdhMoreData || size == 0) return status == 0 ? 0 : null;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(_counter, PdhFmtDouble | PdhFmtNoCap100, ref size, out var count, buffer) != 0) return null;
                // Cada processo tem sua instância do motor 3D; o uso da placa é a soma (como no Gerenciador de Tarefas)
                double sum = 0, best = 0;
                var itemSize = Marshal.SizeOf<CounterValueItem>();
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterValueItem>(buffer + i * itemSize);
                    if (item.Status != 0) continue;
                    sum += item.Value; best = Math.Max(best, item.Value);
                }
                return _max ? best : Math.Clamp(sum, 0, 100);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Dispose() { if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; } }
    }
}
