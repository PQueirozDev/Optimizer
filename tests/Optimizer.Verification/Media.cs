using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PQueirozOptimizer;

/// <summary>
/// Imagens e vídeo do site, feitos com a interface real: o app abre fora da tela (com animações e o monitor
/// ao vivo funcionando) e é capturado quadro a quadro. --shots gera as fotos; --tour grava o vídeo (ffmpeg).
/// </summary>
internal static class Media
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    static MainWindow OpenWindow(int width, int height)
    {
        Program.CreateTestApp();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = width, Height = height, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        if (Application.Current != null) { Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown; Application.Current.MainWindow = window; }
        Wait(1500);
        // Fecha a janela extra que a inicialização real do App de teste possa ter aberto
        foreach (Window extra in Application.Current!.Windows.Cast<Window>().Where(w => w != window).ToList()) extra.Close();
        return window;
    }

    static void Wait(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    static void Navigate(MainWindow w, string page, int? gamingTab = null)
    {
        if (gamingTab is { } tab) typeof(MainWindow).GetField("_gamingTab", Flags)!.SetValue(w, tab);
        typeof(MainWindow).GetMethod("NavigateTo", Flags)!.Invoke(w, new object[] { page });
    }

    static void Theme(MainWindow w, bool dark) => typeof(MainWindow).GetMethod("ApplyTheme", Flags)!.Invoke(w, new object[] { dark, false });

    static BitmapSource Capture(MainWindow w, int outWidth, int outHeight)
    {
        var visual = (FrameworkElement)w.Content;
        var dpi = 96.0 * outWidth / visual.ActualWidth;
        var bitmap = new RenderTargetBitmap(outWidth, outHeight, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    static void SaveJpeg(BitmapSource image, string path, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // ================= Fotos =================
    public static int Shots(string root)
    {
        Directory.CreateDirectory(root);
        var w = OpenWindow(1440, 900);
        Wait(6000); // painel lido e monitor com algumas leituras
        var pages = new (string Name, string Page, int? Tab, bool Dark)[]
        {
            ("visao-geral", "dashboard", null, true), ("modo-jogo", "gaming", 0, true), ("jogos", "gaming", 1, true),
            ("nvidia", "gaming", 2, true), ("sistema", "gaming", 3, true), ("rede", "network", null, true),
            ("drivers", "drivers", null, true), ("bios", "bios", null, true), ("otimizacoes", "optimization", null, true), ("personalizar", "customize", null, true),
            ("visao-geral-claro", "dashboard", null, false),
        };
        foreach (var (name, page, tab, dark) in pages)
        {
            Theme(w, dark);
            Navigate(w, page, tab);
            Wait(page == "gaming" && tab == 3 ? 5000 : 1600); // a cascata termina; o Defender é lido em segundo plano
            SaveJpeg(Capture(w, 2160, 1350), Path.Combine(root, name + ".jpg"), 90);
            Console.WriteLine("SHOT " + name);
        }
        w.Close();
        return 0;
    }

    // ================= Cenas do vídeo de apresentação =================
    /// <summary>
    /// Cenas 16:9 em alta resolução (folga para o movimento de câmera) e a legenda de cada uma em PNG
    /// transparente. tools\Make-SiteVideo.ps1 junta tudo no ffmpeg com zoom suave e transições.
    /// </summary>
    public static int VideoShots(string root)
    {
        Directory.CreateDirectory(root);
        var w = OpenWindow(1440, 810);
        Wait(6500);
        var scenes = new (string Page, int? Tab, bool Dark, string Title, string Subtitle)[]
        {
            ("dashboard", null, true, "Visão geral ao vivo", "Saúde do PC, CPU, GPU, RAM e ping em tempo real"),
            ("gaming", 0, true, "Modo Jogo", "Fecha apps e pausa serviços enquanto você joga — e devolve tudo ao sair"),
            ("gaming", 1, true, "Configurações dos jogos", "Presets competitivos para Fortnite, Apex, CS2 e Rocket League"),
            ("gaming", 2, true, "Perfil NVIDIA", "Baixa latência e desempenho máximo gravados direto no driver"),
            ("gaming", 3, true, "Plano de energia Qrz", "Baixa latência em um clique, com volta ao plano anterior"),
            ("network", null, true, "Rede", "Teste de velocidade, DNS mais rápido e ajustes de latência"),
            ("drivers", null, true, "Instalação limpa de driver", "DDU, reinício e o driver novo instalado sozinho"),
            ("bios", null, true, "Editor de BIOS", "Leia e ajuste a BIOS pelo Windows, com cópia original"),
            ("optimization", null, true, "Otimizações revisáveis", "Você vê cada ajuste antes de aplicar — tudo com backup"),
            ("dashboard", null, false, "Tema claro e escuro", "PQueiroz Optimizer"),
        };
        for (var i = 0; i < scenes.Length; i++)
        {
            var (page, tab, dark, title, subtitle) = scenes[i];
            Theme(w, dark);
            Navigate(w, page, tab);
            Wait(page == "gaming" && tab == 3 ? 5000 : 2200);
            SaveJpeg(Capture(w, 2880, 1620), Path.Combine(root, $"scene{i:D2}.jpg"), 93);
            SavePng(Caption(title, subtitle), Path.Combine(root, $"caption{i:D2}.png"));
            Console.WriteLine("SCENE " + title);
        }
        w.Close();
        return 0;
    }

    static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Legenda no canto inferior esquerdo: cartão de vidro escuro com barra em gradiente.</summary>
    static BitmapSource Caption(string title, string subtitle)
    {
        const int width = 1920, height = 1080;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var typeface = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var subFace = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
            var t = new FormattedText(title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 46, Brushes.White, 1.0);
            var s = new FormattedText(subtitle, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, subFace, 26, new SolidColorBrush(Color.FromRgb(0xC9, 0xCF, 0xE0)), 1.0);
            var boxW = Math.Max(t.Width, s.Width) + 96;
            const double boxH = 150, x = 72, y = height - boxH - 72;
            var violet = Color.FromRgb(0x8B, 0x5C, 0xF6); var cyan = Color.FromRgb(0x22, 0xD3, 0xEE);
            // Sombra suave e cartão
            for (var i = 6; i >= 1; i--) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(10 * (7 - i)), 0, 0, 0)), null, new Rect(x - i * 3, y - i * 3 + 8, boxW + i * 6, boxH + i * 6), 26 + i * 3, 26 + i * 3);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0x0C, 0x0E, 0x16)), new Pen(new LinearGradientBrush(Color.FromArgb(0xC0, violet.R, violet.G, violet.B), Color.FromArgb(0x60, cyan.R, cyan.G, cyan.B), 0), 2), new Rect(x, y, boxW, boxH), 26, 26);
            dc.DrawRoundedRectangle(new LinearGradientBrush(violet, cyan, 90), null, new Rect(x + 26, y + 32, 6, boxH - 64), 3, 3);
            dc.DrawText(t, new Point(x + 56, y + 26));
            dc.DrawText(s, new Point(x + 56, y + 26 + t.Height + 6));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    // ================= Vídeo =================
    public static int Tour(string root)
    {
        var frames = Path.Combine(root, "frames");
        if (Directory.Exists(frames)) Directory.Delete(frames, true);
        Directory.CreateDirectory(frames);
        const int outW = 1600, outH = 900;
        var w = OpenWindow(1440, 810);
        Wait(6500);
        var scroll = (ScrollViewer)typeof(MainWindow).GetField("ContentScroll", Flags)!.GetValue(w)!;
        var toast = typeof(MainWindow).GetMethod("ShowToast", Flags)!;

        // Roteiro: (segundo, ação). Rolagens são suaves, feitas a cada quadro.
        double scrollFrom = 0, scrollTo = 0, scrollStart = 0, scrollEnd = 0;
        void ScrollBetween(double from, double to, double start, double seconds) { scrollFrom = from; scrollTo = to; scrollStart = start; scrollEnd = start + seconds; }
        var script = new List<(double At, Action Run)>
        {
            (0.0, () => Navigate(w, "dashboard")),
            (2.6, () => toast.Invoke(w, new object[] { "Busca rápida", "Pressione Ctrl+K para abrir qualquer recurso.", "Info" })),
            (5.0, () => Navigate(w, "gaming", 0)), (6.2, () => ScrollBetween(0, 380, 6.2, 2.2)),
            (9.0, () => Navigate(w, "gaming", 1)), (10.3, () => ScrollBetween(0, 520, 10.3, 2.4)),
            (13.3, () => Navigate(w, "gaming", 2)), (14.4, () => ScrollBetween(0, 300, 14.4, 2.0)),
            (17.0, () => Navigate(w, "gaming", 3)),
            (20.0, () => Navigate(w, "network")), (21.4, () => ScrollBetween(0, 260, 21.4, 1.8)),
            (24.0, () => Navigate(w, "drivers")),
            (27.0, () => Navigate(w, "bios")), (28.3, () => ScrollBetween(0, 280, 28.3, 1.8)),
            (31.0, () => { Theme(w, false); Navigate(w, "dashboard"); }),
            (34.0, () => { Theme(w, true); Navigate(w, "dashboard"); }),
        };
        const double total = 37.0;

        var clock = Stopwatch.StartNew();
        var stamps = new List<double>();
        var pending = new List<Task>();
        var next = 0;
        var done = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            var t = clock.Elapsed.TotalSeconds;
            while (next < script.Count && script[next].At <= t) script[next++].Run();
            if (t >= scrollStart && t <= scrollEnd + 0.05 && scrollEnd > scrollStart)
            {
                var p = Math.Clamp((t - scrollStart) / (scrollEnd - scrollStart), 0, 1);
                var eased = p < 0.5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2;
                scroll.ScrollToVerticalOffset(scrollFrom + (scrollTo - scrollFrom) * eased);
            }
            // Só a captura fica na thread da interface; a compressão roda em paralelo
            var shot = Capture(w, outW, outH); shot.Freeze();
            var file = Path.Combine(frames, $"{stamps.Count:D5}.jpg");
            pending.Add(Task.Run(() => SaveJpeg(shot, file, 92)));
            stamps.Add(t);
            if (t >= total) { timer.Stop(); done.Continue = false; }
        };
        timer.Start();
        Dispatcher.PushFrame(done);
        Task.WaitAll(pending.ToArray());
        w.Close();
        Console.WriteLine($"TOUR {stamps.Count} quadros em {total:0} s ({stamps.Count / total:0.0} q/s)");

        // Lista do ffmpeg com a duração real de cada quadro: o vídeo fica na velocidade real
        var list = new StringBuilder();
        for (var i = 0; i < stamps.Count; i++)
        {
            var duration = (i + 1 < stamps.Count ? stamps[i + 1] : stamps[i] + 0.04) - stamps[i];
            list.Append($"file 'frames/{i:D5}.jpg'\nduration {duration.ToString("0.0000", CultureInfo.InvariantCulture)}\n");
        }
        list.Append($"file 'frames/{stamps.Count - 1:D5}.jpg'\n");
        File.WriteAllText(Path.Combine(root, "frames.txt"), list.ToString());
        return 0;
    }
}
