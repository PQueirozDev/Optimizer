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

    // App com recursos e estilos, mas sem a inicialização real (licença, idioma salvo, janela principal)
    static App CreateTestApp()
    {
        var app = new App(); app.InitializeComponent();
        var key = typeof(Application).GetField("EVENT_STARTUP", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var events = (System.ComponentModel.EventHandlerList)typeof(Application).GetProperty("Events", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        events[key] = null;
        return app;
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/verification");
            Directory.CreateDirectory(root);
            var log = new ActivityLog(Path.Combine(root, "verification.log"));
            var bridge = new PowerShellBridge(log);
            Assert(bridge.GetSteps("padrao").Count == 7, "Plano padrão contém 7 etapas");
            Assert(bridge.GetSteps("gamer").Count == 17, "Plano avançado contém 17 etapas");
            Assert(!UpdateService.CanAutoInstall(new UpdateInfo(true, "1.0.0", "1.1.0", null, "https://github.com/PQueirozDev/Optimizer/releases/download/v1.1.0/Setup.exe", "Setup.exe", null)), "Atualização sem hash publicado não é instalada automaticamente");
            Assert(new LicenseService().MachineId.Length == 20, "ID do computador gerado");
            Assert(new LicenseService().DisplayMachineId.Replace("-", "") == new LicenseService().MachineId, "ID exibido em blocos equivale ao ID real");
            var sampleKey = "PQO1-eyJQcm9kdWN0IjoiUFEifQ." + new string('A', 342);
            Assert(LicenseService.ExtractKey($"Sua chave:\n{sampleKey[..60]}\n{sampleKey[60..]}\nObrigado!") == sampleKey, "Chave extraída de mensagem com quebras de linha");
            Assert(LicenseService.ExtractKey("sem chave aqui") is null, "Texto sem chave é ignorado");
            Assert(!new LicenseService().TryActivate(sampleKey, out _, out _), "Chave com assinatura falsa é recusada");
            Assert(bridge.GetSteps("debloat").Count == 19, "Plano debloat contém 19 etapas, limpeza usa análise separada");
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

            // Tradução da saída do script e das classificações do console ao vivo
            Translator.IsEnglish = true;
            Assert(Translator.Tr("Limpando cache DNS") == "Clearing DNS cache", "Nome de etapa traduzido");
            Assert(Translator.Tr("Resumo: 5 aplicado(s), 0 falha(s)") == "Summary: 5 applied, 0 failed", "Resumo do script traduzido");
            Assert(MainWindow.ClassifyLine("  [3/16]    [OK] Limpando cache DNS") is { Tone: "Success", Text: "Limpando cache DNS" }, "Linha de sucesso classificada");
            Assert(MainWindow.ClassifyLine("[FALHA] Cortana removida: acesso negado") is { Tone: "Danger" }, "Linha de falha classificada");
            Assert(MainWindow.ClassifyLine("======================") is null, "Moldura decorativa ignorada");
            Assert(Translator.Tr("Cortana removida: acesso negado").StartsWith("Cortana removed: "), "Rótulo antes de dois-pontos traduzido");
            Assert(Translator.Tr("  ● Disponível para download oficial") == "  ● Available for official download", "Símbolo e espaços preservados");
            Translator.IsEnglish = false;
            Assert(Translator.Tr("Limpando cache DNS") == "Limpando cache DNS", "Português permanece sem alteração");

            // Inicialização: formato das chaves StartupApproved (mesmo do Gerenciador de Tarefas)
            Assert(StartupService.IsEnabled(null) && StartupService.IsEnabled(new byte[] { 2, 0, 0, 0 }) && !StartupService.IsEnabled(new byte[] { 3, 0, 0, 0 }), "Estado de inicialização lido corretamente");
            Assert(StartupService.BuildApprovedValue(false, DateTime.UtcNow) is { Length: 12 } off && off[0] == 3 && BitConverter.ToInt64(off, 4) > 0, "Desativar grava 03 + data");
            Assert(StartupService.BuildApprovedValue(true, DateTime.UtcNow) is { Length: 12 } on && on[0] == 2, "Ativar grava 02");
            Assert(StartupService.ExecutablePath("\"C:\\Program Files\\App\\a.exe\" --min") == @"C:\Program Files\App\a.exe", "Caminho entre aspas extraído");
            Assert(StartupService.ExecutablePath(@"C:\Tools\b.exe -silent") == @"C:\Tools\b.exe", "Caminho sem aspas extraído");

            if (args.Contains("--i18n"))
            {
                var app = CreateTestApp();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Translator.Attach();
                Translator.IsEnglish = true;
                var window = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
                typeof(MainWindow).GetField("_loc", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!.GetType().GetMethod("SetLanguage")!.Invoke(typeof(MainWindow).GetField("_loc", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window), new object[] { "en" });
                window.Show();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                void Wait(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
                var portuguese = new System.Text.RegularExpressions.Regex(@"[áàâãéêíóôõúçÁÉÍÓÚÇÃÕ]|\b(de|do|da|dos|das|para|com|seu|sua|você|não|nao|um|uma|os|pelo|pela|ou|está|são|ativo|ativa|ativar|desativado|pronto|salvar|excluir|abrir|revisar|baixar|voltar|copiar|sair|limpar|todos|todas|nenhum|nenhuma|\w+ando|\w+endo|\w+cao|\w+coes)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var ignore = new System.Text.RegularExpressions.Regex(@"Pedro Queiroz|PQueiroz|pqueiroz|\\|NVIDIA|Intel|AMD|Microsoft Windows|GeForce|Ryzen|Radeon|https?://|\.exe|\.lnk");
                var found = new SortedSet<string>();
                void Collect(DependencyObject node)
                {
                    if (node is System.Windows.Controls.TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text) && portuguese.IsMatch(tb.Text) && !ignore.IsMatch(tb.Text)) found.Add(tb.Text.Replace("\n", " ⏎ "));

                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Collect(VisualTreeHelper.GetChild(node, i));
                }
                void Snap(string name) { var visual = (FrameworkElement)window.Content; var bmp = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32); bmp.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using var fs = File.Create(Path.Combine(root, name + ".png")); png.Save(fs); }
                Wait(4000);
                // O App do teste dispara a inicialização real (idioma salvo + outra janela); desfaz isso
                if (Application.Current != null) { Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown; Application.Current.MainWindow = window; }
                foreach (Window extra in (Application.Current?.Windows.Cast<Window>() ?? Enumerable.Empty<Window>()).Where(w => w != window).ToList()) extra.Close();
                Translator.IsEnglish = true;
                foreach (var page in new[] { "dashboard", "optimization", "startup", "drivers", "tools", "history", "settings", "about", "patchnotes", "bios" })
                {
                    typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { page });
                    Wait(page == "dashboard" ? 4000 : 600);
                    Collect(window); Snap("en-" + page);
                }
                foreach (var op in new[] { "padrao", "gamer", "debloat", "quickclean" })
                {
                    ((Task)typeof(MainWindow).GetMethod("PrepareOperationAsync", flags)!.Invoke(window, new object[] { op })!).ContinueWith(_ => { });
                    Wait(op == "quickclean" ? 6000 : 600);
                    Collect(window); Snap("en-review-" + op);
                }
                ((Task)typeof(MainWindow).GetMethod("RunLiveAsync", flags)!.Invoke(window, new object?[] { "analisar", null })!).ContinueWith(_ => { });
                Wait(8000); Collect(window); Snap("en-live-analisar");
                Console.WriteLine($"I18N {found.Count} textos possivelmente em português:");
                foreach (var text in found) Console.WriteLine("  " + text);
                window.Close();
            }

            if (args.Contains("--render"))
            {
                var app = CreateTestApp();
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
                foreach (var page in new[] { "drivers", "tools", "settings", "about", "patchnotes", "bios" })
                {
                    typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { page });
                    Render(page + "-dark", 1320, 860);
                }
                typeof(MainWindow).GetMethod("ApplyTheme", flags)!.Invoke(window, new object[] { false, false });
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "dashboard" });
                Pump((Task)typeof(MainWindow).GetMethod("RenderDashboardAsync", flags)!.Invoke(window, null)!);
                Render("dashboard-light", 1320, 860);
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "optimization" });
                Render("optimizations-light", 1320, 860);
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}




