using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace PQueirozOptimizer;

/// <summary>
/// Minimizar para a bandeja (Configurações → Janela): ao minimizar, o app sai da barra de tarefas e fica como ícone
/// perto do relógio. Feito direto com Shell_NotifyIcon para não misturar Windows Forms no app WPF.
/// </summary>
public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public int Id;
        public int Flags;
        public int CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public int State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public int TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public int InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ExtractIcon(IntPtr instance, string file, int index);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    private const int NimAdd = 0, NimDelete = 2;
    private const int NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4;
    private const int TrayCallback = 0x8000 + 0x51; // WM_APP + n
    private const int WmLButtonUp = 0x0202, WmLButtonDblClk = 0x0203, WmRButtonUp = 0x0205;

    private IntPtr _trayIcon;
    private bool _trayShown, _trayHooked;

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Chamado quando a janela muda de estado: minimizada com a opção ligada, vai para a bandeja.</summary>
    private void HandleTrayOnStateChanged()
    {
        if (WindowState == WindowState.Minimized && _configService.Config.MinimizeToTray) HideToTray();
    }

    private void HideToTray()
    {
        if (Handle == IntPtr.Zero) return;
        if (!_trayHooked) { HwndSource.FromHwnd(Handle)?.AddHook(TrayHook); _trayHooked = true; }
        if (!_trayShown)
        {
            if (_trayIcon == IntPtr.Zero) _trayIcon = ExtractIcon(IntPtr.Zero, Environment.ProcessPath ?? "", 0);
            var data = TrayData();
            data.Flags = NifMessage | NifIcon | NifTip;
            data.CallbackMessage = TrayCallback;
            data.Icon = _trayIcon;
            data.Tip = Services.Translator.Tr(_operationRunning ? "Qrztweaks — operação em andamento" : "Qrztweaks — clique para abrir");
            _trayShown = Shell_NotifyIcon(NimAdd, ref data);
        }
        // Só esconde se o ícone apareceu: sem ele, o app ficaria inacessível. Hide em vez de ShowInTaskbar = false,
        // que deixa uma barrinha de título minimizada no canto da tela
        if (_trayShown) Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        SetForegroundWindow(Handle);
        RemoveTrayIcon();
    }

    private void RemoveTrayIcon()
    {
        if (!_trayShown) return;
        var data = TrayData();
        Shell_NotifyIcon(NimDelete, ref data);
        _trayShown = false;
    }

    /// <summary>No fechamento: tira o ícone (senão ele fica "fantasma" até passar o mouse) e libera o HICON.</summary>
    private void DisposeTray()
    {
        RemoveTrayIcon();
        if (_trayIcon != IntPtr.Zero) { DestroyIcon(_trayIcon); _trayIcon = IntPtr.Zero; }
    }

    private NotifyIconData TrayData() => new() { Size = Marshal.SizeOf<NotifyIconData>(), Window = Handle, Id = 1, Tip = "", Info = "", InfoTitle = "" };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
    private static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    private IntPtr TrayHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // O Explorer reiniciou (Personalizar Windows faz isso): a barra nova não tem o ícone, então ele volta
        if (msg == TaskbarCreated && _trayShown) { _trayShown = false; HideToTray(); return IntPtr.Zero; }
        if (msg != TrayCallback) return IntPtr.Zero;
        switch (lParam.ToInt32() & 0xFFFF)
        {
            case WmLButtonUp:
            case WmLButtonDblClk:
                RestoreFromTray(); handled = true; break;
            case WmRButtonUp:
                ShowTrayMenu(); handled = true; break;
        }
        return IntPtr.Zero;
    }

    /// <summary>Configurações → Janela.</summary>
    private Border WindowBehaviorSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Janela", "Como o app se comporta quando você não está usando."));
        var check = new CheckBox { IsChecked = _configService.Config.MinimizeToTray, Content = "" };
        check.SetResourceReference(StyleProperty, "SwitchCheckBox");
        void Save(bool enabled)
        {
            try { _configService.SaveMinimizeToTray(enabled); }
            catch (System.IO.IOException ex) { ShowToast("Preferências", ex.Message, "Warning"); }
        }
        check.Checked += (_, _) => Save(true);
        check.Unchecked += (_, _) => Save(false);
        panel.Children.Add(SettingRow("Minimizar para a bandeja", "Ao minimizar, o app sai da barra de tarefas e fica só como ícone perto do relógio. Clique no ícone para abrir de novo.", check));
        return Surface(panel);
    }

    private void ShowTrayMenu()
    {
        var open = new MenuItem { Header = Services.Translator.Tr("Abrir o Qrztweaks") };
        open.Click += (_, _) => RestoreFromTray();
        var exit = new MenuItem { Header = Services.Translator.Tr("Sair") };
        exit.Click += (_, _) => { RestoreFromTray(); Close(); };
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint, Items = { open, exit } };
        // A janela está escondida: o menu precisa do foco do app para fechar ao clicar fora
        SetForegroundWindow(Handle);
        menu.IsOpen = true;
    }
}
