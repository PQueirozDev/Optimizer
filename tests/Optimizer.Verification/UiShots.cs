using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PQueirozOptimizer;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

/// <summary>
/// Capturas da interface real para a inspeção visual do redesign: a janela abre fora da tela com o monitor ao vivo
/// funcionando e cada página é capturada em vários tamanhos (em pixels independentes de DPI: 1280x720 equivale a
/// 1920x1080 com 150% ou 2560x1440 com 200%). Também procura textos em inglês com o app em português.
///   --ui [--sizes 1366x728,1920x1040] [--pages dashboard,smart] [--theme Dark] [--tall] [--pt-audit]
/// </summary>
internal static class UiShots
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    static string? Option(string[] args, string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();

    static void Wait(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    // Palavras que só existem em inglês (sem termos técnicos usados também em português, como CPU, FPS, ping ou driver)
    static readonly Regex English = new(@"\b(the|and|of|with|your|you|for|from|now|new|live|mode|lab|center|smart|optimize|optimization|performance|analyzer|settings|dashboard|boost|booster|games?|gaming|health|stability|network|storage|memory|usage|load|free|headroom|activity|scan(ned)?|recent(ly)?|run|launch|configure|profiles?|low|high|apply|revert|backup|updates?|license|patch|notes|about|tools|apps|startup|services|debloat|advisor|search|status|online|offline|ready|loading|error|warning|success|enabled|disabled|overview|stats|quick|actions?|details|open|close|save|cancel|back|next|done|start|stop|pause|resume|command|insights?|tweaks?|toggle|light|dark|theme|shortcut|cleaner|check|view|show|hide|more|less|unlock|demo|premium|ultra|release|changelog|report|export|import|compare|capture|record|level|score|library|support|help|feedback|welcome|home)\b",
        RegexOptions.IgnoreCase);
    static readonly Regex Ignore = new(@"https?://|\.exe|\.ini|\.json|Ctrl|NVIDIA|GeForce|Intel|AMD|Ryzen|Radeon|Microsoft|Steam|Epic|Discord|PresentMon|Windows Update|Game Bar|Game Mode|Xbox|Qrztweaks|Pedro Queiroz|PQueiroz");

    public static int Run(string root, string[] args)
    {
        var outDir = Path.Combine(root, "ui");
        Directory.CreateDirectory(outDir);
        var sizes = (Option(args, "--sizes") ?? "1366x728,1920x1040").Split(',').Select(s => s.Split('x')).Select(p => (W: int.Parse(p[0]), H: int.Parse(p[1]))).ToList();
        var pages = (Option(args, "--pages") ?? "dashboard,smart,perflab,optimization,services,apps,startup,drivers,tools,customize,history,resources,fixes,diagnostics,restore,gaming,profiles,network,settings,bios,biosadvisor,patchnotes,about").Split(',');
        var audit = args.Contains("--pt-audit");
        var tall = args.Contains("--tall");

        Program.CreateTestApp();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        // Aparência controlada aqui (só na memória): as capturas não dependem das preferências salvas neste PC
        var look = AppearanceService.Defaults();
        if (Option(args, "--theme") is { } theme) look.Theme = Enum.Parse<ThemeMode>(theme, true);
        var window = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = sizes[0].W, Height = sizes[0].H, ShowActivated = false, ShowInTaskbar = false };
        AppearanceService.Apply(look);
        window.Show();
        if (Application.Current != null) { Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown; Application.Current.MainWindow = window; }
        AppearanceService.Apply(look);
        Wait(6000); // leitura do sistema e algumas amostras do monitor ao vivo
        foreach (Window extra in Application.Current!.Windows.Cast<Window>().Where(w => w != window).ToList()) extra.Close();
        if (window.FindName("TutorialLayer") is UIElement tutorial) tutorial.Visibility = Visibility.Collapsed;

        var found = new SortedDictionary<string, SortedSet<string>>();
        void Collect(DependencyObject node, string page)
        {
            if (node is TextBlock tb && tb.Tag as string != Translator.SystemDataTag && tb.IsVisible && !string.IsNullOrWhiteSpace(tb.Text) && English.IsMatch(tb.Text) && !Ignore.IsMatch(tb.Text))
            {
                if (!found.TryGetValue(tb.Text, out var where)) found[tb.Text] = where = new SortedSet<string>();
                where.Add(page);
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Collect(VisualTreeHelper.GetChild(node, i), page);
        }

        foreach (var (w, h) in sizes)
        {
            window.Width = w; window.Height = h;
            Wait(400);
            foreach (var page in pages)
            {
                typeof(MainWindow).GetMethod("NavigateTo", Flags)!.Invoke(window, new object[] { page });
                Wait(page == "dashboard" ? 2500 : page == "startup" ? 6000 : 900);
                if (window.FindName("TutorialLayer") is UIElement layer) layer.Visibility = Visibility.Collapsed;
                Save(window, Path.Combine(outDir, $"{page}-{w}x{h}.png"), w, tall ? 2600 : h, tall);
                if (audit) Collect(window, page);
                Console.WriteLine($"UI {page} {w}x{h}");
            }
        }
        if (audit)
        {
            Console.WriteLine($"PT-AUDIT {found.Count} textos com palavras em inglês:");
            foreach (var (text, where) in found) Console.WriteLine($"  [{string.Join(",", where)}] {text.Replace("\n", " ⏎ ")}");
        }
        window.Close();
        return 0;
    }

    /// <summary>Captura a janela; com <paramref name="tall"/>, a página inteira (sem a rolagem) numa imagem alta.</summary>
    static void Save(MainWindow window, string path, int width, int height, bool tall)
    {
        var visual = (FrameworkElement)window.Content;
        if (tall)
        {
            visual.Measure(new Size(width, height)); visual.Arrange(new Rect(0, 0, width, height)); visual.UpdateLayout();
        }
        else window.UpdateLayout();
        var w = (int)Math.Round(tall ? width : visual.ActualWidth); var h = (int)Math.Round(tall ? height : visual.ActualHeight);
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        png.Save(stream);
        if (tall) { window.InvalidateMeasure(); window.UpdateLayout(); }
    }
}
