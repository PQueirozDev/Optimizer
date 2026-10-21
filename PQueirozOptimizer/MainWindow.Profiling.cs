using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Medição de desempenho da interface (só com PQO_STARTUP_PROFILE): CPU, uso do motor 3D da placa de vídeo pelo
/// próprio app e memória com o app parado, e o tempo de cada troca de página. Nada aqui assina eventos de
/// renderização (isso obrigaria a janela a redesenhar a cada quadro e distorceria a medição).
/// </summary>
public partial class MainWindow
{
    private static readonly string[] ProfiledPages =
    {
        "perflab", "smart", "optimization", "services", "apps", "startup", "drivers", "tools", "customize", "history",
        "resources", "fixes", "diagnostics", "restore", "gaming", "profiles", "network", "settings", "bios", "biosadvisor",
        "patchnotes", "about", "dashboard", "health", "devices", "stress", "library", "overlay",
    };

    private async Task RunProfilePassAsync()
    {
        static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        using var process = Process.GetCurrentProcess();

        async Task MeasureIdle(string name, int seconds)
        {
            await Task.Delay(3000); // o monitor e a página terminam de carregar
            using var gpu = ProcessGpuCounter.TryCreate(Environment.ProcessId);
            gpu?.Read();
            process.Refresh();
            var cpuStart = process.TotalProcessorTime;
            var clock = Stopwatch.StartNew();
            var gpuSamples = new List<double>();
            for (var i = 0; i < seconds; i++)
            {
                await Task.Delay(1000);
                if (gpu?.Read() is { } g) gpuSamples.Add(g);
            }
            process.Refresh();
            var cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / clock.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            StartupProfiler.Metric($"{name}-cpu-percent", F(cpu));
            StartupProfiler.Metric($"{name}-gpu-3d-percent", gpuSamples.Count == 0 ? "-1" : F(gpuSamples.Average()));
            StartupProfiler.Metric($"{name}-working-set-mb", F(process.WorkingSet64 / 1048576.0));
            StartupProfiler.Metric($"{name}-private-mb", F(process.PrivateMemorySize64 / 1048576.0));
            StartupProfiler.Metric($"{name}-managed-mb", F(GC.GetTotalMemory(false) / 1048576.0));
        }

        if (StartupProfiler.IdleSeconds > 0) await MeasureIdle("idle-dashboard", StartupProfiler.IdleSeconds);
        if (!StartupProfiler.NavigationPass) return;

        // Cada troca: do clique até o quadro seguinte desenhado com a página nova
        var times = new List<double>();
        foreach (var page in ProfiledPages)
        {
            var clock = Stopwatch.StartNew();
            NavigateTo(page);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var ms = clock.Elapsed.TotalMilliseconds;
            times.Add(ms);
            StartupProfiler.Metric($"nav-{page}-ms", F(ms));
            await Task.Delay(page == "startup" ? 2500 : 700); // leituras em segundo plano da página terminam antes da próxima
        }
        times.Sort();
        StartupProfiler.Metric("nav-median-ms", F(times[times.Count / 2]));
        StartupProfiler.Metric("nav-max-ms", F(times[^1]));
        if (StartupProfiler.IdleSeconds > 0)
        {
            NavigateTo("settings");
            await MeasureIdle("idle-settings", Math.Max(5, StartupProfiler.IdleSeconds / 2));
        }
    }

    /// <summary>Uso do motor 3D da placa de vídeo pelo processo (contadores "GPU Engine" do Windows), em %.</summary>
    private sealed class ProcessGpuCounter : IDisposable
    {
        private const uint PdhFmtDouble = 0x00000200, PdhFmtNoCap100 = 0x00008000;
        private const int PdhMoreData = unchecked((int)0x800007D2);
        private IntPtr _query, _counter;

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint count, IntPtr buffer);
        [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);

        [StructLayout(LayoutKind.Sequential)]
        private struct Item { public IntPtr Name; public uint Status; public double Value; }

        public static ProcessGpuCounter? TryCreate(int pid)
        {
            var c = new ProcessGpuCounter();
            if (PdhOpenQuery(null, IntPtr.Zero, out c._query) != 0) return null;
            if (PdhAddEnglishCounter(c._query, $@"\GPU Engine(pid_{pid}_*engtype_3D)\Utilization Percentage", IntPtr.Zero, out c._counter) != 0) { c.Dispose(); return null; }
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
                double sum = 0;
                var itemSize = Marshal.SizeOf<Item>();
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<Item>(buffer + i * itemSize);
                    if (item.Status == 0) sum += item.Value;
                }
                return sum;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Dispose() { if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; } }
    }
}
