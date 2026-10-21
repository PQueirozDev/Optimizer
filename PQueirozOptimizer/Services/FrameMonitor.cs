using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PQueirozOptimizer.Services;

/// <summary>Quadros de um processo nos últimos segundos.</summary>
public sealed record FrameStats(int ProcessId, string ProcessName, double Fps, double FrameTimeMs, double Low1Fps);

/// <summary>
/// Contador de FPS sem programas externos: uma sessão ETW em tempo real ouve o evento "Present" do DXGI
/// (Direct3D 10/11/12) e do Direct3D 9, o mesmo sinal que o PresentMon usa, e conta os quadros por processo.
/// Exige administrador (o app já roda assim). Jogos só em Vulkan/OpenGL que não passam pelo DXGI não aparecem.
/// </summary>
public sealed class FrameMonitor : IDisposable
{
    private const string SessionName = "Qrztweaks-Frames";
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const ushort DxgiPresentStart = 42, D3D9PresentStart = 1;
    private const double HistoryMs = 10_000;

    private readonly object _gate = new();
    // Por processo e por swap chain: um jogo com duas janelas (ou o launcher junto) não soma os quadros das duas
    private readonly Dictionary<(int Pid, ulong SwapChain), Queue<double>> _presents = new();
    private readonly Dictionary<(int Pid, ulong SwapChain), long> _lastSeen = new();
    private ulong _session, _trace = InvalidTrace;
    private IntPtr _properties, _loggerName;
    private IntPtr _logFile;
    private Thread? _thread;
    private EventRecordCallback? _callback; // mantém o delegate vivo enquanto o ETW usa o ponteiro

    public static FrameMonitor Shared { get; } = new();

