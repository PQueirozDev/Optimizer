using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Overlay de FPS no jogo: liga/desliga, posição, tamanho e o que aparece, com prévia ao vivo.</summary>
public partial class MainWindow
{
    // Estático: o overlay continua aberto ao trocar de idioma (a janela principal é recriada)
    private static GameOverlayWindow? _overlay;
    private DispatcherTimer? _overlayPreviewTimer;

    private static readonly (string Key, string Pt, string En)[] OverlayCorners =
    {
        ("TopLeft", "Superior esquerdo", "Top left"), ("TopRight", "Superior direito", "Top right"),
        ("BottomLeft", "Inferior esquerdo", "Bottom left"), ("BottomRight", "Inferior direito", "Bottom right"),
    };

    private void ShowOverlay()
    {
        PageTitle.Text = "Overlay";
        PageBadge.Visibility = Visibility.Collapsed;
        _overlayPreviewTimer?.Stop();
        var settings = OverlaySettings.Load();
        var root = new StackPanel();
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);

        // ---------- Ligar ----------
        var main = new StackPanel();
        var on = _overlay is { IsVisible: true };
        main.Children.Add(CardHeader(Glyphs.Monitor, L("CONTADOR DE FPS", "FPS COUNTER"), L("Overlay no jogo", "In-game overlay"), Pill(on ? L("Ligado", "On") : L("Desligado", "Off"), on ? "Success" : "Info"), titleIsData: false));
        var intro = Label(L("Mostra FPS, tempo de quadro, 1% low e o uso de CPU, GPU e RAM por cima do jogo. Não injeta nada no jogo (seguro com anti-cheat) e o mouse passa direto por ele. Funciona em tela cheia sem bordas ou em janela; na tela cheia exclusiva o jogo fica por cima de qualquer janela.",
            "Shows FPS, frame time, 1% low and CPU, GPU and RAM usage on top of the game. It injects nothing into the game (safe with anti-cheat) and the mouse goes straight through it. Works in borderless fullscreen or windowed; in exclusive fullscreen the game sits above every window."), 12.5, true);
        intro.Tag = Translator.SystemDataTag;
        main.Children.Add(intro);
        var toggle = IconButton(on ? Glyphs.Stop : Glyphs.Play, on ? L("Desligar overlay", "Turn overlay off") : L("Ligar overlay", "Turn overlay on"), primary: true);
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Click += (_, _) => { ToggleOverlay(); KeepScroll(ShowOverlay); };
        main.Children.Add(toggle);
        root.Children.Add(Surface(main));

        // ---------- Prévia ao vivo ----------
        var preview = new StackPanel();
        preview.Children.Add(HomeBandTitle(L("Agora", "Now"), live: true));
        var fps = StatTile(L("FPS · PROGRAMA EM PRIMEIRO PLANO", "FPS · FOREGROUND PROGRAM"));
        var frame = StatTile(L("TEMPO DE QUADRO", "FRAME TIME"));
        var low = StatTile(L("1% LOW", "1% LOW"));
        var process = StatTile(L("PROCESSO", "PROCESS"));
        var tiles = HomeGrid(200);
        foreach (var t in new[] { fps, frame, low, process }) tiles.Children.Add(t.Card);
        preview.Children.Add(tiles);
        var hintText = L("Com o overlay ligado, abra um jogo DirectX (9 a 12): os números aparecem aqui e no canto da tela.", "With the overlay on, open a DirectX (9 to 12) game: the numbers show up here and in the corner of the screen.");
        var hint = Label(hintText, 12, true);
        hint.Tag = Translator.SystemDataTag;
        preview.Children.Add(hint);
        root.Children.Add(preview);

