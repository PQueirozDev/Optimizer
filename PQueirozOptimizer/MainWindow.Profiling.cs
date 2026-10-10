using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Medição de desempenho da interface (só com PQO_STARTUP_PROFILE): CPU e memória com o app parado,
/// quadros desenhados por segundo (animações contínuas aparecem aqui) e o tempo de cada troca de página.
/// </summary>
public partial class MainWindow
{
    private static readonly string[] ProfiledPages =
    {
        "perflab", "smart", "optimization", "services", "apps", "startup", "drivers", "tools", "customize", "history",
        "resources", "fixes", "diagnostics", "restore", "gaming", "profiles", "network", "settings", "bios", "biosadvisor",
        "patchnotes", "about", "dashboard",
    };

    private async Task RunProfilePassAsync()
    {
        static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        using var process = Process.GetCurrentProcess();

        async Task MeasureIdle(string name, int seconds)
        {
            await Task.Delay(3000); // o monitor e a página terminam de carregar
            var frames = 0;
            EventHandler onFrame = (_, _) => frames++;
            process.Refresh();
            var cpuStart = process.TotalProcessorTime;
            var clock = Stopwatch.StartNew();
            CompositionTarget.Rendering += onFrame;
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            CompositionTarget.Rendering -= onFrame;
            process.Refresh();
            var cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / clock.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            StartupProfiler.Metric($"{name}-cpu-percent", F(cpu));
            StartupProfiler.Metric($"{name}-frames-per-second", F(frames / clock.Elapsed.TotalSeconds));
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
}