    public bool Running => _thread is { IsAlive: true };
    public string? Error { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (Running) return;
            Error = null;
            try
            {
                StartSession();
                OpenAndProcess();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Error = ex.Message;
                FreeMemory(StopSession());
            }
        }
    }

    public void Stop()
    {
        Thread? thread;
        (IntPtr LogFile, IntPtr Name) memory;
        lock (_gate) { thread = _thread; _thread = null; memory = StopSession(); }
        // Fora do lock: o callback do ETW também usa o lock, e a thread precisa terminar antes de liberar a memória
        if (thread is null || thread.Join(3000)) FreeMemory(memory);
        lock (_gate) { _presents.Clear(); _lastSeen.Clear(); }
    }

    public void Dispose() => Stop();

    // ================= Sessão ETW =================
    private const int PropertiesSize = 120, NameBytes = 1024;
    private const uint WnodeFlagTracedGuid = 0x00020000, RealTimeMode = 0x00000100;
    private const uint ControlStop = 1, ErrorAlreadyExists = 183;
    private const ulong InvalidTrace = ulong.MaxValue;

    private IntPtr NewProperties()
    {
        var p = Marshal.AllocHGlobal(PropertiesSize + NameBytes);
        for (var i = 0; i < PropertiesSize + NameBytes; i++) Marshal.WriteByte(p, i, 0);
        Marshal.WriteInt32(p, 0, PropertiesSize + NameBytes); // Wnode.BufferSize
        Marshal.WriteInt32(p, 40, 1);                          // Wnode.ClientContext = QPC (alta resolução)
        Marshal.WriteInt32(p, 44, unchecked((int)WnodeFlagTracedGuid));
        Marshal.WriteInt32(p, 48, 64);                         // BufferSize (KB)
        Marshal.WriteInt32(p, 64, unchecked((int)RealTimeMode)); // LogFileMode
        Marshal.WriteInt32(p, 68, 1);                          // FlushTimer (s)
        Marshal.WriteInt32(p, 116, PropertiesSize);            // LoggerNameOffset
        return p;
    }

    private void StartSession()
    {
        _properties = NewProperties();
        var status = StartTraceW(out _session, SessionName, _properties);
        if (status == ErrorAlreadyExists)
        {
            // Sessão que sobrou de uma execução anterior (o app fechou sem parar): para e cria de novo
            ControlTraceW(0, SessionName, _properties, ControlStop);
            Marshal.FreeHGlobal(_properties);
            _properties = NewProperties();
            status = StartTraceW(out _session, SessionName, _properties);
        }
        if (status != 0) throw new System.ComponentModel.Win32Exception((int)status);
        var dxgi = DxgiProvider;
        var d3d9 = D3D9Provider;
        var a = EnableTraceEx2(_session, ref dxgi, 1, 4, ulong.MaxValue, 0, 0, IntPtr.Zero);
        EnableTraceEx2(_session, ref d3d9, 1, 4, ulong.MaxValue, 0, 0, IntPtr.Zero);
        if (a != 0) throw new System.ComponentModel.Win32Exception((int)a);
    }

    private void OpenAndProcess()
    {
        // EVENT_TRACE_LOGFILEW (448 bytes no x64): nome da sessão @8, modo @28, callback de EVENT_RECORD @424
        _logFile = Marshal.AllocHGlobal(448);
        for (var i = 0; i < 448; i++) Marshal.WriteByte(_logFile, i, 0);
        _loggerName = Marshal.StringToHGlobalUni(SessionName);
        Marshal.WriteIntPtr(_logFile, 8, _loggerName);
        Marshal.WriteInt32(_logFile, 28, 0x00000100 | 0x10000000); // PROCESS_TRACE_MODE_REAL_TIME | EVENT_RECORD
        _callback = OnEvent;
        Marshal.WriteIntPtr(_logFile, 424, Marshal.GetFunctionPointerForDelegate(_callback));
        _trace = OpenTraceW(_logFile);
        if (_trace == InvalidTrace) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var handle = _trace;
        _thread = new Thread(() =>
        {
            var status = ProcessTrace(new[] { handle }, 1, IntPtr.Zero, IntPtr.Zero);
            if (status is not (0 or 1223)) Error = new System.ComponentModel.Win32Exception((int)status).Message; // 1223 = cancelada
        }) { IsBackground = true, Name = "ETW frames" };
        _thread.Start();
    }

    /// <summary>Para a sessão e fecha o consumidor; devolve a memória que só pode ser liberada depois da thread terminar.</summary>
    private (IntPtr LogFile, IntPtr Name) StopSession()
    {
        if (_properties != IntPtr.Zero)
        {
            ControlTraceW(_session, null, _properties, ControlStop);
            Marshal.FreeHGlobal(_properties);
            _properties = IntPtr.Zero;
        }
        if (_trace != InvalidTrace) { CloseTrace(_trace); _trace = InvalidTrace; }
        var memory = (_logFile, _loggerName);
        _logFile = _loggerName = IntPtr.Zero;
        return memory;
    }

    private static void FreeMemory((IntPtr LogFile, IntPtr Name) memory)
    {
        if (memory.LogFile != IntPtr.Zero) Marshal.FreeHGlobal(memory.LogFile);
        if (memory.Name != IntPtr.Zero) Marshal.FreeHGlobal(memory.Name);
    }

    // EVENT_RECORD: Flags @4, ProcessId @12, TimeStamp (FILETIME, 100 ns) @16, ProviderId @24, Id @40, UserDataLength @86, UserData @96
    private const ushort Header32Bit = 0x0020;
    private const uint DxgiPresentTest = 0x00000001;
    private const double EvictAfterMs = 10_000;

    private void OnEvent(IntPtr record)
    {
        var id = (ushort)Marshal.ReadInt16(record, 40);
        if (id != DxgiPresentStart && id != D3D9PresentStart) return;
        var provider = Marshal.PtrToStructure<Guid>(record + 24);
        if (!(id == DxgiPresentStart && provider == DxgiProvider) && !(id == D3D9PresentStart && provider == D3D9Provider)) return;
        // Dados do evento: ponteiro da swap chain (4 ou 8 bytes, conforme o processo) e as flags do Present
        var pointerSize = ((ushort)Marshal.ReadInt16(record, 4) & Header32Bit) != 0 ? 4 : 8;
        var length = (ushort)Marshal.ReadInt16(record, 86);
        var data = Marshal.ReadIntPtr(record, 96);
        ulong swapChain = 0;
        if (data != IntPtr.Zero && length >= pointerSize + 4)
        {
            swapChain = pointerSize == 8 ? (ulong)Marshal.ReadInt64(data) : (uint)Marshal.ReadInt32(data);
            // DXGI_PRESENT_TEST só pergunta se daria para apresentar: não é um quadro (o PresentMon também descarta)
            if (id == DxgiPresentStart && ((uint)Marshal.ReadInt32(data, pointerSize) & DxgiPresentTest) != 0) return;
        }
        var key = (Marshal.ReadInt32(record, 12), swapChain);
        var ms = Marshal.ReadInt64(record, 16) / 10_000.0;
        lock (_gate)
        {
            if (!_presents.TryGetValue(key, out var queue)) _presents[key] = queue = new Queue<double>();
            queue.Enqueue(ms);
            while (queue.Count > 0 && queue.Peek() < ms - HistoryMs) queue.Dequeue();
            _lastSeen[key] = Stopwatch.GetTimestamp();
        }
    }

    // ================= Estatísticas =================
    /// <summary>FPS do último segundo, tempo médio de quadro e 1% low dos últimos 10 s. Null sem quadros recentes.</summary>
    public FrameStats? GetStats(int pid)
    {
        double[]? times = null;
        lock (_gate)
        {
            // Processos que pararam de apresentar (fechados) saem da memória
            foreach (var stale in _lastSeen.Where(e => Stopwatch.GetElapsedTime(e.Value).TotalMilliseconds > EvictAfterMs).Select(e => e.Key).ToList())
            {
                _lastSeen.Remove(stale);
                _presents.Remove(stale);
            }
            // A swap chain principal do processo: a que apresentou nos últimos 2 s com mais quadros
            var best = _presents.Where(e => e.Key.Pid == pid && e.Value.Count >= 3 && Stopwatch.GetElapsedTime(_lastSeen[e.Key]).TotalSeconds <= 2)
                .OrderByDescending(e => e.Value.Count).Select(e => e.Value).FirstOrDefault();
            if (best is null) return null;
            times = best.ToArray();
        }
        return Compute(pid, ProcessName(pid), times);
    }

    /// <summary>Cálculo puro (testável): tempos de apresentação em ms, em ordem.</summary>
    public static FrameStats? Compute(int pid, string name, IReadOnlyList<double> times)
    {
        if (times.Count < 3) return null;
        var last = times[^1];
        var start = 0;
        while (start < times.Count - 1 && times[start] < last - 1000) start++;
        var span = last - times[start];
        var fps = span > 0 ? (times.Count - 1 - start) * 1000.0 / span : 0;
        var deltas = new double[times.Count - 1];
        for (var i = 1; i < times.Count; i++) deltas[i - 1] = times[i] - times[i - 1];
        Array.Sort(deltas);
        var p99 = deltas[(int)Math.Min(deltas.Length - 1, Math.Floor(deltas.Length * 0.99))];
        return new FrameStats(pid, name, fps, fps > 0 ? 1000 / fps : 0, p99 > 0 ? 1000 / p99 : 0);
    }

    /// <summary>Quadros do programa em primeiro plano (o jogo que está na tela), ignorando o próprio app.</summary>
    public FrameStats? GetForegroundStats()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var pid);
        return pid != 0 && pid != Environment.ProcessId ? GetStats((int)pid) : null;
    }

    private static string ProcessName(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return pid.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void EventRecordCallback(IntPtr record);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint StartTraceW(out ulong handle, string name, IntPtr properties);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ControlTraceW(ulong handle, string? name, IntPtr properties, uint control);
    [DllImport("advapi32.dll")]
    private static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level, ulong matchAny, ulong matchAll, uint timeout, IntPtr parameters);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern ulong OpenTraceW(IntPtr logFile);
    [DllImport("advapi32.dll")]
    private static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
    [DllImport("advapi32.dll")]
    private static extern uint CloseTrace(ulong handle);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
}
