using System.IO;
using System.Runtime.InteropServices;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Temperatura e memória da placa NVIDIA pela NVML, a biblioteca de monitoramento que o próprio driver instala
/// (a mesma usada pelo nvidia-smi), carregada pelo caminho completo em System32. Cada leitura custa microssegundos,
/// sem abrir processos. Sem placa NVIDIA, sem driver ou com erro, tudo fica indisponível (null), sem estimativa.
/// </summary>
public static class NvmlReader
{
    private const int Success = 0;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HandleFn(uint index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TemperatureFn(IntPtr device, int sensor, out uint celsius);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MemoryFn(IntPtr device, out Memory memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct Memory { public ulong Total, Free, Used; }

    private static readonly object Gate = new();
    private static bool _tried;
    private static IntPtr _device;
    private static TemperatureFn? _temperature;
    private static MemoryFn? _memory;

    /// <summary>Se a NVML respondeu (há uma placa NVIDIA com driver).</summary>
    public static bool Available { get { lock (Gate) { EnsureLoaded(); return _device != IntPtr.Zero; } } }

    private static void EnsureLoaded()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, "nvml.dll");
            if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var library)) return;
            T? Export<T>(string name) where T : Delegate => NativeLibrary.TryGetExport(library, name, out var fn) ? Marshal.GetDelegateForFunctionPointer<T>(fn) : null;
            var init = Export<InitFn>("nvmlInit_v2");
            var handle = Export<HandleFn>("nvmlDeviceGetHandleByIndex_v2");
            if (init is null || handle is null || init() != Success || handle(0, out var device) != Success) return;
            _temperature = Export<TemperatureFn>("nvmlDeviceGetTemperature");
            _memory = Export<MemoryFn>("nvmlDeviceGetMemoryInfo");
            _device = device;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or MarshalDirectiveException or SEHException) { _device = IntPtr.Zero; }
    }

    /// <summary>Temperatura do núcleo da GPU em °C (sensor 0 = GPU), ou null.</summary>
    public static double? TemperatureC()
    {
        lock (Gate)
        {
            EnsureLoaded();
            if (_device == IntPtr.Zero || _temperature is null) return null;
            try { return _temperature(_device, 0, out var celsius) == Success && celsius is > 0 and < 130 ? celsius : null; }
            catch (SEHException) { return null; }
        }
    }

    /// <summary>Memória de vídeo usada e total em GB, ou null.</summary>
    public static (double UsedGb, double TotalGb)? MemoryGb()
    {
        lock (Gate)
        {
            EnsureLoaded();
            if (_device == IntPtr.Zero || _memory is null) return null;
            try
            {
                if (_memory(_device, out var m) != Success || m.Total == 0) return null;
                const double gb = 1024d * 1024 * 1024;
                return (m.Used / gb, m.Total / gb);
            }
            catch (SEHException) { return null; }
        }
    }
}
