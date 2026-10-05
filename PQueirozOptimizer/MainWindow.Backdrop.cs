using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Fundo translúcido: o Acrylic do Windows 11 aparece atrás das superfícies semitransparentes do tema.</summary>
public partial class MainWindow
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmSystemBackdropType = 38;
    private const int BackdropNone = 1, BackdropAcrylic = 3;
    private const int WmSettingChange = 0x001A;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointStruct Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private bool _settingsHooked;

    /// <summary>Aplica (ou remove) o fundo translúcido conforme o tema atual; chamado ao abrir e a cada troca de aparência.</summary>
    private void ApplyBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        HookSystemSettings(hwnd);
        if (!ThemeService.TranslucencySupported) return;
        var translucent = ThemeService.IsTranslucent;

        // A janela e o vidro do DWM precisam ficar transparentes para o Acrylic aparecer
        if (WindowChrome.GetWindowChrome(this) is { } chrome) chrome.GlassFrameThickness = translucent ? new Thickness(-1) : new Thickness(0);
        var margins = translucent ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Margins();
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = translucent ? Colors.Transparent : Colors.Black;
        if (translucent) Background = Brushes.Transparent;
        else SetResourceReference(BackgroundProperty, "BackgroundBrush");

        // O Acrylic segue o modo claro/escuro da janela, não o do app: alinha os dois
        var dark = ThemeService.IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        var backdrop = translucent ? BackdropAcrylic : BackdropNone;
        DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref backdrop, sizeof(int));
    }

    /// <summary>
    /// Ao mudar as cores do Windows (efeitos de transparência, modo claro/escuro) reaplica a aparência:
    /// o translúcido liga ou desliga na hora e o tema Automático acompanha o sistema.
    /// </summary>
    private void HookSystemSettings(IntPtr hwnd)
    {
        if (_settingsHooked || HwndSource.FromHwnd(hwnd) is not { } source) return;
        _settingsHooked = true;
        source.AddHook((IntPtr _, int msg, IntPtr _, IntPtr lParam, ref bool _) =>
        {
            if (msg == WmGetMinMaxInfo && lParam != IntPtr.Zero)
            {
                var monitor = MonitorFromWindow(new WindowInteropHelper(this).Handle, MonitorDefaultToNearest);
                if (monitor != IntPtr.Zero)
                {
                    var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                    if (GetMonitorInfo(monitor, ref info))
                    {
                        var data = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                        data.MaxPosition = new PointStruct { X = info.Work.Left - info.Monitor.Left, Y = info.Work.Top - info.Monitor.Top };
                        data.MaxSize = new PointStruct { X = info.Work.Right - info.Work.Left, Y = info.Work.Bottom - info.Work.Top };
                        data.MinTrackSize = new PointStruct
                        {
                            X = Math.Min(data.MinTrackSize.X > 0 ? data.MinTrackSize.X : 760, info.Work.Right - info.Work.Left),
                            Y = Math.Min(data.MinTrackSize.Y > 0 ? data.MinTrackSize.Y : 520, info.Work.Bottom - info.Work.Top)
                        };
                        Marshal.StructureToPtr(data, lParam, false);
                    }
                }
            }
            if (msg == WmSettingChange && lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
                Dispatcher.BeginInvoke(() => AppearanceService.Apply(AppearanceService.Current));
            return IntPtr.Zero;
        });
    }
}
