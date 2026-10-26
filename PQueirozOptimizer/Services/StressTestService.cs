using System.Management;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Teste de estresse de CPU e memória. A CPU roda contas de ponto flutuante em todos os núcleos; a memória é
/// preenchida com um padrão que muda a cada passada e conferida palavra por palavra — qualquer diferença é um erro
/// real de memória (RAM instável, XMP/EXPO agressivo, overclock ou undervolt). As threads rodam abaixo do normal,
/// então o PC continua respondendo.
/// </summary>
public sealed class StressTestService : IDisposable
{
    private const int ChunkLongs = 16 * 1024 * 1024; // 128 MB por bloco
    private CancellationTokenSource? _cts;
    private readonly List<Thread> _threads = new();
    private long _memErrors, _memPasses, _cpuOps;

    public bool Running => _cts != null;
    public long MemoryErrors => Interlocked.Read(ref _memErrors);
    public long MemoryPasses => Interlocked.Read(ref _memPasses);
    /// <summary>Blocos de trabalho da CPU concluídos (para mostrar que o teste está andando).</summary>
    public long CpuOperations => Interlocked.Read(ref _cpuOps);
    public long MemoryAllocatedBytes { get; private set; }
    /// <summary>O teste de memória foi pedido mas nenhum bloco pôde ser alocado (memória livre insuficiente).</summary>
    public bool MemoryFailed { get; private set; }
    public int CpuThreads { get; private set; }

    public void Start(bool cpu, bool ram, double ramFraction = 0.6)
    {
        if (Running || (!cpu && !ram)) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Interlocked.Exchange(ref _memErrors, 0);
        Interlocked.Exchange(ref _memPasses, 0);
        Interlocked.Exchange(ref _cpuOps, 0);
        MemoryAllocatedBytes = 0;
        MemoryFailed = false;
        // Com os dois testes, um núcleo fica para a memória
        var threads = Environment.ProcessorCount;
        if (cpu && ram && threads > 1) threads--;
        CpuThreads = cpu ? threads : 0;
        if (cpu)
            for (var i = 0; i < threads; i++)
            {
                var seed = i;
                StartThread(() => CpuWorker(token, seed), $"Stress CPU {seed}");
            }
        if (ram)
        {
            var bytes = (long)(FreePhysicalMemory() * Math.Clamp(ramFraction, 0.1, 0.85));
            StartThread(() => RamWorker(token, bytes), "Stress RAM");
        }
    }

    public void Stop()
    {
        if (_cts is null) return;
        _cts.Cancel();
        foreach (var t in _threads) t.Join(3000);
        _threads.Clear();
        _cts.Dispose();
        _cts = null;
        // Devolve na hora os GB alocados pelo teste de memória
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public void Dispose() => Stop();

    private void StartThread(ThreadStart body, string name)
    {
        var t = new Thread(body) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
        _threads.Add(t);
        t.Start();
    }

    private void CpuWorker(CancellationToken token, int seed)
    {
        var data = new double[4096];
        for (var i = 0; i < data.Length; i++) data[i] = i + seed + 1;
        var x64 = (ulong)(seed + 1) * 0x9E3779B97F4A7C15UL;
        while (!token.IsCancellationRequested)
        {
            for (var pass = 0; pass < 64; pass++)
                for (var k = 0; k < data.Length; k++)
                {
                    var x = Math.FusedMultiplyAdd(data[k], 1.0000001, 0.5);
                    x = Math.Sqrt(x * x + 1) + Math.Sin(x) * 0.001;
                    data[k] = x > 1e12 ? k + 1 : x;
                    x64 ^= x64 << 13; x64 ^= x64 >> 7; x64 ^= x64 << 17;
                }
            if (x64 == 0) data[0] = 0; // impede o compilador de descartar o laço
            Interlocked.Increment(ref _cpuOps);
        }
    }

    private void RamWorker(CancellationToken token, long totalBytes)
    {
        var blocks = new List<long[]>();
        try
        {
            long allocated = 0;
            try
            {
                while (allocated + ChunkLongs * 8L <= totalBytes && !token.IsCancellationRequested)
                {
                    blocks.Add(new long[ChunkLongs]);
                    allocated += ChunkLongs * 8L;
                }
            }
            catch (OutOfMemoryException) { }
            MemoryAllocatedBytes = allocated;
            if (blocks.Count == 0) { MemoryFailed = true; return; }
            ulong pass = 0;
            while (!token.IsCancellationRequested)
            {
                pass++;
                var pattern = (long)(pass * 0x9E3779B97F4A7C15UL);
                foreach (var block in blocks)
                {
                    for (var i = 0; i < block.Length; i++) block[i] = pattern ^ i;
                    if (token.IsCancellationRequested) return;
                }
                foreach (var block in blocks)
                {
                    long errors = 0;
                    for (var i = 0; i < block.Length; i++) if (block[i] != (pattern ^ i)) errors++;
                    if (errors > 0) Interlocked.Add(ref _memErrors, errors);
                    if (token.IsCancellationRequested) return;
                }
                Interlocked.Increment(ref _memPasses);
            }
        }
        finally { blocks.Clear(); }
    }

    // API nativa em vez de WMI: Start roda na thread da interface e uma consulta WMI lenta travava o botão
    private static long FreePhysicalMemory() => HardwareMonitorService.AvailablePhysicalBytes() ?? 1L << 30;
}