        // ---------- Preferências ----------
        var prefs = new StackPanel();
        prefs.Children.Add(CardHeader(Glyphs.Settings, L("APARÊNCIA", "APPEARANCE"), L("Posição e conteúdo", "Position and content"), null, titleIsData: false));
        prefs.Children.Add(new Border { Height = AppearanceService.Space(8) });
        var corner = Array.FindIndex(OverlayCorners, c => c.Key == settings.Corner);
        prefs.Children.Add(SettingRow(L("Canto da tela", "Screen corner"), L("Onde o overlay fica.", "Where the overlay sits."),
            Segmented(OverlayCorners.Select(c => L(c.Pt, c.En)).ToArray(), Math.Max(0, corner), i => { settings.Corner = OverlayCorners[i].Key; SaveOverlay(settings); KeepScroll(ShowOverlay); })));
        var scales = new[] { 0.85, 1.0, 1.25, 1.5 };
        var scale = Array.FindIndex(scales, s => Math.Abs(s - settings.Scale) < 0.01);
        prefs.Children.Add(SettingRow(L("Tamanho", "Size"), L("Para telas 1440p e 4K, use um tamanho maior.", "For 1440p and 4K screens, use a larger size."),
            Segmented(new[] { L("Pequeno", "Small"), L("Normal", "Normal"), L("Grande", "Large"), L("Maior", "Larger") }, Math.Max(0, scale), i => { settings.Scale = scales[i]; SaveOverlay(settings); KeepScroll(ShowOverlay); })));
        void Switch(string text, bool value, Action<bool> set)
        {
            var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4, 0, 8) };
            box.SetResourceReference(StyleProperty, "SwitchCheckBox");
            box.Checked += (_, _) => { set(true); SaveOverlay(settings); };
            box.Unchecked += (_, _) => { set(false); SaveOverlay(settings); };
            prefs.Children.Add(box);
        }
        Switch(L("Tempo de quadro (ms)", "Frame time (ms)"), settings.ShowFrameTime, v => settings.ShowFrameTime = v);
        Switch(L("1% low", "1% low"), settings.ShowLow, v => settings.ShowLow = v);
        Switch(L("Uso da CPU", "CPU usage"), settings.ShowCpu, v => settings.ShowCpu = v);
        Switch(L("Clock da CPU", "CPU clock"), settings.ShowClock, v => settings.ShowClock = v);
        Switch(L("Uso e temperatura da GPU", "GPU usage and temperature"), settings.ShowGpu, v => settings.ShowGpu = v);
        Switch(L("Uso da RAM", "RAM usage"), settings.ShowRam, v => settings.ShowRam = v);
        root.Children.Add(Surface(prefs));

        void Update()
        {
            var stats = FrameMonitor.Shared.Running ? FrameMonitor.Shared.GetForegroundStats() : null;
            fps.Value.Text = stats is { } s ? $"{s.Fps:0}" : "—";
            frame.Value.Text = stats is { } s2 ? $"{s2.FrameTimeMs:0.0} ms" : "—";
            low.Value.Text = stats is { } s3 ? $"{s3.Low1Fps:0}" : "—";
            process.Value.Text = stats?.ProcessName ?? (FrameMonitor.Shared.Error ?? "—");
            hint.Text = _overlay is { IsVisible: true } && GameOverlayWindow.IsExclusiveFullscreen()
                ? L("O jogo está em tela cheia exclusiva: nesse modo nenhuma janela aparece por cima dele. Mude o jogo para tela cheia sem bordas (ou deixe ligadas as otimizações de tela cheia do Windows) para ver o overlay.",
                    "The game is in exclusive fullscreen: in this mode no window can show on top of it. Switch the game to borderless fullscreen (or keep Windows fullscreen optimizations on) to see the overlay.")
                : hintText;
        }
        Update();
        // Timer desta montagem: o Unloaded da página antiga não pode parar o da nova
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _overlayPreviewTimer = timer;
        timer.Tick += (_, _) => Update();
        timer.Start();
        root.Unloaded += (_, _) => timer.Stop();
    }

    /// <summary>Grava e, com o overlay aberto, reabre para aplicar posição e conteúdo na hora.</summary>
    private static void SaveOverlay(OverlaySettings settings)
    {
        settings.Save();
        if (_overlay is not { IsVisible: true }) return;
        _overlay.Close();
        _overlay = new GameOverlayWindow(settings);
        _overlay.Show();
    }

    private void ToggleOverlay()
    {
        if (_overlay is { IsVisible: true })
        {
            _overlay.Close();
            _overlay = null;
            OperationStatus.Text = L("Overlay desligado.", "Overlay turned off.");
            return;
        }
        _overlay = new GameOverlayWindow(OverlaySettings.Load());
        _overlay.Show();
        OperationStatus.Text = FrameMonitor.Shared.Error is { } error ? L("Overlay ligado, mas o contador de FPS falhou: ", "Overlay on, but the FPS counter failed: ") + error : L("Overlay ligado.", "Overlay on.");
        _log.Write("INFO", "Overlay de FPS ligado" + (FrameMonitor.Shared.Error is { } e ? " (erro do ETW: " + e + ")" : ""));
    }
}
