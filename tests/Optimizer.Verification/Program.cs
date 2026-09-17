using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PQueirozOptimizer;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

internal static class Program
{
    static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/verification");
            Directory.CreateDirectory(root);
            var log = new ActivityLog(Path.Combine(root, "verification.log"));
            var bridge = new PowerShellBridge(log);
            Assert(bridge.GetSteps("padrao").Count == 5, "Plano padrão contém 5 etapas");
            Assert(bridge.GetSteps("gamer").Count == 12, "Plano avançado contém 12 etapas");
            Assert(bridge.GetSteps("debloat").Count == 15, "Plano debloat contém 15 etapas, limpeza usa análise separada");
            var configPath = Path.Combine(root, "config-test.json");
            File.WriteAllText(configPath, """{"activeProfile":"Modo Gamer","profiles":[{"name":"Modo Gamer","enabledOptimizations":null}]}""");
            var config = new ConfigService(configPath);
            Assert(config.Config.ActiveProfile == "Modo Gamer", "Perfil gamer preservado na carga");
            Assert(config.GetActiveProfile().EnabledOptimizations.Count > 0, "Lista nula de ajustes recuperada");
            config.Save();
            Assert(new ConfigService(configPath).Config.ActiveProfile == "Modo Gamer", "Perfil persiste após salvar");
            var driver = new DriverService();
            SystemSnapshot Snapshot(string gpu) => new("Windows", "1", "x64", "CPU", gpu, "16 GB", "100 GB", "50 GB", "1h", false);
            Assert(!driver.GetAllDrivers(Snapshot("AMD Radeon"))[0].IsRecommendedForCurrentHardware, "NVIDIA não recomendado para AMD");
            Assert(driver.GetAllDrivers(Snapshot("NVIDIA GeForce"))[0].IsRecommendedForCurrentHardware, "NVIDIA recomendado para GPU compatível");

            var cleanRoot = Path.Combine(root, "clean-fixtures");
            Directory.CreateDirectory(cleanRoot);
            var old = Path.Combine(cleanRoot, "old.tmp");
            var recent = Path.Combine(cleanRoot, "recent.tmp");
            var changed = Path.Combine(cleanRoot, "changed.tmp");
            var outside = Path.Combine(root, "outside.tmp");
            foreach (var file in new[] { old, recent, changed, outside }) File.WriteAllText(file, "fixture");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
            File.SetLastWriteTimeUtc(changed, DateTime.UtcNow.AddDays(-3));
            CleanFile Entry(string path) { var info = new FileInfo(path); return new(path, info.Length, info.LastWriteTimeUtc); }
            var entries = new[] { Entry(old), Entry(recent), Entry(changed), Entry(outside) };
            File.AppendAllText(changed, "changed");
            var result = new QuickCleanService(log).CleanAsync(new[] { new CleanCategory("Teste", cleanRoot, entries, 0) }).GetAwaiter().GetResult();
            Assert(result.Removed == 1 && !File.Exists(old), "Limpeza remove somente arquivo antigo aprovado");
            Assert(File.Exists(recent) && File.Exists(changed) && File.Exists(outside), "Limpeza preserva recente, alterado e fora da raiz");

            if (args.Contains("--render"))
            {
                var app = new App(); app.InitializeComponent();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher)); var window = new MainWindow();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                void Pump(Task task)
                {
                    var frame = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                    timer.Tick += (_, _) => { if (task.IsCompleted) frame.Continue = false; };
                    timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
                    task.GetAwaiter().GetResult();
                }
                Pump((Task)typeof(MainWindow).GetMethod("RenderDashboardAsync", flags)!.Invoke(window, null)!);
                void Render(string name, int width, int height)
                {
                    var visual = (FrameworkElement)window.Content;
                    visual.Measure(new Size(width, height)); visual.Arrange(new Rect(0, 0, width, height)); visual.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(root, name + ".png")); png.Save(stream);
                    Console.WriteLine("RENDER " + name);
                }
                Render("dashboard-dark", 1320, 860);
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "optimization" });
                Render("optimizations-dark", 1060, 700);
                Pump((Task)typeof(MainWindow).GetMethod("PrepareOperationAsync", flags)!.Invoke(window, new object[] { "gamer" })!);
                Render("review-dark", 1060, 700);
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "history" });
                Render("history-dark", 1320, 860);
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}




